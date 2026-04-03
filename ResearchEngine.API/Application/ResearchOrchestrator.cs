using ResearchEngine.Configuration;
using ResearchEngine.Domain;
using ResearchEngine.Infrastructure;
using System.Text;
using Hangfire;
using Hangfire.States;

namespace ResearchEngine.Application;

public sealed class ResearchOrchestrator(
    IRuntimeSettingsAccessor runtimeSettings,
    ISearchClient searchClient,
    ICrawlClient crawlClient,
    ISourceReliabilityEvaluator sourceReliabilityEvaluator,
    IResearchJobRepository jobRepository,
    IResearchEventRepository eventRepository,
    IResearchSourceRepository sourceRepository,
    IResearchLearningRepository learningRepository,
    IResearchProtocolService protocolService,
    IQueryPlanningService queryPlanningService,
    ILearningIntelService learningIntelService,
    IReportSynthesisService reportSynthesisService,
    IBackgroundJobClient backgroundJobs,
    ILogger<ResearchOrchestrator> logger)
    : IResearchOrchestrator
{
    /// <summary>
    /// Creates a job row and immediately starts running it in the background.
    /// Returns the job id.
    /// </summary>
    public async Task<Guid> StartJobAsync(
        string query,
        IEnumerable<Clarification> clarifications,
        int breadth,
        int depth,
        SourceDiscoveryMode? discoveryMode,
        string language,
        string? region,
        CancellationToken ct = default)
    {
        var settings = await runtimeSettings.GetCurrentAsync(ct);
        var clarificationList = clarifications.ToList();
        var effectiveDiscoveryMode = discoveryMode
            ?? SourceDiscoveryModeExtensions.ParseOrDefault(
                settings.ResearchOrchestratorConfig.DefaultDiscoveryMode,
                SourceDiscoveryMode.Balanced);

        if (effectiveDiscoveryMode == SourceDiscoveryMode.Auto)
            effectiveDiscoveryMode = await protocolService.AutoSelectDiscoveryModeAsync(query, clarificationList, ct);

        var job = await jobRepository.CreateJobAsync(
            query: query,
            clarifications: clarificationList,
            breadth: breadth,
            depth: depth,
            discoveryMode: effectiveDiscoveryMode,
            language: language,
            region: region,
            ct: ct);

        var hfId = backgroundJobs.Create(
            Hangfire.Common.Job.FromExpression<IResearchOrchestrator>(o =>
                o.RunJobBackgroundAsync(job.Id)),
            new EnqueuedState("jobs"));

        await jobRepository.SetJobHangfireIdAsync(job.Id, hfId, ct);

        return job.Id;
    }

    // Wrapper for Hangfire invocation
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 30, 300, 1800 })]
    public Task RunJobBackgroundAsync(Guid jobId)
        => RunJobAsync(jobId, CancellationToken.None);

    /// <summary>
    /// Worker entrypoint (used by background execution / tests).
    /// </summary>
    private async Task RunJobAsync(Guid jobId, CancellationToken ct = default)
    {
        if (await jobRepository.IsJobDeletedAsync(jobId, CancellationToken.None))
            return;

        var job = await jobRepository.GetJobAsync(jobId, ct)
            ?? throw new ArgumentException("Job not found", nameof(jobId));

        job.Status = ResearchJobStatus.Running;
        await jobRepository.UpdateJobAsync(job, ct);

        var progress = new ResearchProgressTracker(jobId, eventRepository, minEmitIntervalMs: 250);

        try
        {
            await progress.InfoAsync(ResearchEventStage.Planning, "Starting research planning", ct);
            var trustPolicy = SourceTrustRuleCatalog.BuildPolicy(job.Region, job.TargetLanguage);
            logger.LogInformation(
                "Using source trust packs {ActivePacks} for job {JobId} (language={Language}, region={Region}).",
                string.Join(", ", trustPolicy.ActivePackNames),
                job.Id,
                job.TargetLanguage,
                job.Region ?? "<none>");

            var clarifications = job.Clarifications ?? new List<Clarification>();
            var clarificationsText = FormatClarifications(clarifications);

            await ThrowIfCanceledAsync(jobId, ct);

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
                await ThrowIfCanceledAsync(jobId, ct);
                ct.ThrowIfCancellationRequested();

                await ProcessSerpQueryAsync(
                    job,
                    serpQuery,
                    clarificationsText,
                    progress,
                    trustPolicy,
                    ct);
            }

            await progress.InfoAsync(
                ResearchEventStage.Summarizing,
                "Generating final report",
                ct);

            var synthesisId = await reportSynthesisService.CreateSynthesisAsync(
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
            
            await ThrowIfCanceledAsync(jobId, ct);

            await reportSynthesisService.RunSynthesisAsync(synthesisId, progress, ct);

            // Job becomes completed when synthesis completes
            job.Status = ResearchJobStatus.Completed;
            await jobRepository.UpdateJobAsync(job, ct);

            // metrics + done
            progress.MarkAllCompleted();
            await progress.EmitMetricsSummaryAsync(ct);
        }
        catch (OperationCanceledException oce)
        {
            job.Status = ResearchJobStatus.Canceled;
            await jobRepository.UpdateJobAsync(job, CancellationToken.None);

            await eventRepository.AppendEventAsync(
                jobId,
                new ResearchEvent(DateTimeOffset.UtcNow, ResearchEventStage.Canceled,
                    $"Research canceled: {oce.Message}"),
                CancellationToken.None);

            return; // do not throw -> no Hangfire retry
        }
        catch (Exception ex)
        {
            job.Status = ResearchJobStatus.Failed;
            await jobRepository.UpdateJobAsync(job, ct);

            await eventRepository.AppendEventAsync(
                jobId,
                new ResearchEvent(DateTimeOffset.UtcNow, ResearchEventStage.Failed, $"Research failed: {ex.Message}"),
                ct);

            throw; // let Hangfire retry
        }
    }

    // ---------------- SERP + URL processing ----------------

    private async Task ProcessSerpQueryAsync(
        ResearchJob job,
        string serpQuery,
        string clarificationsText,
        ResearchProgressTracker progress,
        AppliedSourceTrustPolicy trustPolicy,
        CancellationToken ct)
    {
        var settings = await runtimeSettings.GetCurrentAsync(ct);
        var options = settings.ResearchOrchestratorConfig;
        var searchResults = await searchClient.SearchAsync(
            new SearchRequest(
                Query: serpQuery,
                Limit: options.LimitSearches,
                Location: job.Region,
                DiscoveryMode: job.DiscoveryMode),
            ct);

        progress.SerpSearchCompleted(searchResults.Count);

        var candidates = searchResults
            .Select(r => new SearchCandidate(r, sourceReliabilityEvaluator.Evaluate(r, trustPolicy)))
            .Where(c => sourceReliabilityEvaluator.ShouldInclude(c.Assessment, job.DiscoveryMode, SourceSelectionStage.Candidate))
            .OrderByDescending(c => c.Assessment.Score)
            .ThenBy(c => c.Result.Position ?? int.MaxValue)
            .ThenBy(c => c.Result.Url, StringComparer.OrdinalIgnoreCase)
            .GroupBy(c => c.Result.Url, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        if (candidates.Count == 0)
        {
            await progress.InfoAsync(
                ResearchEventStage.Searching,
                $"Processed SERP query '{serpQuery}' with 0 URLs after applying discovery mode {job.DiscoveryMode.ToApiValue()}.",
                ct);

            return;
        }

        if (candidates.Count > options.MaxUrlsPerSerpQuery)
            candidates = candidates.Take(options.MaxUrlsPerSerpQuery).ToList();

        progress.UrlsQueued(candidates.Count);

        await progress.InfoAsync(
            ResearchEventStage.LearningExtraction,
            $"Starting learning extraction for SERP query '{serpQuery}' ({candidates.Count} URLs queued, discovery mode {job.DiscoveryMode.ToApiValue()}).",
            ct);

        var semaphore = new SemaphoreSlim(options.MaxUrlParallelism);

        var tasks = candidates.Select(async candidate =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var summary = await ProcessUrlAsync(
                    job,
                    serpQuery,
                    candidate,
                    clarificationsText,
                    trustPolicy,
                    ct);

                progress.UrlProcessed(summary.UsedCache, summary.HadError, summary.LearningCount);

                await progress.ReportAsync(
                    ResearchEventStage.LearningExtraction,
                    $"Processed URL {candidate.Result.Url}",
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
            $"Processed SERP query '{serpQuery}' with {candidates.Count} URLs.",
            ct);
    }

    private async Task ThrowIfCanceledAsync(Guid jobId, CancellationToken ct)
    {
        if (await jobRepository.IsJobCancelRequestedAsync(jobId, ct))
            throw new OperationCanceledException($"Job {jobId} canceled.");
    }

    private sealed record UrlProcessingSummary(bool UsedCache, bool HadError, int LearningCount);
    private sealed record SearchCandidate(SearchResult Result, SourceReliabilityAssessment Assessment);

    private async Task<UrlProcessingSummary> ProcessUrlAsync(
        ResearchJob job,
        string serpQuery,
        SearchCandidate candidate,
        string clarificationsText,
        AppliedSourceTrustPolicy trustPolicy,
        CancellationToken ct)
    {
        var url = candidate.Result.Url;

        // 1) Fetch content
        var content = await crawlClient.FetchContentAsync(url, ct);

        if (string.IsNullOrWhiteSpace(content))
        {
            logger.LogWarning("Empty content for URL {Url}, skipping.", url);
            return new UrlProcessingSummary(UsedCache: false, HadError: true, LearningCount: 0);
        }

        // 2) Upsert Source
        var evaluated = sourceReliabilityEvaluator.Evaluate(candidate.Result, trustPolicy, content, SourceKind.Web);
        if (!sourceReliabilityEvaluator.ShouldInclude(evaluated, job.DiscoveryMode, SourceSelectionStage.Final))
        {
            logger.LogInformation(
                "Skipping URL {Url} after final trust evaluation for discovery mode {DiscoveryMode}. Tier={Tier}, Classification={Classification}.",
                url,
                job.DiscoveryMode,
                evaluated.Tier,
                evaluated.Classification);

            return new UrlProcessingSummary(UsedCache: false, HadError: false, LearningCount: 0);
        }

        var source = await sourceRepository.UpsertSourceAsync(
            jobId: job.Id,
            reference: url,
            kind: SourceKind.Web,
            content: content,
            title: string.IsNullOrWhiteSpace(candidate.Result.Title) ? null : candidate.Result.Title,
            language: job.TargetLanguage,
            region: job.Region,
            metadata: new SourceMetadata(
                Domain: evaluated.Domain,
                SearchCategory: evaluated.SearchCategory,
                Classification: evaluated.Classification,
                ReliabilityTier: evaluated.Tier,
                ReliabilityScore: evaluated.Score,
                IsPrimarySource: evaluated.IsPrimarySource,
                ReliabilityRationale: evaluated.Rationale),
            ct: ct);

        // 3) Reuse cached learnings for (source, queryHash)
        var cached = await learningRepository.GetLearningsForSourceAndQueryAsync(source.Id, serpQuery, ct);
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
