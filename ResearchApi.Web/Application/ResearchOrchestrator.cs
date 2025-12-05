using System.Text;
using ResearchApi.Domain;
using ResearchApi.Infrastructure;

namespace ResearchApi.Application;

public class ResearchOrchestrator(
    ISearchClient searchClient,
    ICrawlClient crawlClient,
    IResearchJobStore jobStore,
    IQueryPlanningService queryPlanningService,
    ILearningExtractionService learningExtractionService,
    IResearchContentStore researchContentStore,
    IReportSynthesisService reportSynthesisService,
    ILearningEmbeddingService learningEmbeddingService,
    ILogger<ResearchOrchestrator> logger)
    : IResearchOrchestrator
{
    private readonly ISearchClient _searchClient             = searchClient;
    private readonly ICrawlClient _crawlClient               = crawlClient;
    private readonly IResearchJobStore _jobStore             = jobStore;
    private readonly IQueryPlanningService _planning         = queryPlanningService;
    private readonly ILearningExtractionService _learningSvc = learningExtractionService;
    private readonly IResearchContentStore _contentStore     = researchContentStore;
    private readonly IReportSynthesisService _reportSvc      = reportSynthesisService;
    private readonly ILearningEmbeddingService _embeddingSvc = learningEmbeddingService;
    private readonly ILogger<ResearchOrchestrator> _logger   = logger;

    private readonly object _metricsLock = new();

    private const int LimitSearches       = 5;
    private const int MaxUrlParallelism   = 1;   // tune based on your hardware
    private const int MaxUrlsPerSerpQuery = 20;  // optional safety cap

    // ---------------- PUBLIC API (unchanged) ----------------

    public Task<ResearchJob> StartJobAsync(
        string query,
        IEnumerable<Clarification> clarifications,
        int breadth,
        int depth,
        string language,
        string? region,
        CancellationToken ct = default)
        => _jobStore.CreateJobAsync(query, clarifications, breadth, depth, language, region, ct);

    public async Task RunJobAsync(Guid jobId, CancellationToken ct)
    {
        var job = await _jobStore.GetJobAsync(jobId, ct)
                ?? throw new ArgumentException("Job not found", nameof(jobId));

        job.Status = ResearchJobStatus.Running;
        await _jobStore.UpdateJobAsync(job, ct);

        var metrics = new ResearchMetrics();

        try
        {
            await ReportProgressAsync(
                jobId,
                metrics,
                "planning",
                "Starting research planning",
                ct);

            var clarifications     = job.Clarifications ?? new List<Clarification>();
            var clarificationsText = FormatClarifications(clarifications);
            var queryHash          = _contentStore.ComputeQueryHash(job.Query);

            // 1) SERP planning
            var serpQueries = await _planning.GenerateSerpQueriesAsync(
                job.Query,
                clarificationsText,
                job.Depth,
                job.Breadth,
                job.TargetLanguage,
                ct);

            metrics.SerpQueries   = serpQueries.Count;
            metrics.TotalWorkUnits += serpQueries.Count; // each SERP search is one unit

            await ReportProgressAsync(
                jobId,
                metrics,
                "planning",
                $"Generated {serpQueries.Count} queries based on clarifications and depth={job.Depth}",
                ct);

            // 2) Collect learnings with progress-aware metrics
            var (visitedUrls, allLearnings) =
                await CollectLearningsAsync(job, serpQueries, clarificationsText, queryHash, metrics, ct);
            
            // 3) Final synthesis counts as one more unit
            metrics.TotalWorkUnits += 1;

            await ReportProgressAsync(
                jobId,
                metrics,
                "summarizing",
                "Generating final report",
                ct);
            
            job.VisitedUrls    = visitedUrls.Select(u => new VisitedUrl { Url = u }).ToList();

            var reportMarkdown = await _reportSvc
                .WriteFinalReportAsync(job, clarificationsText, allLearnings, ct);

            metrics.CompletedWorkUnits += 1; // synthesis done

            job.Status         = ResearchJobStatus.Completed;
            job.ReportMarkdown = reportMarkdown;


            await _jobStore.UpdateJobAsync(job, ct);

            // final 100% + metrics summary
            metrics.CompletedWorkUnits = metrics.TotalWorkUnits;

            await ReportProgressAsync(
                jobId,
                metrics,
                "metrics",
                metrics.ToString(),
                ct);

            await ReportProgressAsync(
                jobId,
                metrics,
                "completed",
                "Research completed successfully",
                ct);
        }
        catch (Exception ex)
        {
            job.Status = ResearchJobStatus.Failed;
            await _jobStore.UpdateJobAsync(job, ct);

            await _jobStore.AppendEventAsync(
                jobId,
                new ResearchEvent(DateTimeOffset.UtcNow, "failed", $"Research failed: {ex.Message}"),
                ct);

            throw;
        }
    }

    // ---------------- PHASE 2: collect learnings (optimized) ----------------

   private async Task<(HashSet<string> visitedUrls, List<Learning> allLearnings)> CollectLearningsAsync(
        ResearchJob job,
        IReadOnlyList<string> serpQueries,
        string clarificationsText,
        string queryHash,
        ResearchMetrics metrics,
        CancellationToken ct)
    {
        var visitedUrls   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allLearnings  = new List<Learning>();

        foreach (var serpQuery in serpQueries)
        {
            ct.ThrowIfCancellationRequested();

            var newLearningsForQuery = await ProcessSerpQueryAsync(
                job,
                serpQuery,
                clarificationsText,
                queryHash,
                visitedUrls,
                processedUrls,
                metrics,
                ct);

            allLearnings.AddRange(newLearningsForQuery);

            metrics.UniqueUrlsDiscovered = visitedUrls.Count;
            metrics.UniqueUrlsProcessed  = processedUrls.Count;
        }

        return (visitedUrls, allLearnings);
    }

    private async Task<IReadOnlyList<Learning>> ProcessSerpQueryAsync(
        ResearchJob job,
        string serpQuery,
        string clarificationsText,
        string queryHash,
        HashSet<string> visitedUrls,
        HashSet<string> processedUrls,
        ResearchMetrics metrics,
        CancellationToken ct)
    {
        _logger.LogInformation("Processing SERP query: {Query}, [{Region}]", serpQuery, job.Region);

        var searchResults = await _searchClient.SearchAsync(
            serpQuery,
            LimitSearches,
            location: job.Region,
            ct: ct);

        metrics.SearchResultsTotal += searchResults.Count;

        var newUrls = searchResults
            .Select(r => r.Url)
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        lock (visitedUrls)
        {
            foreach (var u in newUrls)
                visitedUrls.Add(u);
        }

        var urlsToProcess = new List<string>();
        lock (processedUrls)
        {
            foreach (var url in newUrls)
            {
                if (!processedUrls.Contains(url))
                {
                    processedUrls.Add(url);
                    urlsToProcess.Add(url);
                }
            }
        }

        // Search itself is one unit – mark as done now
        metrics.CompletedWorkUnits += 1;

        if (urlsToProcess.Count == 0)
        {
            await ReportProgressAsync(
                job.Id,
                metrics,
                "search-progress",
                $"Processed SERP query '{serpQuery}' with {newUrls.Count} URLs (all cached/duplicate).",
                ct);

            return Array.Empty<Learning>();
        }

        if (urlsToProcess.Count > MaxUrlsPerSerpQuery)
        {
            urlsToProcess = urlsToProcess.Take(MaxUrlsPerSerpQuery).ToList();
        }

        // Add work units for each URL we’re about to process
        metrics.TotalWorkUnits += urlsToProcess.Count;

        var collected     = new List<Learning>();
        var collectedLock = new object();
        var semaphore     = new SemaphoreSlim(MaxUrlParallelism);

        var tasks = urlsToProcess.Select(async url =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var result = await ProcessUrlAsync(
                    job,
                    serpQuery,
                    url,
                    clarificationsText,
                    queryHash,
                    ct);

                if (result.Learnings.Count > 0)
                {
                    lock (collectedLock)
                    {
                        collected.AddRange(result.Learnings);
                    }
                }

                lock (_metricsLock)
                {
                    if (result.UsedCache)
                    {
                        metrics.UrlsServedFromCache++;
                        metrics.TotalLearningsReused += result.Learnings.Count;
                    }
                    else
                    {
                        metrics.UrlsProcessedForContent++;
                        metrics.TotalLearningsGenerated += result.Learnings.Count;
                    }

                    if (result.HadError)
                    {
                        metrics.ExtractionFailures++;
                    }

                    // one URL finished
                    metrics.CompletedWorkUnits += 1;
                }

                // optional: per-URL progress ping, or throttle if too noisy
                await ReportProgressAsync(
                    job.Id,
                    metrics,
                    "url-progress",
                    $"Processed URL {url}",
                    ct);
            }
            finally
            {
                semaphore.Release();
            }
    });

        await Task.WhenAll(tasks);

        await ReportProgressAsync(
            job.Id,
            metrics,
            "search-progress",
            $"Processed SERP query '{serpQuery}' with {newUrls.Count} URLs ({urlsToProcess.Count} newly processed).",
            ct);

        return collected;
    }


    private sealed record UrlProcessingResult(
    IReadOnlyList<Learning> Learnings,
    bool UsedCache,
    bool HadError);

    private async Task<UrlProcessingResult> ProcessUrlAsync(
        ResearchJob job,
        string serpQuery,
        string url,
        string clarificationsText,
        string queryHash,
        CancellationToken ct)
    {
        // 1. Fetch content
        var content = await _crawlClient.FetchContentAsync(url, ct);

        if (string.IsNullOrWhiteSpace(content))
        {
            _logger.LogWarning("Empty content for URL {Url}, skipping learnings extraction.", url);
            return new UrlProcessingResult(Array.Empty<Learning>(), UsedCache: false, HadError: true);
        }

        // 2. Upsert ScrapedPage
        var page = await _contentStore.UpsertScrapedPageAsync(
            url: url,
            content: content,
            language: job.TargetLanguage,
            region: job.Region,
            ct: ct);

        // 3. Try reuse cached learnings
        var cached = await _contentStore.GetLearningsForPageAndQueryAsync(page.Id, queryHash, ct);
        if (cached.Count > 0)
        {
            _logger.LogInformation(
                "Reusing {Count} cached learnings for URL {Url} (PageId={PageId}, QueryHash={QueryHash}).",
                cached.Count, url, page.Id, queryHash);

            return new UrlProcessingResult(cached.ToList(), UsedCache: true, HadError: false);
        }

        // 4. Extract new learnings with LLM
        try
        {
            var extractedTexts = await _learningSvc.ExtractLearningsAsync(
                job.Query,
                clarificationsText,
                page,
                url,
                job.TargetLanguage,
                ct);

            if (extractedTexts.Count == 0)
            {
                return new UrlProcessingResult(Array.Empty<Learning>(), UsedCache: false, HadError: false);
            }

            var newLearnings = extractedTexts.Select(el => new Learning
            {
                JobId     = job.Id,
                PageId    = page.Id,
                QueryHash = queryHash,
                SourceUrl = url,
                Text      = el.Text,
                ImportanceScore = el.Importance,
                CreatedAt = DateTime.UtcNow
            }).ToList();
            

            //THIS IS SHIT!! REFACTOR!!1
            var newLearningsWithEmbeddings = await _embeddingSvc.PopulateEmbeddingsAsync(newLearnings, ct);
            await _contentStore.AddLearningsAsync(job.Id, page.Id, newLearningsWithEmbeddings, ct);

            return new UrlProcessingResult(newLearningsWithEmbeddings, UsedCache: false, HadError: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error during learnings extraction for URL {Url} (SERP query '{Query}'). SKIPPING",
                url,
                serpQuery);

            return new UrlProcessingResult(Array.Empty<Learning>(), UsedCache: false, HadError: true);
        }
    }

    // ----------------- helper (unchanged) -----------------

    public string FormatClarifications(IEnumerable<Clarification> clarifications)
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

    public sealed class ResearchMetrics
    {
        public int SerpQueries { get; set; }
        public int SearchResultsTotal { get; set; }

        public int UniqueUrlsDiscovered { get; set; }
        public int UniqueUrlsProcessed { get; set; }

        public int UrlsProcessedForContent { get; set; }
        public int UrlsServedFromCache { get; set; }

        public int ExtractionFailures { get; set; }
        public int TotalLearningsGenerated { get; set; }
        public int TotalLearningsReused { get; set; }

        public double TotalWorkUnits { get; set; } = 2; // planning + final synthesis
        public double CompletedWorkUnits { get; set; } = 0;

        public int CurrentProgressPercent
        {
            get
            {
                if (TotalWorkUnits <= 0) return 0;
                var p = (int)Math.Round(CompletedWorkUnits / TotalWorkUnits * 100.0);
                return Math.Clamp(p, 0, 100);
            }
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Research metrics summary:");
            sb.AppendLine($"  SERP queries:              {SerpQueries}");
            sb.AppendLine($"  Search results (total):    {SearchResultsTotal}");
            sb.AppendLine($"  Unique URLs discovered:    {UniqueUrlsDiscovered}");
            sb.AppendLine($"  Unique URLs processed:     {UniqueUrlsProcessed}");
            sb.AppendLine($"  URLs processed (fresh):    {UrlsProcessedForContent}");
            sb.AppendLine($"  URLs served from cache:    {UrlsServedFromCache}");
            sb.AppendLine($"  New learnings generated:   {TotalLearningsGenerated}");
            sb.AppendLine($"  Learnings reused (cache):  {TotalLearningsReused}");
            sb.AppendLine($"  Extraction failures:       {ExtractionFailures}");
            sb.AppendLine($"  Work units:                {CompletedWorkUnits}/{TotalWorkUnits} ({CurrentProgressPercent}%)");
            return sb.ToString();
        }
    }

    private async Task ReportProgressAsync(
        Guid jobId,
        ResearchMetrics metrics,
        string stage,
        string message,
        CancellationToken ct)
    {
        var percent = metrics.CurrentProgressPercent;
        var decoratedMessage = $"[{percent}%] {message}";

        await _jobStore.AppendEventAsync(
            jobId,
            new ResearchEvent(DateTimeOffset.UtcNow, stage, decoratedMessage),
            ct);
    }
}
