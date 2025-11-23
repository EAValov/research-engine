using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ResearchApi.Prompts;

namespace ResearchApi.Domain;

public interface ILlmClient
{
    Task<string> CompleteAsync(Prompt prompt, CancellationToken cancellationToken = default);

    Task<int> CountTokensAsync(Prompt prompt, CancellationToken ct = default);

    string StripThinkBlock(string text);
}

public record SearchResult(string Url, string Title, string Snippet);

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
    ResearchJob CreateJob(string query, IEnumerable<Clarification> clarifications, int breadth, int depth, string language, string? region);
    ResearchJob? GetJob(Guid id);
    void UpdateJob(ResearchJob job);
    IReadOnlyList<ResearchEvent> GetEvents(Guid jobId);
    void AppendEvent(Guid jobId, ResearchEvent ev);
}

public interface IResearchOrchestrator
{
    Task<IReadOnlyList<string>> GenerateFeedbackQueries(string query, int max, bool includeBreadthDepthQuestions, CancellationToken ct);
    ResearchJob StartJob(string query, IEnumerable<Clarification> clarifications, int breadth, int depth, string language, string? region);
    Task RunJobAsync(Guid jobId, CancellationToken ct);
}
