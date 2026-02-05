namespace ResearchEngine.Blazor.State;

public sealed class AppState
{
    public int Version { get; set; } = 1;

    // "light" | "dark" | "system"
    public string? Theme { get; set; } = "system";

    public Guid? LastOpenedJobId { get; set; }

    public JobListState JobList { get; set; } = new();

    // per-job UI state
    public Dictionary<Guid, JobUiState> Jobs { get; set; } = new();
}

public sealed class JobListState
{
    public string SearchText { get; set; } = "";
    public string SortKey { get; set; } = "created"; // "created"|"updated"|"status" (prototype)
    public List<Guid> PinnedJobs { get; set; } = new();
}

public sealed class JobUiState
{
    public bool EvidenceOpen { get; set; }
    public string EvidenceTab { get; set; } = "sources"; // "sources"|"learnings"

    // Selected synthesis: store "latest" or Guid string
    public string SelectedSynthesis { get; set; } = "latest";

    public string? CompareAId { get; set; }
    public string? CompareBId { get; set; }

    public string SourcesFilter { get; set; } = "";
    public string LearningsFilter { get; set; } = "";

    public int LearningsLoadedCount { get; set; } = 25; // optional restore

    public OverridesState Overrides { get; set; } = new();
}

public sealed class OverridesState
{
    public List<Guid> PinnedSourceIds { get; set; } = new();
    public List<Guid> ExcludedSourceIds { get; set; } = new();
    public List<Guid> PinnedLearningIds { get; set; } = new();
    public List<Guid> ExcludedLearningIds { get; set; } = new();
}