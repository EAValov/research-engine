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

public sealed class LearningEmbeddingService : ILearningEmbeddingService
{
    private readonly ILlmService _llmService;
    private readonly ResearchDbContext _db;
    private readonly ILogger<LearningEmbeddingService> _logger;

    public LearningEmbeddingService(
        ILlmService llmService,
        ResearchDbContext db,
        ILogger<LearningEmbeddingService> logger)
    {
        _llmService = llmService;
        _db = db;
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
        int topK             = 20,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(queryText))
            return Array.Empty<Learning>();

        var result = await _llmService.GenerateEmbeddingAsync(queryText, ct);
        if (result is null)
            return Array.Empty<Learning>();

        var queryEmbedding = result.Vector;

        var q = _db.Learnings
            .Include(l => l.Page)
            .Where(l => l.Embedding != null);

        if (jobId.HasValue)
            q = q.Where(l => l.JobId == jobId.Value);

        if (!string.IsNullOrWhiteSpace(queryHash))
            q = q.Where(l => l.QueryHash == queryHash);

        if (!string.IsNullOrWhiteSpace(language))
            q = q.Where(l => l.Page.Language == language);

        if (!string.IsNullOrWhiteSpace(region))
            q = q.Where(l => l.Page.Region == region);

        q = q
            .OrderBy(l => l.Embedding!.CosineDistance(queryEmbedding))
            .Take(Math.Clamp(topK, 1, 200));

        return await q.AsNoTracking().ToListAsync(ct);
    }
}
