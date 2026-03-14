namespace ResearchEngine.WebUI.State;

public static class AppStateDefaults
{
    public const int CurrentVersion = 1;

    public static AppState CreateDefault()
    {
        return new AppState
        {
            Version = CurrentVersion,
            Theme = "system",
            JobList = new JobListState
            {
                SearchText = "",
                SortKey = "created",
                PinnedJobs = new List<Guid>()
            },
            Api = new ApiConnectionState
            {
                BaseUrl = "",
                ApiKey = "",
                AuthEnabled = true
            },
            Jobs = new Dictionary<Guid, JobUiState>()
        };
    }

    public static JobUiState CreateDefaultJobUi()
    {
        return new JobUiState
        {
            EvidenceOpen = false,
            EvidenceTab = "sources",
            SelectedSynthesis = "latest",
            CompareAId = null,
            CompareBId = null,
            SourcesFilter = "",
            LearningsFilter = "",
            LearningsLoadedCount = 25,
            Overrides = new OverridesState()
        };
    }
}
