using System.Text.Json;
using ResearchEngine.Blazor.Services;

namespace ResearchEngine.Blazor.State;

public sealed class AppStateStore
{
    public const string StorageKey = "researchEngine:appState:v1";

    private readonly LocalStorageService _storage;

    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private AppState _state = AppStateDefaults.CreateDefault();
    private bool _initialized;

    // Debounce save
    private int _saveVersion;
    private Task? _saveTask;

    public AppStateStore(LocalStorageService storage)
    {
        _storage = storage;
    }

    public AppState State => _state;

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        var raw = await _storage.GetAsync(StorageKey);
        if (string.IsNullOrWhiteSpace(raw))
        {
            _state = AppStateDefaults.CreateDefault();
            QueueSave();
            return;
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<AppState>(raw, _json);

            if (loaded is null)
            {
                _state = AppStateDefaults.CreateDefault();
                QueueSave();
                return;
            }

            // Version checks
            if (loaded.Version > AppStateDefaults.CurrentVersion)
            {
                _state = AppStateDefaults.CreateDefault();
                QueueSave();
                return;
            }

            if (loaded.Version < AppStateDefaults.CurrentVersion)
            {
                loaded = Migrate(loaded);
            }

            // Normalize / bound lists to avoid unbounded growth
            Normalize(loaded);

            _state = loaded;
        }
        catch
        {
            // Corrupted storage => reset
            _state = AppStateDefaults.CreateDefault();
            QueueSave();
        }
    }

    private static AppState Migrate(AppState from)
    {
        // Migration stub (v1 only right now)
        from.Version = AppStateDefaults.CurrentVersion;
        return from;
    }

    private static void Normalize(AppState s)
    {
        s.Theme = NormalizeTheme(s.Theme);

        if (s.JobList is null)
            s.JobList = new JobListState();

        s.JobList.SearchText ??= "";
        s.JobList.SortKey ??= "created";
        s.JobList.PinnedJobs ??= new List<Guid>();

        // Bound pinned jobs
        if (s.JobList.PinnedJobs.Count > 200)
            s.JobList.PinnedJobs = s.JobList.PinnedJobs.Take(200).ToList();

        if (s.Api is null)
            s.Api = new ApiConnectionState();

        s.Api.BaseUrl = NormalizeApiBaseUrl(s.Api.BaseUrl);
        s.Api.BearerToken ??= "";

        s.Jobs ??= new Dictionary<Guid, JobUiState>();

        foreach (var kv in s.Jobs.ToList())
        {
            var ui = kv.Value ?? AppStateDefaults.CreateDefaultJobUi();

            ui.EvidenceTab = NormalizeEvidenceTab(ui.EvidenceTab);
            ui.SelectedSynthesis = string.IsNullOrWhiteSpace(ui.SelectedSynthesis) ? "latest" : ui.SelectedSynthesis;

            ui.SourcesFilter ??= "";
            ui.LearningsFilter ??= "";
            if (ui.LearningsLoadedCount <= 0) ui.LearningsLoadedCount = 25;

            ui.Overrides ??= new OverridesState();
            ui.Overrides.PinnedSourceIds ??= new List<Guid>();
            ui.Overrides.ExcludedSourceIds ??= new List<Guid>();
            ui.Overrides.PinnedLearningIds ??= new List<Guid>();
            ui.Overrides.ExcludedLearningIds ??= new List<Guid>();

            Bound(ui.Overrides.PinnedSourceIds, 500);
            Bound(ui.Overrides.ExcludedSourceIds, 500);
            Bound(ui.Overrides.PinnedLearningIds, 1000);
            Bound(ui.Overrides.ExcludedLearningIds, 1000);

            s.Jobs[kv.Key] = ui;
        }
    }

    private static void Bound(List<Guid> list, int max)
    {
        if (list.Count > max)
        {
            list.RemoveRange(max, list.Count - max);
        }
    }

    private static string NormalizeTheme(string? t)
        => t is null ? "system"
         : t.Equals("dark", StringComparison.OrdinalIgnoreCase) ? "dark"
         : t.Equals("light", StringComparison.OrdinalIgnoreCase) ? "light"
         : "system";

    private static string NormalizeApiBaseUrl(string? url)
        => (url ?? string.Empty).Trim();

    private static string NormalizeEvidenceTab(string? t)
        => t is not null && t.Equals("learnings", StringComparison.OrdinalIgnoreCase) ? "learnings" : "sources";

    public void ClearAll()
    {
        _state = AppStateDefaults.CreateDefault();
        QueueSave(immediate: true);
    }

    public async Task ClearAllAsync()
    {
        _state = AppStateDefaults.CreateDefault();
        try
        {
            await _storage.RemoveAsync(StorageKey);
        }
        catch { }
        QueueSave(immediate: true);
    }

    // Theme
    public string GetTheme() => NormalizeTheme(_state.Theme);
    public void SetTheme(string theme)
    {
        _state.Theme = NormalizeTheme(theme);
        QueueSave();
    }

    // API connection settings
    public ApiConnectionState GetApiSettings() => _state.Api;

    public void SetApiSettings(string baseUrl, string bearerToken, bool authEnabled)
    {
        _state.Api.BaseUrl = NormalizeApiBaseUrl(baseUrl);
        _state.Api.BearerToken = bearerToken?.Trim() ?? "";
        _state.Api.AuthEnabled = authEnabled;
        QueueSave();
    }

    // JobList
    public JobListState GetJobList() => _state.JobList;
    public void SetJobListSearch(string text)
    {
        _state.JobList.SearchText = text ?? "";
        QueueSave();
    }

    public void SetJobListSort(string sortKey)
    {
        _state.JobList.SortKey = string.IsNullOrWhiteSpace(sortKey) ? "created" : sortKey;
        QueueSave();
    }

    public void SetLastOpenedJob(Guid jobId)
    {
        _state.LastOpenedJobId = jobId;
        QueueSave();
    }

    // Per-job UI state
    public JobUiState GetOrCreateJobUi(Guid jobId)
    {
        if (!_state.Jobs.TryGetValue(jobId, out var ui) || ui is null)
        {
            ui = AppStateDefaults.CreateDefaultJobUi();
            _state.Jobs[jobId] = ui;
            QueueSave();
        }

        return ui;
    }

    public void UpdateJobUi(Guid jobId, Action<JobUiState> mutator)
    {
        var ui = GetOrCreateJobUi(jobId);
        mutator(ui);
        QueueSave();
    }

    // Debounced persistence
    private void QueueSave(bool immediate = false)
    {
        _saveVersion++;
        var versionSnapshot = _saveVersion;

        if (immediate)
        {
            _saveTask = PersistAsync(versionSnapshot, delayMs: 0);
            return;
        }

        _saveTask ??= PersistAsync(versionSnapshot, delayMs: 400);
    }

    private async Task PersistAsync(int versionSnapshot, int delayMs)
    {
        try
        {
            if (delayMs > 0)
                await Task.Delay(delayMs);

            // If any newer change was queued during delay, restart (debounce)
            if (versionSnapshot != _saveVersion)
            {
                _saveTask = PersistAsync(_saveVersion, delayMs: 400);
                return;
            }

            var json = JsonSerializer.Serialize(_state, _json);
            await _storage.SetAsync(StorageKey, json);
        }
        catch
        {
            // ignore (storage blocked/unavailable)
        }
        finally
        {
            // allow new debounced run
            _saveTask = null;
        }
    }
}
