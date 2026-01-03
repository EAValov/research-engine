using ResearchEngine.Configuration;
using ResearchEngine.Domain;
using System.Text;
using Microsoft.Extensions.Options;

namespace ResearchEngine.Application;

public sealed class ResearchOrchestrator(
    IOptions<ResearchOrchestratorConfig> options,
    ISearchClient searchClient,
    ICrawlClient crawlClient,
    IResearchJobStore jobStore,
    IQueryPlanningService queryPlanningService,
    ILearningIntelService learningIntelService,
    IReportSynthesisService reportSynthesisService,
    ILogger<ResearchOrchestrator> logger)
    : IResearchOrchestrator
{
    private readonly int _limitSearches       = options.Value?.LimitSearches ?? 5;
    private readonly int _maxUrlParallelism   = options.Value?.MaxUrlParallelism ?? 1;
    private readonly int _maxUrlsPerSerpQuery = options.Value?.MaxUrlsPerSerpQuery ?? 20;

    /// <summary>
    /// Creates a job row and immediately starts running it in the background.
    /// Returns the job id.
    /// </summary>
    public async Task<Guid> StartJobAsync(
        string query,
        IEnumerable<Clarification> clarifications,
        int breadth,
        int depth,
        string language,
        string? region,
        CancellationToken ct = default)
    {
        // 1) Create job row
        var job = await jobStore.CreateJobAsync(
            query: query,
            clarifications: clarifications,
            breadth: breadth,
            depth: depth,
            language: language,
            region: region,
            ct: ct);

        // 2) Run in background (do NOT tie to request cancellation)
        _ = Task.Run(async () =>
        {
            try
            {
                await RunJobAsync(job.Id, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // RunJobAsync already appends Failed event + sets status,
                // but keep a log just in case.
                logger.LogError(ex, "Background RunJobAsync failed for job {JobId}", job.Id);
            }
        });

        return job.Id;
    }

    /// <summary>
    /// Worker entrypoint (used by background execution / tests).
    /// </summary>
    public async Task RunJobAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await jobStore.GetJobAsync(jobId, ct)
            ?? throw new ArgumentException("Job not found", nameof(jobId));

        job.Status = ResearchJobStatus.Running;
        await jobStore.UpdateJobAsync(job, ct);

        var progress = new ResearchProgressTracker(jobId, jobStore, minEmitIntervalMs: 250);

        try
        {
            await progress.InfoAsync(ResearchEventStage.Planning, "Starting research planning", ct);

            var clarifications = job.Clarifications ?? new List<Clarification>();
            var clarificationsText = FormatClarifications(clarifications);

            // 1) SERP planning
            var serpQueries = await queryPlanningService.GenerateSerpQueriesAsync(
                job.Query,
                clarificationsText,
                job.Depth,
                job.Breadth,
                job.TargetLanguage,
                ct);

            progress.AddPlannedSerpQueries(serpQueries.Count);

            await progress.InfoAsync(
                ResearchEventStage.Planning,
                $"Generated {serpQueries.Count} queries based on clarifications and depth={job.Depth}",
                ct);

            // 2) SERP -> URLs -> Sources -> Learnings
            foreach (var serpQuery in serpQueries)
            {
                ct.ThrowIfCancellationRequested();

                await ProcessSerpQueryAsync(
                    job,
                    serpQuery,
                    clarificationsText,
                    progress,
                    ct);
            }

            await progress.InfoAsync(
                ResearchEventStage.Summarizing,
                "Generating final report",
                ct);

            var synthesisId = await reportSynthesisService.StartSynthesisAsync(
                jobId: job.Id,
                parentSynthesisId: null,
                outline: null,
                instructions: null,
                ct: ct);

            // Emit synthesis id for clients/tools
            await progress.InfoAsync(
                ResearchEventStage.Summarizing,
                $"Synthesis started (synthesisId={synthesisId})",
                ct);

            await reportSynthesisService.RunExistingSynthesisAsync(synthesisId, progress, ct);

            // Job becomes completed when synthesis completes
            job.Status = ResearchJobStatus.Completed;
            await jobStore.UpdateJobAsync(job, ct);

            // metrics + done
            progress.MarkAllCompleted();
            await progress.EmitMetricsSummaryAsync(ct);
        }
        catch (Exception ex)
        {
            job.Status = ResearchJobStatus.Failed;
            await jobStore.UpdateJobAsync(job, ct);

            await jobStore.AppendEventAsync(
                jobId,
                new ResearchEvent(DateTimeOffset.UtcNow, ResearchEventStage.Failed, $"Research failed: {ex.Message}"),
                ct);

            throw;
        }
    }

    // ---------------- SERP + URL processing ----------------

    private async Task ProcessSerpQueryAsync(
        ResearchJob job,
        string serpQuery,
        string clarificationsText,
        ResearchProgressTracker progress,
        CancellationToken ct)
    {
        var searchResults = await searchClient.SearchAsync(
            serpQuery,
            _limitSearches,
            location: job.Region,
            ct: ct);

        progress.SerpSearchCompleted(searchResults.Count);

        var urls = searchResults
            .Select(r => r.Url)
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (urls.Count == 0)
        {
            await progress.InfoAsync(
                ResearchEventStage.Searching,
                $"Processed SERP query '{serpQuery}' with 0 URLs.",
                ct);

            return;
        }

        if (urls.Count > _maxUrlsPerSerpQuery)
            urls = urls.Take(_maxUrlsPerSerpQuery).ToList();

        progress.UrlsQueued(urls.Count);

        await progress.InfoAsync(
            ResearchEventStage.LearningExtraction,
            $"Starting learning extraction for SERP query '{serpQuery}' ({urls.Count} URLs queued).",
            ct);

        var semaphore = new SemaphoreSlim(_maxUrlParallelism);

        var tasks = urls.Select(async url =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var summary = await ProcessUrlAsync(
                    job,
                    serpQuery,
                    url,
                    clarificationsText,
                    ct);

                progress.UrlProcessed(summary.UsedCache, summary.HadError, summary.LearningCount);

                await progress.ReportAsync(
                    ResearchEventStage.LearningExtraction,
                    $"Processed URL {url}",
                    ct);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        await progress.InfoAsync(
            ResearchEventStage.Searching,
            $"Processed SERP query '{serpQuery}' with {urls.Count} URLs.",
            ct);
    }

    private sealed record UrlProcessingSummary(bool UsedCache, bool HadError, int LearningCount);

    private async Task<UrlProcessingSummary> ProcessUrlAsync(
        ResearchJob job,
        string serpQuery,
        string url,
        string clarificationsText,
        CancellationToken ct)
    {
        // 1) Fetch content
        var content = await crawlClient.FetchContentAsync(url, ct);

        if (string.IsNullOrWhiteSpace(content))
        {
            logger.LogWarning("Empty content for URL {Url}, skipping.", url);
            return new UrlProcessingSummary(UsedCache: false, HadError: true, LearningCount: 0);
        }

        // 2) Upsert Source
        var source = await jobStore.UpsertSourceAsync(
            jobId: job.Id,
            reference: url,
            kind: SourceKind.Web,
            content: content,
            title: null,
            language: job.TargetLanguage,
            region: job.Region,
            ct: ct);

        // 3) Reuse cached learnings for (source, queryHash)
        var cached = await jobStore.GetLearningsForSourceAndQueryAsync(source.Id, serpQuery, ct);
        if (cached.Count > 0)
        {
            logger.LogInformation("Reusing {Count} cached learnings for URL {Url} (SourceId={SourceId}).", cached.Count, url, source.Id);
            return new UrlProcessingSummary(UsedCache: true, HadError: false, LearningCount: cached.Count);
        }

        // 4) Extract learnings (+ embeddings) via intel service
        try
        {
            var extracted = await learningIntelService.ExtractAndSaveLearningsAsync(
                jobId: job.Id,
                sourceId: source.Id,
                query: job.Query,
                clarificationsText: clarificationsText,
                sourceUrl: source.Reference,
                sourceContent: source.Content,
                targetLanguage: job.TargetLanguage,
                computeEmbeddings: true,
                ct: ct);

            if (extracted.Count == 0)
                return new UrlProcessingSummary(UsedCache: false, HadError: false, LearningCount: 0);

            return new UrlProcessingSummary(UsedCache: false, HadError: false, LearningCount: extracted.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error during learnings extraction for URL {Url} (SERP query '{Query}'). SKIPPING",
                url,
                serpQuery);

            return new UrlProcessingSummary(UsedCache: false, HadError: true, LearningCount: 0);
        }
    }

    // ---------------- helpers ----------------

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
}