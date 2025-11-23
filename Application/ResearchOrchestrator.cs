using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResearchApi.Configuration;
using ResearchApi.Domain;
using ResearchApi.Prompts;

namespace ResearchApi.Application;

public class ResearchOrchestrator(ILlmClient llmClient, ISearchClient searchClient, ICrawlClient crawlClient, IResearchJobStore jobStore, ILogger<ResearchOrchestrator> logger, IOptions<LlmChunkingOptions> chunkingOptions) 
    : IResearchOrchestrator
{
    private readonly ILlmClient _llmClient = llmClient;
    private readonly ISearchClient _searchClient = searchClient;
    private readonly ICrawlClient _crawlClient = crawlClient;
    private readonly IResearchJobStore _jobStore = jobStore;
    private readonly ILogger<ResearchOrchestrator> _logger = logger;
    private readonly LlmChunkingOptions _chunkingOptions = chunkingOptions.Value;

    private const int MaxConcurrentRequests = 1;
    private const int LimitSearches = 5;
    private const int MaxLearningsPerSearchResult = 3;

    public ResearchJob StartJob(string query, IEnumerable<Clarification> clarifications, int breadth, int depth, string language, string? region)
    {
        var job = _jobStore.CreateJob(query, clarifications, breadth, depth, language, region);
        return job;
    }

    public async Task RunJobAsync(Guid jobId, CancellationToken ct)
    {
        var job = _jobStore.GetJob(jobId);
        if (job == null)
        {
            throw new ArgumentException("Job not found", nameof(jobId));
        }

        var runningJob = job with { Status = ResearchJobStatus.Running };
        _jobStore.UpdateJob(runningJob);

        try
        {
            _jobStore.AppendEvent(jobId, new ResearchEvent(DateTimeOffset.UtcNow, "planning", "Starting research planning"));

            var clarifications = job.Clarifications ?? new List<Clarification>();

            // Step 1: Generate SERP queries (now uses clarifications + depth)
            var serpQueries = await GenerateSerpQueries(job.Query, clarifications, job.Depth, job.Breadth, ct, job.TargetLanguage);

            _jobStore.AppendEvent(jobId, new ResearchEvent(DateTimeOffset.UtcNow, "planning",
                $"Generated {serpQueries.Count} queries based on clarifications and depth={job.Depth}"));

            // Step 2: Process each SERP query
            var visitedUrls = new HashSet<string>();
            var allLearnings = new List<Learning>();
            var semaphore = new SemaphoreSlim(MaxConcurrentRequests);

            var tasks = serpQueries.Select(async (query, index) =>
            {
                _logger.LogInformation("Processing SERP query {Index}: {Query}", index, query);
                await semaphore.WaitAsync(ct);
                try
                {
                    var searchResults = await _searchClient.SearchAsync(
                        query,
                        LimitSearches,
                        location: job.Region,
                        ct: ct);

                    _logger.LogInformation(
                        "Search results for query '{Query}': {Count} URLs found",
                        query,
                        searchResults.Count);

                    var newUrls = searchResults
                        .Select(r => r.Url)
                        .Where(u => !string.IsNullOrWhiteSpace(u))
                        .Distinct()
                        .ToList();

                    visitedUrls.UnionWith(newUrls);

                    foreach (var url in newUrls)
                    {
                        var content = await _crawlClient.FetchContentAsync(url, ct);

                        if (string.IsNullOrWhiteSpace(content))
                        {
                            _logger.LogWarning("Empty content for URL {Url}, skipping learnings extraction.", url);
                            continue;
                        }

                        try
                        {
                            var learnings = await ExtractLearnings(
                                job.Query,
                                clarifications,
                                content,
                                url,   
                                ct,
                                job.TargetLanguage);

                            lock (allLearnings)
                            {
                                allLearnings.AddRange(learnings);
                            }
                        }
                        catch (Exception ex)
                        {
                             _logger.LogError(ex, "Error during learnings extraction. SKIPPING");
                        }
                    }

                    _jobStore.AppendEvent(
                        jobId,
                        new ResearchEvent(
                            DateTimeOffset.UtcNow,
                            "search-progress",
                            $"Processed SERP query '{query}' with {newUrls.Count} URLs"));
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            // Step 3: Synthesize final report, with clarifications
            _jobStore.AppendEvent(jobId, new ResearchEvent(DateTimeOffset.UtcNow, "summarizing", "Generating final report"));

            var reportMarkdown = await WriteFinalReport(job.Query, clarifications, allLearnings, visitedUrls.ToList(), ct, job.TargetLanguage);

            var completedJob = runningJob with
            {
                Status = ResearchJobStatus.Completed,
                ReportMarkdown = reportMarkdown,
                VisitedUrls = visitedUrls.ToList()
            };

            _jobStore.UpdateJob(completedJob);
            _jobStore.AppendEvent(jobId, new ResearchEvent(DateTimeOffset.UtcNow, "completed", "Research completed successfully"));
        }
        catch (Exception ex)
        {
            var failedJob = runningJob with { Status = ResearchJobStatus.Failed };
            _jobStore.UpdateJob(failedJob);
            _jobStore.AppendEvent(jobId, new ResearchEvent(DateTimeOffset.UtcNow, "failed",
                $"Research failed: {ex.Message}"));
            throw;
        }
    }

    public async Task<IReadOnlyList<string>> GenerateFeedbackQueries(string query, int max, bool includeBreadthDepthQuestions, CancellationToken ct)
    {
        var prompt = FeedbackPromptFactory.Build(query, max, includeBreadthDepthQuestions);

        var rawResponse = await _llmClient.CompleteAsync(prompt, ct);

        var withoutThink = _llmClient.StripThinkBlock(rawResponse);

        var jsonStart = withoutThink.IndexOf('{');
        if (jsonStart > 0)
        {
            withoutThink = withoutThink[jsonStart..];
        }

        var plan = JsonSerializer.Deserialize<SerpQueryPlan>(withoutThink, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        var queries = plan?.Queries?
            .Where(q => !string.IsNullOrWhiteSpace(q))
            .Select(q => q.Trim())
            .Take(max)
            .ToList() ?? new List<string>();

        return queries;
    }

    private sealed class SerpQueryPlan
    {
        public List<string>? Queries { get; set; }
    }

    private async Task<IReadOnlyList<string>> GenerateSerpQueries(
        string query,
        IEnumerable<Clarification> clarifications,
        int depth,
        int breadth,
        CancellationToken ct,
        string targetLanguage)
    {
        var clarificationsText = FormatClarifications(clarifications);

        var prompt = PlanningPromptFactory.Build(
            query,
            clarificationsText: clarificationsText,
            breadth: breadth,
            depth: depth,
            targetLanguage: targetLanguage);

        _logger.LogDebug("LLM Prompt:\n{Prompt}", prompt.userPrompt);

        var rawResponse = await _llmClient.CompleteAsync(prompt, ct);

        _logger.LogDebug("LLM Raw Output:\n{Output}", rawResponse);

        var withoutThink = _llmClient.StripThinkBlock(rawResponse);
        
        var jsonStart = withoutThink.IndexOf('{');
        if (jsonStart > 0)
        {
            withoutThink = withoutThink[jsonStart..];
        }

        var plan = JsonSerializer.Deserialize<SerpQueryPlan>(withoutThink, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        var queries = plan?.Queries?
            .Where(q => !string.IsNullOrWhiteSpace(q))
            .Select(q => q.Trim())
            .Take(breadth)
            .ToList() ?? new List<string>();

        _logger.LogInformation("Generated {Count} SERP queries for query '{Query}' with depth={Depth}, breadth={Breadth}", 
            queries.Count, query, depth, breadth);

        return queries;
    }

    private static string FormatClarifications(IEnumerable<Clarification> clarifications)
    {
        var list = clarifications.ToList();
        if (list.Count == 0)
            return "No additional clarifications were provided.";

        var sb = new StringBuilder();
        sb.AppendLine("Additional context from user clarifications:");
        foreach (var c in list)
        {
            sb.AppendLine($"Q: {c.Question}");
            sb.AppendLine($"A: {c.Answer}");
            sb.AppendLine();
        }
        return sb.ToString().Trim();
    }


    private async Task<string> WriteFinalReport(
        string query,
        IEnumerable<Clarification> clarifications,
        List<Learning> learnings,
        List<string> visitedUrls,
        CancellationToken ct,
        string? targetLanguage = "en")
    {
        var clarificationsText = FormatClarifications(clarifications);
        
        // Create a mapping from URL to citation index
        var sourceIndexMap = new Dictionary<string, int>();
        var currentIndex = 1;
        foreach (var url in visitedUrls)
        {
            if (!sourceIndexMap.ContainsKey(url))
            {
                sourceIndexMap[url] = currentIndex++;
            }
        }

        // Format learnings with citation tags
        var formattedLearnings = learnings.Select(learning => {
            var citationIndex = sourceIndexMap[learning.SourceUrl];
            return $"<learning source=\"{learning.SourceUrl}\" citation=\"[{citationIndex}]\">{learning.Text}</learning>";
        });
        var learningsString = string.Join("\n", formattedLearnings);

        var prompt = SynthesisPromptFactory.Build(
            query,
            learningsString,
            clarificationsText: clarificationsText,
            targetLanguage: targetLanguage);

        _logger.LogDebug("LLM Prompt:\n{Prompt}", prompt.userPrompt);

        var response = await _llmClient.CompleteAsync(prompt, ct);

        _logger.LogDebug("LLM Raw Output:\n{Output}", response);

        // Format sources section with citation indices
        var urlsSection = "\n\n## Sources\n\n" + string.Join("\n", visitedUrls.Select(url => $"[{sourceIndexMap[url]}] {url}"));
        var finalResponse = response + urlsSection;

        _logger.LogInformation("Final report generated with {LearningsCount} learnings from {UrlCount} URLs", 
            learnings.Count, visitedUrls.Count);

        return finalResponse;
    }

    private async Task<IReadOnlyList<Learning>> ExtractLearnings(
        string query,
        IEnumerable<Clarification> clarifications,
        string content,
        string sourceUrl,
        CancellationToken ct,
        string targetLanguage)
    {
        var maxPromptTokens = _chunkingOptions.MaxPromptTokens;

        var pending = new Queue<string>();
        pending.Enqueue(content ?? string.Empty);

        var allLearnings = new List<Learning>();
        var clarificationsText = FormatClarifications(clarifications);

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var segment = pending.Dequeue();
            if (string.IsNullOrWhiteSpace(segment))
                continue;

            // Build prompt for this segment
            var prompt = LearningExtractionPromptFactory.Build(
                query,
                segment,
                clarificationsText: clarificationsText,
                maxLearnings: MaxLearningsPerSearchResult,
                targetLanguage: targetLanguage);

            var tokenCount = await _llmClient.CountTokensAsync(prompt, ct);

            _logger.LogDebug(
                "Token count for learning-extraction segment (URL={Url}, length={Length} chars): {Tokens}",
                sourceUrl, segment.Length, tokenCount);

            if (tokenCount <= maxPromptTokens)
            {
                // Safe to call LLM
                var rawResponse = await _llmClient.CompleteAsync(prompt, ct);

                _logger.LogDebug("LLM Raw Output for chunk:\n{Output}", rawResponse);

                var withoutThink = _llmClient.StripThinkBlock(rawResponse);

                _logger.LogDebug("LLM output without <think> block for chunk:\n{Output}", withoutThink);

                var learnings = withoutThink
                    .Split('\n')
                    .Select(line => line.Trim())
                    .Where(line => !string.IsNullOrEmpty(line))
                    .Select(text => new Learning(text, sourceUrl)) 
                    .ToList();

                _logger.LogInformation("Extracted {Count} learnings from chunk of length {Length} for URL {Url}", 
                    learnings.Count, content.Length, sourceUrl);

                if (learnings.Count == 0)
                {
                    _logger.LogWarning("No learnings extracted for chunk of URL {Url}; content length={Length}, query={Query}", 
                        sourceUrl, content.Length, query);
                }                   

                allLearnings.AddRange(learnings);
            }
            else
            {
                // Segment too big -> split it further
                if (segment.Length < 2000) // safety threshold to avoid endless splitting
                {
                    _logger.LogWarning(
                        "Segment for URL {Url} still over token limit ({Tokens}) " +
                        "even though length={Length}; truncating.",
                        sourceUrl, tokenCount, segment.Length);

                    var truncated = segment[..(segment.Length / 2)];
                    pending.Enqueue(truncated);
                    continue;
                }

                var (left, right) = SplitSegmentOnBoundary(segment);

                _logger.LogInformation(
                    "Split segment for URL {Url} into two parts: leftLength={Left}, rightLength={Right}",
                    sourceUrl, left.Length, right.Length);

                if (!string.IsNullOrWhiteSpace(left))
                    pending.Enqueue(left);
                if (!string.IsNullOrWhiteSpace(right))
                    pending.Enqueue(right);
            }
        }

        return allLearnings;
    }

    /// <summary>
    /// Splits a segment into two parts on a "nice" boundary (paragraph / sentence),
    /// roughly in the middle. If no good boundary is found, falls back to a hard split.
    /// </summary>
    private (string left, string right) SplitSegmentOnBoundary(string segment)
    {
        if (string.IsNullOrEmpty(segment))
            return (string.Empty, string.Empty);

        var length = segment.Length;

        // Short segments – just hard split
        if (length <= 2000)
        {
            var mid = length / 2;
            return (segment[..mid], segment[mid..]);
        }

        var target = length / 2;

        // 1) Try paragraph boundary near the middle: "\n\n"
        var leftSearchLimit  = Math.Max(0, target - 2000);
        var rightSearchLimit = Math.Min(length, target + 2000);

        var window = segment[leftSearchLimit..rightSearchLimit];
        var relParagraphIdx = window.LastIndexOf("\n\n", StringComparison.Ordinal);

        if (relParagraphIdx >= 0)
        {
            var splitIndex = leftSearchLimit + relParagraphIdx + 2; // include the line break
            var left  = segment[..splitIndex];
            var right = segment[splitIndex..];
            return (left, right);
        }

        // 2) Try sentence boundary (period + space) near middle
        var relSentenceIdx = window.LastIndexOf(". ", StringComparison.Ordinal);
        if (relSentenceIdx >= 0)
        {
            var splitIndex = leftSearchLimit + relSentenceIdx + 2; // after ". "
            var left  = segment[..splitIndex];
            var right = segment[splitIndex..];
            return (left, right);
        }

        // 3) Fallback: hard split in the middle
        var midIndex = length / 2;
        return (segment[..midIndex], segment[midIndex..]);
    }
}
