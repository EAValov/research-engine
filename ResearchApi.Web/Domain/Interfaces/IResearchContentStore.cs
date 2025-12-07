namespace ResearchApi.Domain;

public interface IResearchContentStore
{
    /// <summary>
    /// Upserts a scraped page by URL + content hash.
    /// If the same URL and hash already exist, returns the existing page.
    /// If the URL exists but content changed, updates the content and hash.
    /// Otherwise, inserts a new row.
    /// </summary>
    Task<ScrapedPage> UpsertScrapedPageAsync(
        string url,
        string content,
        string? language,
        string? region,
        CancellationToken ct = default);

    /// <summary>
    /// Creates Learning rows with precomputed embeddings.
    /// </summary>
    Task AddLearningsAsync (
        Guid jobId,
        Guid pageId,
        IEnumerable<Learning> learningsWithEmbeddings,
        CancellationToken ct = default);

    /// <summary>
    /// Fetch existing learnings for a page.
    /// </summary>
    Task<IReadOnlyList<Learning>> GetLearningsForPageAndQueryAsync(
        Guid pageId,
        string queryHash,
        CancellationToken ct = default);

    string ComputeQueryHash(string query);
}
