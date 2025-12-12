using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using ResearchApi.Configuration;
using ResearchApi.Domain;

namespace ResearchApi.Infrastructure;

public sealed class LearningEmbeddingService(
    IEmbeddingModel embeddingModel,
    IDbContextFactory<ResearchDbContext> dbContextFactory,
    IOptions<LearningSimilarityOptions> similarityOptions,
    ILogger<LearningEmbeddingService> logger)
    : ILearningEmbeddingService
{
    readonly LearningSimilarityOptions _options = similarityOptions.Value;
    
    public async Task<IReadOnlyList<Learning>> PopulateEmbeddingsAsync(
        IEnumerable<Learning> learnings,
        CancellationToken ct = default)
    {
        if (learnings is null)
            return Array.Empty<Learning>();

        var learningList = learnings as IList<Learning> ?? learnings.ToList();
        if (learningList.Count == 0)
            return Array.Empty<Learning>();

        var toEmbed = learningList
            .Where(l => l.Embedding is null)
            .ToList();

        if (toEmbed.Count == 0)
        {
            logger.LogDebug("All {Count} learnings already have embeddings.", learningList.Count);
            return Array.Empty<Learning>();
        }

        logger.LogInformation("Computing embeddings for {Count} learnings.", toEmbed.Count);

        var texts   = toEmbed.Select(l => l.Text).ToList();
        var vectors = await embeddingModel.GenerateEmbeddingsAsync(texts, ct);

        var count = Math.Min(toEmbed.Count, vectors.Count);
        for (var i = 0; i < count; i++)
        {
            toEmbed[i].Embedding = new Vector(vectors[i].Vector);
        }

        logger.LogInformation("Stored embeddings for {Count} learnings.", count);

        return toEmbed;
    }

    public async Task<IReadOnlyList<Learning>> GetSimilarLearningsAsync(
        string queryText,
        Guid? jobId          = null,
        string? queryHash    = null,
        string? language     = null,
        string? region       = null,
        int   topK           = 20,
        CancellationToken ct = default)
    {
        logger.LogDebug("[GetSimilarLearningsAsync] Calling tool with parameters: queryText={queryText}, jobId={jobId}, language{language}, region={region}",
             queryText, jobId, language, region);

        if (string.IsNullOrWhiteSpace(queryText))
            return Array.Empty<Learning>();

        var embeddingResult = await embeddingModel.GenerateEmbeddingAsync(queryText, ct);
        if (embeddingResult is null)
            return Array.Empty<Learning>();

        var queryVector = new Vector(embeddingResult.Vector);
        var maxK        = Math.Clamp(topK, 1, 200);

        var localMinImportance          = _options.LocalMinImportance;
        var globalMinImportance         = _options.GlobalMinImportance;
        var minLocalFractionForNoGlobal = _options.MinLocalFractionForNoGlobal;

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        IQueryable<Learning> BaseLocalQuery()
        {
            var q = db.Learnings
                .Include(l => l.Page)
                .Where(l => l.Embedding != null);

            if (jobId is Guid jid)
            {
                q = q.Where(l => l.JobId == jid);
            }

            if (!string.IsNullOrWhiteSpace(queryHash))
            {
                q = q.Where(l => l.QueryHash == queryHash);
            }

            return q;
        }

        IQueryable<Learning> BaseGlobalQuery() =>
            db.Learnings
              .Include(l => l.Page)
              .Where(l => l.Embedding != null);

        async Task<List<Learning>> RunQueryAsync(
            IQueryable<Learning> baseQuery,
            bool useLanguageFilter,
            bool useRegionFilter,
            float minImportance,
            int limit,
            IEnumerable<Guid>? excludeIds = null)
        {
            var q = baseQuery;

            if (useLanguageFilter && !string.IsNullOrWhiteSpace(language))
            {
                q = q.Where(l => l.Page.Language == language);
            }

            if (useRegionFilter && !string.IsNullOrWhiteSpace(region))
            {
                q = q.Where(l => l.Page.Region == region);
            }

            q = q.Where(l => l.ImportanceScore >= minImportance);

            if (excludeIds is not null)
            {
                var excluded = excludeIds as ICollection<Guid> ?? excludeIds.ToList();
                if (excluded.Count > 0)
                {
                    q = q.Where(l => !excluded.Contains(l.Id));
                }
            }

            // vector similarity first, then importance desc
            q = q
                .OrderBy(l => l.Embedding!.CosineDistance(queryVector))
                .ThenByDescending(l => l.ImportanceScore)
                .Take(Math.Clamp(limit, 1, maxK));

            return await q.AsNoTracking().ToListAsync(ct);
        }

        var useLangFilter   = !string.IsNullOrWhiteSpace(language);
        var useRegionFilter = !string.IsNullOrWhiteSpace(region);

        // ========== 1) LOCAL SEARCH ==========

        var localQuery  = BaseLocalQuery();
        var localResults = new List<Learning>();

        // 1a) with language + region (if provided)
        localResults.AddRange(await RunQueryAsync(
            localQuery,
            useLanguageFilter: useLangFilter,
            useRegionFilter:   useRegionFilter,
            minImportance:     localMinImportance,
            limit:             maxK));

        // 1b) drop region
        if (localResults.Count < maxK && useRegionFilter)
        {
            localResults.AddRange(await RunQueryAsync(
                localQuery,
                useLanguageFilter: useLangFilter,
                useRegionFilter:   false,
                minImportance:     localMinImportance,
                limit:             maxK - localResults.Count,
                excludeIds:        localResults.Select(l => l.Id)));
        }

        // 1c) drop language too
        if (localResults.Count < maxK && useLangFilter)
        {
            localResults.AddRange(await RunQueryAsync(
                localQuery,
                useLanguageFilter: false,
                useRegionFilter:   false,
                minImportance:     localMinImportance,
                limit:             maxK - localResults.Count,
                excludeIds:        localResults.Select(l => l.Id)));
        }

        var minLocalNeeded = (int)Math.Ceiling(maxK * minLocalFractionForNoGlobal);
        if (localResults.Count >= minLocalNeeded)
            return ApplyDiversityFilter(localResults, topK);

        // ========== 2) GLOBAL SEARCH ==========

        var remaining = maxK - localResults.Count;
        if (remaining <= 0)
            return ApplyDiversityFilter(localResults, topK);

        var globalQuery = BaseGlobalQuery();

        var globalResults = await RunQueryAsync(
            globalQuery,
            useLanguageFilter: useLangFilter,
            useRegionFilter:   false,
            minImportance:     globalMinImportance,
            limit:             remaining,
            excludeIds:        localResults.Select(l => l.Id));

        if (globalResults.Count > 0)
            localResults.AddRange(globalResults);

        return ApplyDiversityFilter(localResults, topK);
    }

    IReadOnlyList<Learning> ApplyDiversityFilter(
        IReadOnlyList<Learning> orderedCandidates,
        int topK)
    {
        if (orderedCandidates.Count == 0)
            return orderedCandidates;

        var maxPerUrl        = _options.DiversityMaxPerUrl;
        var maxTextSimilarity = _options.DiversityMaxTextSimilarity;

        var selected  = new List<Learning>(capacity: topK);
        var perUrl    = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var seenTexts = new List<string>();

        foreach (var candidate in orderedCandidates)
        {
            if (selected.Count >= topK)
                break;

            var url = candidate.SourceUrl ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(url))
            {
                if (perUrl.TryGetValue(url, out var count) && count >= maxPerUrl)
                    continue;
            }

            var normText = NormalizeForSimilarity(candidate.Text);
            if (string.IsNullOrWhiteSpace(normText))
                continue;

            var isTooSimilar = seenTexts.Any(t =>
                ComputeJaccardSimilarity(t, normText) >= maxTextSimilarity);

            if (isTooSimilar)
                continue;

            selected.Add(candidate);
            seenTexts.Add(normText);

            if (!string.IsNullOrWhiteSpace(url))
            {
                perUrl[url] = perUrl.TryGetValue(url, out var count)
                    ? count + 1
                    : 1;
            }
        }

        logger.LogDebug("[GetSimilarLearningsAsync] {number} Learnings retrieved: {learnings}", selected.Count, string.Join(",", selected.Select(g => g.ToString())));

        return selected;
    }

    static string NormalizeForSimilarity(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
            else if (ch is '-' or '_' or '%')
            {
                sb.Append(ch);
            }
        }

        var normalized = sb.ToString();
        return string.Join(
            ' ',
            normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    static double ComputeJaccardSimilarity(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return 0.0;

        var setA = a.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);
        var setB = b.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);

        if (setA.Count == 0 || setB.Count == 0)
            return 0.0;

        var intersection = setA.Intersect(setB).Count();
        var union        = setA.Union(setB).Count();

        return union == 0 ? 0.0 : (double)intersection / union;
    }
}