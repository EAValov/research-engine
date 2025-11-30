using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ResearchApi.Infrastructure;
using ResearchApi.Prompts;

namespace ResearchApi.Domain;

public record SearchResult(string Url, string Title, string Description);

public interface ISearchClient
{
      Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int limit,
        string? location = null,
        CancellationToken ct = default);
}

public interface ICrawlClient
{
    Task<string> FetchContentAsync(string url, CancellationToken ct = default);
}

public interface IResearchJobStore
{
    Task<int> AppendEventAsync(Guid jobId, ResearchEvent ev, CancellationToken ct = default);
    Task<ResearchJob> CreateJobAsync(string query, IEnumerable<Clarification> clarifications, int breadth, int depth, string language, string? region, CancellationToken ct = default);
    Task<IReadOnlyList<ResearchEvent>> GetEventsAsync(Guid jobId, CancellationToken ct = default);
    Task<ResearchJob?> GetJobAsync(Guid id, CancellationToken ct = default);
    Task<int> UpdateJobAsync(ResearchJob job, CancellationToken ct = default);
}

public interface IResearchOrchestrator
{
    Task<ResearchJob> StartJobAsync(string query, IEnumerable<Clarification> clarifications, int breadth, int depth, string language, string? region, CancellationToken ct = default);
    Task RunJobAsync(Guid jobId, CancellationToken ct);
}

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
