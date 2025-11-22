using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using ResearchApi.Domain;
using ResearchApi.Prompts;

namespace ResearchApi.Application;

public class ResearchOrchestrator(ILlmClient llmClient, ISearchClient searchClient, ICrawlClient crawlClient, IResearchJobStore jobStore) 
    : IResearchOrchestrator
{
    private readonly ILlmClient _llmClient = llmClient;
    private readonly ISearchClient _searchClient = searchClient;

    private readonly ICrawlClient _crawlClient = crawlClient;

    private readonly IResearchJobStore _jobStore = jobStore;
    private const int MaxConcurrentRequests = 2;

    public ResearchJob StartJob(string query, IEnumerable<Clarification> clarifications, int breadth, int depth)
    {
        var job = _jobStore.CreateJob(query, clarifications, breadth, depth);
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
            var serpQueries = await GenerateSerpQueries(job.Query, clarifications, job.Depth, job.Breadth, ct);

            _jobStore.AppendEvent(jobId, new ResearchEvent(DateTimeOffset.UtcNow, "planning",
                $"Generated {serpQueries.Count} queries based on clarifications and depth={job.Depth}"));

            // Step 2: Process each SERP query
            var visitedUrls = new HashSet<string>();
            var allLearnings = new List<Learning>();
            var semaphore = new SemaphoreSlim(MaxConcurrentRequests);

            var tasks = serpQueries.Select(async query =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    var searchResults = await _searchClient.SearchAsync(query, 5, ct: ct);

                    var newUrls = searchResults.Select(r => r.Url).Where(u => !string.IsNullOrEmpty(u)).ToList();
                    visitedUrls.UnionWith(newUrls);

                    var contentTasks = newUrls.Select(url => _crawlClient.FetchContentAsync(url, ct));
                    var contents = await Task.WhenAll(contentTasks);
                    var contentString = string.Join("\n\n", contents.Where(c => !string.IsNullOrEmpty(c)));

                    // NOW pass clarifications into learning extraction as well
                    var learnings = await ExtractLearnings(job.Query, clarifications, contentString, newUrls, ct);
                    allLearnings.AddRange(learnings);

                    _jobStore.AppendEvent(jobId, new ResearchEvent(DateTimeOffset.UtcNow, "search-progress",
                        $"Processed query '{query}' with {newUrls.Count} URLs"));
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            // Step 3: Synthesize final report, with clarifications
            _jobStore.AppendEvent(jobId, new ResearchEvent(DateTimeOffset.UtcNow, "summarizing", "Generating final report"));

            var reportMarkdown = await WriteFinalReport(job.Query, clarifications, allLearnings, visitedUrls.ToList(), ct);

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
        CancellationToken ct)
    {
        var clarificationsText = FormatClarifications(clarifications);

        var prompt = PlanningPromptFactory.Build(
            query,
            clarificationsText: clarificationsText,
            breadth: breadth,
            depth: depth);

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
            .Take(breadth)
            .ToList() ?? new List<string>();

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

    private async Task<IReadOnlyList<Learning>> ExtractLearnings(
        string query,
        IEnumerable<Clarification> clarifications,
        string content,
        IEnumerable<string> sourceUrls,
        CancellationToken ct)
    {
        var clarificationsText = FormatClarifications(clarifications);

        var prompt = LearningExtractionPromptFactory.Build(
            query,
            content,
            clarificationsText: clarificationsText,
            maxLearnings: 3);

        var response = await _llmClient.CompleteAsync(prompt, ct);

        var learnings = response
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrEmpty(line))
            .Select(text => new Learning(text, sourceUrls.FirstOrDefault() ?? "")) // Assign first URL to all learnings for now
            .ToList();

        return learnings;
    }

    private async Task<string> WriteFinalReport(
        string query,
        IEnumerable<Clarification> clarifications,
        List<Learning> learnings,
        List<string> visitedUrls,
        CancellationToken ct)
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
            clarificationsText: clarificationsText);

        var response = await _llmClient.CompleteAsync(prompt, ct);

        // Format sources section with citation indices
        var urlsSection = "\n\n## Sources\n\n" + string.Join("\n", visitedUrls.Select(url => $"[{sourceIndexMap[url]}] {url}"));
        return response + urlsSection;
    }
}
