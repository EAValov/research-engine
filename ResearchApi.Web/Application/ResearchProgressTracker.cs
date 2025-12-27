using System.Diagnostics;
using System.Text;
using ResearchApi.Domain;

namespace ResearchApi.Application;

public sealed class ResearchProgressTracker
{
    private readonly Guid _jobId;
    private readonly IResearchJobStore _jobStore;

    private readonly object _lock = new();
    private readonly Stopwatch _sw = Stopwatch.StartNew();

    // throttle: emit at most every N ms for noisy stages (like URL processing)
    private readonly int _minEmitIntervalMs;
    private long _lastEmitTicks;

    public ResearchMetrics Job { get; } = new();
    public SynthesisMetrics Synthesis { get; } = new();

    public ResearchProgressTracker(Guid jobId, IResearchJobStore jobStore, int minEmitIntervalMs = 250)
    {
        _jobId = jobId;
        _jobStore = jobStore;
        _minEmitIntervalMs = Math.Clamp(minEmitIntervalMs, 0, 10_000);
    }

    // ---------------- Job events (prefix with [xx%]) ----------------

    public Task InfoAsync(ResearchEventStage stage, string message, CancellationToken ct = default)
        => EmitJobAsync(stage, message, ct, force: true);

    public Task ReportAsync(ResearchEventStage stage, string message, CancellationToken ct = default)
        => EmitJobAsync(stage, message, ct, force: false);

    private Task EmitJobAsync(ResearchEventStage stage, string message, CancellationToken ct, bool force)
    {
        var percent = Job.CurrentProgressPercent;
        var decorated = $"[{percent}%] {message}";
        return EmitRawAsync(stage, decorated, ct, force);
    }

    // ---------------- Synthesis events (NO % prefix) ----------------

    /// <summary>
    /// Like InfoAsync, but does NOT prefix with [xx%]. Intended for synthesis regen / think-block UX.
    /// Still persists + publishes via AppendEventAsync.
    /// </summary>
    public Task InfoSynthesisAsync(ResearchEventStage stage, string message, CancellationToken ct = default)
        => EmitRawAsync(stage, message, ct, force: true);

    public Task SynthesisCompletedAsync(Guid synthesisID, string message, CancellationToken ct = default)
        => EmitRawAsync(ResearchEventStage.Completed, message, ct, force: true, synthesisID);

    /// <summary>
    /// Like ReportAsync, but does NOT prefix with [xx%] and is throttled unless force=true.
    /// </summary>
    public Task ReportSynthesisAsync(ResearchEventStage stage, string message, CancellationToken ct = default)
        => EmitRawAsync(stage, message, ct, force: false);

    // ---------------- shared emitter ----------------

    private async Task EmitRawAsync(ResearchEventStage stage, string message, CancellationToken ct, bool force, Guid? SynthesisId = null)
    {
        if (!force && _minEmitIntervalMs > 0)
        {
            var now = _sw.ElapsedMilliseconds;
            var last = Interlocked.Read(ref _lastEmitTicks);
            if (now - last < _minEmitIntervalMs)
                return;

            Interlocked.Exchange(ref _lastEmitTicks, now);
        }

        var ev = new ResearchEvent(DateTimeOffset.UtcNow, stage, message);

        if(SynthesisId is not null)
            ev.SynthesisId = SynthesisId;

        await _jobStore.AppendEventAsync(
            _jobId,
            ev,
            ct);
    }

    // ---------------- Job metrics (existing behavior) ----------------

    public void AddPlannedSerpQueries(int count)
    {
        lock (_lock)
        {
            Job.SerpQueries += count;
            Job.TotalWorkUnits += count;
        }
    }

    public void SerpSearchCompleted(int resultCount)
    {
        lock (_lock)
        {
            Job.SearchResultsTotal += resultCount;
            Job.CompletedWorkUnits += 1;
        }
    }

    public void UrlsQueued(int count)
    {
        lock (_lock)
        {
            Job.TotalWorkUnits += count;
        }
    }

    public void UrlProcessed(bool usedCache, bool hadError, int learningCount)
    {
        lock (_lock)
        {
            if (usedCache)
            {
                Job.UrlsServedFromCache++;
                Job.TotalLearningsReused += learningCount;
            }
            else
            {
                Job.UrlsProcessedForContent++;
                Job.TotalLearningsGenerated += learningCount;
            }

            if (hadError) Job.ExtractionFailures++;
            Job.CompletedWorkUnits += 1;
        }
    }

    public void SynthesisPlanned()
    {
        lock (_lock)
        {
            Job.TotalWorkUnits += 1;
        }
    }

    public void SynthesisCompleted()
    {
        lock (_lock)
        {
            Job.CompletedWorkUnits += 1;
        }
    }

    public Task EmitMetricsSummaryAsync(CancellationToken ct = default)
        => InfoAsync(ResearchEventStage.Metrics, Job.ToString(), ct);

