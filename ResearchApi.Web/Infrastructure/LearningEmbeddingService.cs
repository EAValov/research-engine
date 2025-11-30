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

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        // Base query: always apply job / queryHash filters here.
        IQueryable<Learning> BaseQuery() =>
            db.Learnings
            .Include(l => l.Page)
            .Where(l => l.Embedding != null)
            .Where(l => !jobId.HasValue || l.JobId == jobId.Value)
            .Where(l => string.IsNullOrWhiteSpace(queryHash) || l.QueryHash == queryHash);

        async Task<List<Learning>> RunQueryAsync(
            bool useLanguageFilter,
            bool useRegionFilter)
        {
            var q = BaseQuery();

            if (useLanguageFilter && !string.IsNullOrWhiteSpace(language))
            {
                q = q.Where(l => l.Page.Language == language);
            }

            if (useRegionFilter && !string.IsNullOrWhiteSpace(region))
            {
                q = q.Where(l => l.Page.Region == region);
            }

            q = q.OrderBy(l => l.Embedding!.CosineDistance(new Pgvector.Vector(queryEmbedding)))
                .Take(maxK);

            return await q.AsNoTracking().ToListAsync(ct);
        }

        // 1) Try with language + region (if provided)
        var results = await RunQueryAsync(
            useLanguageFilter: !string.IsNullOrWhiteSpace(language),
            useRegionFilter:   !string.IsNullOrWhiteSpace(region));

        if (results.Count > 0)
            return results;

        // 2) Fallback: drop region, keep language
        if (!string.IsNullOrWhiteSpace(region))
        {
            results = await RunQueryAsync(
                useLanguageFilter: !string.IsNullOrWhiteSpace(language),
                useRegionFilter:   false);

            if (results.Count > 0)
                return results;
        }

        // 3) Fallback: drop language + region, keep only job/queryHash
        if (!string.IsNullOrWhiteSpace(language))
        {
            results = await RunQueryAsync(
                useLanguageFilter: false,
                useRegionFilter:   false);

            if (results.Count > 0)
                return results;
        }

        return results; // possibly empty
    }

}
