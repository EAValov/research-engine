using System.Text;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using ResearchApi.Domain;

namespace ResearchApi.Infrastructure;

public interface ILearningEmbeddingService
{
    /// <summary>
    /// Ensures that all given learnings have embeddings.
    /// Only computes embeddings for learnings with Embedding == null.
    /// </summary>
    Task<IReadOnlyList<Learning>> PopulateEmbeddingsAsync(
        IEnumerable<Learning> learnings,
        CancellationToken ct = default);

    Task<IReadOnlyList<Learning>> GetSimilarLearningsAsync(
        string queryText,
        Guid? jobId          = null,
        string? queryHash    = null,
        string? language     = null,
        string? region       = null,
        int topK             = 20,
        CancellationToken ct = default);
}

public class LearningEmbeddingService : ILearningEmbeddingService
{
    private readonly ILlmService _llmService;
    private readonly IDbContextFactory<ResearchDbContext> _dbContextFactory;
    private readonly ILogger<LearningEmbeddingService> _logger;

    public LearningEmbeddingService(
        ILlmService llmService,
        IDbContextFactory<ResearchDbContext> dbContextFactory,
        ILogger<LearningEmbeddingService> logger)
    {
        _llmService = llmService;
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Learning>> PopulateEmbeddingsAsync(
        IEnumerable<Learning> learnings,
        CancellationToken ct = default)
    {
        if (learnings == null || learnings.Count() == 0)
            return Array.Empty<Learning>();

        var toEmbed = learnings
            .Where(l => l.Embedding == null)
            .ToList();

        if (toEmbed.Count() == 0)
        {
            _logger.LogDebug("All {Count} learnings already have embeddings.", learnings.Count());
            return toEmbed;
        }

        _logger.LogInformation("Computing embeddings for {Count} learnings.", toEmbed.Count);

        // You can slice here if you want to limit batch size
        var texts = toEmbed.Select(l => l.Text).ToList();
        var vectors = await _llmService.GenerateEmbeddingsAsync(texts, ct);

        var count = Math.Min(toEmbed.Count, vectors.Count);
        for (int i = 0; i < count; i++)
        {
            toEmbed[i].Embedding = new Pgvector.Vector(vectors[i].Vector);
        }

        _logger.LogInformation("Stored embeddings for {Count} learnings.", count);

        return toEmbed;
    }

   public async Task<IReadOnlyList<Learning>> GetSimilarLearningsAsync(
        string queryText,
        Guid? jobId          = null,
        string? queryHash    = null,
        string? language     = null,
        string? region       = null,
        int topK             = 10,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(queryText))
            return Array.Empty<Learning>();

        var embeddingResult = await _llmService.GenerateEmbeddingAsync(queryText, ct);
        if (embeddingResult is null)
            return Array.Empty<Learning>();

        var queryEmbedding = embeddingResult.Vector;
        var maxK = Math.Clamp(topK, 1, 200);
        var queryVector = new Pgvector.Vector(queryEmbedding);

        // Tunable knobs
        const float  LocalMinImportance          = 0.4f; // “good enough” for current job
        const float  GlobalMinImportance         = 0.65f; // only very strong items from other jobs
        const double MinLocalFractionForNoGlobal = 0.75D;   // if we have ≥ 75% of topK locally, we skip global

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        // -------- base queries --------

        IQueryable<Learning> BaseLocalQuery() =>
            db.Learnings
            .Include(l => l.Page)
            .Where(l => l.Embedding != null)
            .Where(l => jobId.HasValue ? l.JobId == jobId.Value : true)
            .Where(l => string.IsNullOrWhiteSpace(queryHash) || l.QueryHash == queryHash);

        IQueryable<Learning> BaseGlobalQuery() =>
            db.Learnings
            .Include(l => l.Page)
            .Where(l => l.Embedding != null);
            // note: no jobId / queryHash filter here – this is cross-job

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

            // filter by importance
            q = q.Where(l => l.ImportanceScore >= minImportance);

            if (excludeIds is not null)
            {
                var excluded = excludeIds.ToList();
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

        // ========== 1) LOCAL SEARCH: current job only ==========

        var localQuery      = BaseLocalQuery();
        var useLangFilter   = !string.IsNullOrWhiteSpace(language);
        var useRegionFilter = !string.IsNullOrWhiteSpace(region);

        // 1a) try with language + region (if provided)
        var localResults = await RunQueryAsync(
            localQuery,
            useLanguageFilter: useLangFilter,
            useRegionFilter:   useRegionFilter,
            minImportance:     LocalMinImportance,
            limit:             maxK);

        // 1b) fallback: drop region, keep language (if region was specified)
        if (localResults.Count < maxK && useRegionFilter)
        {
            var moreLocal = await RunQueryAsync(
                localQuery,
                useLanguageFilter: useLangFilter,
                useRegionFilter:   false,
                minImportance:     LocalMinImportance,
                limit:             maxK - localResults.Count,
                excludeIds:        localResults.Select(l => l.Id));

            if (moreLocal.Count > 0)
                localResults.AddRange(moreLocal);
        }

        // 1c) fallback: drop language+region, keep only job/queryHash
        if (localResults.Count < maxK && useLangFilter)
        {
            var moreLocal = await RunQueryAsync(
                localQuery,
                useLanguageFilter: false,
                useRegionFilter:   false,
                minImportance:     LocalMinImportance,
                limit:             maxK - localResults.Count,
                excludeIds:        localResults.Select(l => l.Id));

            if (moreLocal.Count > 0)
                localResults.AddRange(moreLocal);
        }

        // If we filled most of topK locally, we don't need global
        var minLocalNeeded = (int)Math.Ceiling(maxK * MinLocalFractionForNoGlobal);
        if (localResults.Count >= minLocalNeeded)
            return ApplyDiversityFilter(localResults, topK);

        // ========== 2) GLOBAL SEARCH: other jobs, same language by default ==========

        var remaining = maxK - localResults.Count;
        if (remaining <= 0)
            return ApplyDiversityFilter(localResults, topK);

        var globalQuery = BaseGlobalQuery();

        // For global: keep language filter (so we don't mix languages),
        // but by default do NOT filter by region – the question may be cross-regional.
        var globalResults = await RunQueryAsync(
            globalQuery,
            useLanguageFilter: useLangFilter,
            useRegionFilter:   false,
            minImportance:     GlobalMinImportance,
            limit:             remaining,
            excludeIds:        localResults.Select(l => l.Id));

        if (globalResults.Count > 0)
            localResults.AddRange(globalResults);

        // Finally apply diversity post-filter
        return ApplyDiversityFilter(localResults, topK);
    }
    
    private static IReadOnlyList<Learning> ApplyDiversityFilter(
        IReadOnlyList<Learning> orderedCandidates,
        int topK,
        int maxPerUrl = 3,
        double maxTextSimilarity = 0.85)
    {
        if (orderedCandidates.Count == 0)
            return orderedCandidates;

        var selected   = new List<Learning>(capacity: topK);
        var perUrl     = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var seenTexts  = new List<string>(); // normalized texts for similarity

        foreach (var candidate in orderedCandidates)
        {
            if (selected.Count >= topK)
                break;

            var url = candidate.SourceUrl ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(url))
            {
                if (!perUrl.TryGetValue(url, out var count))
                    count = 0;

                if (count >= maxPerUrl)
                {
                    // too many learnings from same URL
                    continue;
                }
            }

            var normText = NormalizeForSimilarity(candidate.Text);
            if (string.IsNullOrWhiteSpace(normText))
                continue;

            var isTooSimilar = seenTexts.Any(t =>
                ComputeJaccardSimilarity(t, normText) >= maxTextSimilarity);

            if (isTooSimilar)
                continue;

            // accept
            selected.Add(candidate);
            seenTexts.Add(normText);

            if (!string.IsNullOrWhiteSpace(url))
            {
                perUrl[url] = perUrl.TryGetValue(url, out var count)
                    ? count + 1
                    : 1;
            }
        }

        return selected;
    }

    private static string NormalizeForSimilarity(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Lowercase, remove most punctuation, collapse whitespace.
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
            // ignore other punctuation
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

        var setA = a.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var setB = b.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);

        if (setA.Count == 0 || setB.Count == 0)
            return 0.0;

        var intersection = setA.Intersect(setB).Count();
        var union        = setA.Union(setB).Count();

        return union == 0 ? 0.0 : (double)intersection / union;
    }
}