    // ---------------- Synthesis ephemeral metrics ----------------

    /// <summary>
    /// Reset synthesis-only metrics before each synthesis run (especially regeneration).
    /// Does not touch job metrics.
    /// </summary>
    public void ResetSynthesisMetrics()
    {
        lock (_lock)
        {
            Synthesis.Reset();
        }
    }

    public void SynthesisPlanned(int sectionCount)
    {
        lock (_lock)
        {
            Synthesis.SectionsPlanned = sectionCount;
        }
    }

    public void SynthesisPlanCompleted()
    {
        lock (_lock)
        {
            Synthesis.PlanCompleted = true;
        }
    }

    public void SynthesisSectionStarted(string? title = null)
    {
        lock (_lock)
        {
            Synthesis.SectionsStarted++;
            if (!string.IsNullOrWhiteSpace(title))
                Synthesis.CurrentSectionTitle = title;
        }
    }

    public void SynthesisSectionWritten(bool repaired = false)
    {
        lock (_lock)
        {
            Synthesis.SectionsWritten++;
            if (repaired) Synthesis.Repairs++;
            Synthesis.CurrentSectionTitle = null;
        }
    }

    public void SynthesisSectionSummarized()
    {
        lock (_lock)
        {
            Synthesis.SectionsSummarized++;
        }
    }

    public void SynthesisConclusionCompleted()
    {
        lock (_lock)
        {
            Synthesis.ConclusionCompleted = true;
        }
    }

    public void SynthesisFinalized()
    {
        lock (_lock)
        {
            Synthesis.Finalized = true;
        }
    }

    public Task EmitSynthesisMetricsSummaryAsync(CancellationToken ct = default)
        => InfoSynthesisAsync(ResearchEventStage.Metrics, Synthesis.ToString(), ct);
        

    public void MarkAllCompleted()
    {
        lock (_lock)
        {
            Job.CompletedWorkUnits = Job.TotalWorkUnits;
        }
    }

    // ---------------- metrics types ----------------

    public sealed class ResearchMetrics
    {
        public int SerpQueries { get; internal set; }
        public int SearchResultsTotal { get; internal set; }

        public int UrlsProcessedForContent { get; internal set; }
        public int UrlsServedFromCache { get; internal set; }

        public int ExtractionFailures { get; internal set; }
        public int TotalLearningsGenerated { get; internal set; }
        public int TotalLearningsReused { get; internal set; }

        // planning + synthesis baseline
        public double TotalWorkUnits { get; internal set; } = 2;
        public double CompletedWorkUnits { get; internal set; } = 0;

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
            sb.AppendLine($"  URLs processed (fresh):    {UrlsProcessedForContent}");
            sb.AppendLine($"  URLs served from cache:    {UrlsServedFromCache}");
            sb.AppendLine($"  New learnings generated:   {TotalLearningsGenerated}");
            sb.AppendLine($"  Learnings reused (cache):  {TotalLearningsReused}");
            sb.AppendLine($"  Extraction failures:       {ExtractionFailures}");
            sb.AppendLine($"  Work units:                {CompletedWorkUnits}/{TotalWorkUnits} ({CurrentProgressPercent}%)");
            return sb.ToString().TrimEnd();
        }
    }

    public sealed class SynthesisMetrics
    {
        public int SectionsPlanned { get; internal set; }
        public bool PlanCompleted { get; internal set; }

        public int SectionsStarted { get; internal set; }
        public int SectionsWritten { get; internal set; }
        public int SectionsSummarized { get; internal set; }

        public bool ConclusionCompleted { get; internal set; }
        public bool Finalized { get; internal set; }

        public int Repairs { get; internal set; }
        public string? CurrentSectionTitle { get; internal set; }

        internal void Reset()
        {
            SectionsPlanned = 0;
            PlanCompleted = false;

            SectionsStarted = 0;
            SectionsWritten = 0;
            SectionsSummarized = 0;

            ConclusionCompleted = false;
            Finalized = false;

            Repairs = 0;
            CurrentSectionTitle = null;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Synthesis metrics (ephemeral):");
            sb.AppendLine($"  Sections planned:           {SectionsPlanned}");
            sb.AppendLine($"  Plan completed:             {PlanCompleted}");
            sb.AppendLine($"  Sections started:           {SectionsStarted}");
            sb.AppendLine($"  Sections written:           {SectionsWritten}");
            sb.AppendLine($"  Section summaries:          {SectionsSummarized}");
            sb.AppendLine($"  Conclusion completed:       {ConclusionCompleted}");
            sb.AppendLine($"  Finalized:                  {Finalized}");
            sb.AppendLine($"  Repairs applied:            {Repairs}");
            if (!string.IsNullOrWhiteSpace(CurrentSectionTitle))
                sb.AppendLine($"  Current section:            {CurrentSectionTitle}");
            return sb.ToString().TrimEnd();
        }
    }
}
