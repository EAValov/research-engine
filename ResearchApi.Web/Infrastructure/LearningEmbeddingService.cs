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
    private readonly LearningSimilarityOptions _options = similarityOptions.Value;

    public async Task<IReadOnlyList<Learning>> PopulateEmbeddingsAsync(
        IEnumerable<Learning> learnings,
        CancellationToken ct = default)
    {
        if (learnings is null)
            return Array.Empty<Learning>();

        var learningList = learnings as IList<Learning> ?? learnings.ToList();
        if (learningList.Count == 0)
            return Array.Empty<Learning>();

        // New schema: embedding lives in LearningEmbedding (one-to-one)
        var toEmbed = learningList
            .Where(l => l.Embedding is null)
            .ToList();

        if (toEmbed.Count == 0)
        {
            logger.LogDebug("All {Count} learnings already have embeddings.", learningList.Count);
            return Array.Empty<Learning>();
        }

        logger.LogInformation("Computing embeddings for {Count} learnings.", toEmbed.Count);

        var texts = toEmbed.Select(l => l.Text).ToList();
        var vectors = await embeddingModel.GenerateEmbeddingsAsync(texts, ct);

        var count = Math.Min(toEmbed.Count, vectors.Count);
        if (count == 0)
            return Array.Empty<Learning>();

        var now = DateTimeOffset.UtcNow;

        // Persist embeddings for the learnings that do not have them yet.
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        // Load the same learnings from the DB, including Embedding navigation,
        // to avoid attaching detached entities from the caller.
        var ids = toEmbed.Take(count).Select(l => l.Id).ToList();

        var entities = await db.Learnings
            .Include(l => l.Embedding)
            .Where(l => ids.Contains(l.Id))
            .ToListAsync(ct);

        // Map id -> entity for stable assignment
        var byId = entities.ToDictionary(l => l.Id);

        for (var i = 0; i < count; i++)
        {
            var id = ids[i];
            if (!byId.TryGetValue(id, out var entity))
                continue;

            // If another worker added it meanwhile, skip
            if (entity.Embedding is not null)
                continue;

            entity.Embedding = new LearningEmbedding
            {
                Id = Guid.NewGuid(),
                LearningId = entity.Id,
                Vector = new Vector(vectors[i].Vector),
                CreatedAt = now
            };
        }

        var saved = await db.SaveChangesAsync(ct);
        logger.LogInformation("Stored embeddings for {Count} learnings. DB changes saved: {Saved}.", count, saved);

        // Reflect newly created embedding objects back into the passed-in instances where possible
        foreach (var l in toEmbed)
        {
            if (byId.TryGetValue(l.Id, out var entity))
                l.Embedding = entity.Embedding;
        }

        return toEmbed;
    }

    public async Task<IReadOnlyList<Learning>> GetSimilarLearningsAsync(
        string queryText,
        Guid? jobId = null,
        string? queryHash = null,
        string? language = null,
        string? region = null,
        int topK = 20,
        CancellationToken ct = default)
    {
        logger.LogDebug(
            "[GetSimilarLearningsAsync] queryText={queryText}, jobId={jobId}, language={language}, region={region}, queryHash={queryHash}, topK={topK}",
            queryText, jobId, language, region, queryHash, topK);

        if (string.IsNullOrWhiteSpace(queryText))
            return Array.Empty<Learning>();

        var embeddingResult = await embeddingModel.GenerateEmbeddingAsync(queryText, ct);
        if (embeddingResult is null)
            return Array.Empty<Learning>();

        var queryVector = new Vector(embeddingResult.Vector);
        var maxK = Math.Clamp(topK, 1, 200);

        var localMinImportance = _options.LocalMinImportance;
        var globalMinImportance = _options.GlobalMinImportance;
        var minLocalFractionForNoGlobal = _options.MinLocalFractionForNoGlobal;

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        IQueryable<Learning> BaseLocalQuery()
        {
            // New schema:
            // - Source replaces ScrapedPage and carries Language/Region + Url.
            // - Embedding is in LearningEmbedding (Learning.Embedding)
            var q = db.Learnings
                .Include(l => l.Source)
                .Include(l => l.Embedding)
                .Where(l => l.Embedding != null);

            if (jobId is Guid jid)
                q = q.Where(l => l.JobId == jid);

            if (!string.IsNullOrWhiteSpace(queryHash))
                q = q.Where(l => l.QueryHash == queryHash);

            return q;
        }

        IQueryable<Learning> BaseGlobalQuery() =>
            db.Learnings
              .Include(l => l.Source)
              .Include(l => l.Embedding)
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
                q = q.Where(l => l.Source.Language == language);

            if (useRegionFilter && !string.IsNullOrWhiteSpace(region))
                q = q.Where(l => l.Source.Region == region);

            q = q.Where(l => l.ImportanceScore >= minImportance);

            if (excludeIds is not null)
            {
                var excluded = excludeIds as ICollection<Guid> ?? excludeIds.ToList();
                if (excluded.Count > 0)
                    q = q.Where(l => !excluded.Contains(l.Id));
            }

            // vector similarity first, then importance desc
            q = q
                .OrderBy(l => l.Embedding.Vector.CosineDistance(queryVector))
                .ThenByDescending(l => l.ImportanceScore)
                .Take(Math.Clamp(limit, 1, maxK));

            return await q.AsNoTracking().ToListAsync(ct);
        }

        var useLangFilter = !string.IsNullOrWhiteSpace(language);
        var useRegionFilter = !string.IsNullOrWhiteSpace(region);

        // ========== 1) LOCAL SEARCH ==========

        var localQuery = BaseLocalQuery();
        var localResults = new List<Learning>();

        // 1a) with language + region (if provided)
        localResults.AddRange(await RunQueryAsync(
            localQuery,
            useLanguageFilter: useLangFilter,
            useRegionFilter: useRegionFilter,
            minImportance: localMinImportance,
            limit: maxK));

        // 1b) drop region
        if (localResults.Count < maxK && useRegionFilter)
        {
            localResults.AddRange(await RunQueryAsync(
                localQuery,
                useLanguageFilter: useLangFilter,
                useRegionFilter: false,
                minImportance: localMinImportance,
                limit: maxK - localResults.Count,
                excludeIds: localResults.Select(l => l.Id)));
        }

        // 1c) drop language too
        if (localResults.Count < maxK && useLangFilter)
        {
            localResults.AddRange(await RunQueryAsync(
                localQuery,
                useLanguageFilter: false,
                useRegionFilter: false,
                minImportance: localMinImportance,
                limit: maxK - localResults.Count,
                excludeIds: localResults.Select(l => l.Id)));
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
            useRegionFilter: false,
            minImportance: globalMinImportance,
            limit: remaining,
            excludeIds: localResults.Select(l => l.Id));

        if (globalResults.Count > 0)
            localResults.AddRange(globalResults);

        return ApplyDiversityFilter(localResults, topK);
    }

    private IReadOnlyList<Learning> ApplyDiversityFilter(
        IReadOnlyList<Learning> orderedCandidates,
        int topK)
    {
        if (orderedCandidates.Count == 0)
            return orderedCandidates;

        var maxPerUrl = _options.DiversityMaxPerUrl;
        var maxTextSimilarity = _options.DiversityMaxTextSimilarity;

        var selected = new List<Learning>(capacity: topK);
        var perUrl = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var seenTexts = new List<string>();

        foreach (var candidate in orderedCandidates)
        {
            if (selected.Count >= topK)
                break;

            // New schema: url lives on Source
            var url = candidate.Source?.Url ?? string.Empty;
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

        logger.LogDebug(
            "[GetSimilarLearningsAsync] {Count} learnings selected.",
            selected.Count);

        return selected;
    }

    private static string NormalizeForSimilarity(string text)
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

    private static double ComputeJaccardSimilarity(string a, string b)
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
        var union = setA.Union(setB).Count();

        return union == 0 ? 0.0 : (double)intersection / union;
    }
}