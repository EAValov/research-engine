using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using ResearchEngine.WebUI.Services;
using ResearchEngine.WebUI.State;

namespace ResearchEngine.WebUI.Components.Common;

public partial class SettingsDialog : ComponentBase
{
    private enum ApiTestStatus
    {
        None,
        Success,
        Failure
    }

    private sealed record RuntimeSettingsCallResult(
        RuntimeSettingsResponseModel? Data,
        string? Error = null,
        Dictionary<string, string>? FieldErrors = null);

    private sealed record ChatModelCatalogCallResult(
        ChatModelCatalogResponseModel? Data,
        string? Error = null,
        Dictionary<string, string>? FieldErrors = null);

    private sealed record CrawlApiProbeCallResult(
        bool Success,
        string? Error = null,
        Dictionary<string, string>? FieldErrors = null);

    private static readonly JsonSerializerOptions RuntimeSettingsJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Inject] private AppStateStore AppState { get; set; } = default!;
    [Inject] private ApiConnectionSettings ApiConnection { get; set; } = default!;

    private bool _settingsOpen;
    private bool _savingSettings;
    private bool _testingApiConnection;
    private bool _testingChatApi;
    private bool _testingCrawlApi;
    private bool _loadingRuntimeSettings;
    private bool _runtimeSettingsLoaded;
    private bool _applyCompleted;
    private int _applyFeedbackVersion;
    private string _draftApiBaseUrl = string.Empty;
    private string _draftApiKey = string.Empty;
    private bool _draftApiAuthEnabled = true;
    private ResearchOrchestratorConfigModel _draftResearchOptions = new();
    private LearningSimilarityOptionsModel _draftLearningOptions = new();
    private RuntimeChatConfigModel _draftChatConfig = new();
    private RuntimeCrawlConfigModel _draftCrawlConfig = new();
    private string _draftChatApiKey = string.Empty;
    private string _draftCrawlApiKey = string.Empty;
    private bool _draftChatAuthEnabled = true;
    private bool _draftCrawlAuthEnabled = true;
    private readonly List<string> _chatModelOptions = new();
    private ResearchOrchestratorConfigModel _baselineResearchOptions = new();
    private LearningSimilarityOptionsModel _baselineLearningOptions = new();
    private RuntimeChatConfigModel _baselineChatConfig = new();
    private RuntimeCrawlConfigModel _baselineCrawlConfig = new();
    private readonly Dictionary<string, string> _fieldErrors = new(StringComparer.Ordinal);
    private bool _loadingChatModelOptions;
    private string? _chatModelOptionsError;
    private string? _settingsError;
    private string? _settingsSuccess;
    private ApiTestStatus _apiConnectionTestStatus;
    private ApiTestStatus _chatApiTestStatus;
    private ApiTestStatus _crawlApiTestStatus;
    private string? _apiConnectionTestMessage;
    private string? _chatApiTestMessage;
    private string? _crawlApiTestMessage;
    private string _baselineApiBaseUrl = string.Empty;
    private string _baselineApiKey = string.Empty;
    private bool _baselineApiAuthEnabled;

    private bool IsAnyTesting => _testingApiConnection || _testingChatApi || _testingCrawlApi;

    private bool CanTestApiConnection =>
        ApiConnectionSettings.NormalizeBaseUrl(_draftApiBaseUrl) is not null
        && !_testingApiConnection
        && !_savingSettings;

    private bool CanTestChatApi =>
        _runtimeSettingsLoaded
        && ApiConnectionSettings.NormalizeBaseUrl(_draftChatConfig.Endpoint) is not null
        && !_testingChatApi
        && !_savingSettings;

    private bool CanTestCrawlApi =>
        _runtimeSettingsLoaded
        && ApiConnectionSettings.NormalizeBaseUrl(_draftCrawlConfig.Endpoint) is not null
        && !_testingCrawlApi
        && !_savingSettings;

    private bool HasApiConnectionTestFeedback => _apiConnectionTestStatus != ApiTestStatus.None;
    private bool HasChatApiTestFeedback => _chatApiTestStatus != ApiTestStatus.None;
    private bool HasCrawlApiTestFeedback => _crawlApiTestStatus != ApiTestStatus.None;

    private bool IsRuntimeSettingsDisabled => !_runtimeSettingsLoaded || _loadingRuntimeSettings || _savingSettings;
    private bool IsChatModelSelectionDisabled => IsRuntimeSettingsDisabled || (_loadingChatModelOptions && ChatModelDropdownOptions.Count == 0);
    private IReadOnlyList<string> ChatModelDropdownOptions => BuildChatModelDropdownOptions();

    private bool HasPendingApiConnectionChange =>
        !string.Equals(
            ApiConnectionSettings.NormalizeBaseUrl(_draftApiBaseUrl),
            ApiConnectionSettings.NormalizeBaseUrl(_baselineApiBaseUrl),
            StringComparison.OrdinalIgnoreCase)
        || !string.Equals(_draftApiKey, _baselineApiKey, StringComparison.Ordinal)
        || _draftApiAuthEnabled != _baselineApiAuthEnabled;

    private bool HasRuntimeSettingsChanges =>
        _runtimeSettingsLoaded
        && (!ResearchOptionsEqual(_draftResearchOptions, _baselineResearchOptions)
            || !LearningOptionsEqual(_draftLearningOptions, _baselineLearningOptions)
            || !ChatConfigEqual(_draftChatConfig, _baselineChatConfig)
            || !CrawlConfigEqual(_draftCrawlConfig, _baselineCrawlConfig)
            || !string.IsNullOrWhiteSpace(_draftChatApiKey)
            || !string.IsNullOrWhiteSpace(_draftCrawlApiKey));

    private bool HasAnyChanges
        => HasPendingApiConnectionChange || HasRuntimeSettingsChanges;

    private bool CanApplySettings
        => HasAnyChanges && !_savingSettings && !IsAnyTesting && !_loadingRuntimeSettings;

    private string ApplyButtonText
        => _savingSettings
            ? "Applying..."
            : (_applyCompleted && !HasAnyChanges ? "Applied" : "Apply");

    private string ApplyButtonClass
        => _applyCompleted && !HasAnyChanges
            ? "primarybtn is-applied"
            : "primarybtn";

    private async Task OpenSettingsAsync()
    {
        ResetAppliedState();
        LoadApiDraftFromRuntime();
        CaptureApiBaseline();
        ResetSettingsMessages();
        ClearAllApiTestFeedback();
        ClearFieldErrors();
        _settingsOpen = true;
        await LoadRuntimeSettingsForDraftConnectionAsync(showFailureMessage: true);
    }

    private void CloseSettings()
    {
        ResetAppliedState();
        _settingsOpen = false;
        ResetSettingsMessages();
        ClearAllApiTestFeedback();
        ClearFieldErrors();
    }

    private void LoadApiDraftFromRuntime()
    {
        _draftApiBaseUrl = ApiConnection.ApiBaseUrl;
        _draftApiKey = ApiConnection.ApiKey;
        _draftApiAuthEnabled = ApiConnection.AuthEnabled;
    }

    private async Task SaveSettingsAsync()
    {
        ResetSettingsMessages();
        ClearFieldErrors();

        ValidateApiDraft();
        if (_runtimeSettingsLoaded)
            ValidateRuntimeDraft();

        if (_fieldErrors.Count > 0)
        {
            _settingsError = "Review the highlighted settings and try again.";
            return;
        }

        _savingSettings = true;

        try
        {
            if (!_runtimeSettingsLoaded)
            {
                if (!ApplyApiDraft())
                {
                    SetFieldError("Api.BaseUrl", "Enter a valid absolute API URL that starts with http:// or https://.");
                    _settingsError = "Review the highlighted settings and try again.";
                    return;
                }

                var loaded = await LoadRuntimeSettingsForDraftConnectionAsync(showFailureMessage: false);
                _settingsSuccess = loaded ? null : "API settings applied.";
                StartAppliedState();
                return;
            }

            var request = new UpdateRuntimeSettingsRequestModel
            {
                ResearchOrchestratorConfig = CloneResearchOptions(_draftResearchOptions),
                LearningSimilarityOptions = CloneLearningOptions(_draftLearningOptions),
                ChatConfig = CloneChatConfigForUpdate(_draftChatConfig, _draftChatApiKey, _draftChatAuthEnabled),
                CrawlConfig = CloneCrawlConfigForUpdate(_draftCrawlConfig, _draftCrawlApiKey, _draftCrawlAuthEnabled)
            };

            var saveResult = await UpdateRuntimeSettingsDirectAsync(
                _draftApiBaseUrl,
                _draftApiAuthEnabled,
                _draftApiKey,
                request,
                CancellationToken.None);

            if (saveResult.Error is not null)
            {
                ApplyServerValidationErrors(saveResult.FieldErrors);
                _settingsError = saveResult.Error;
                return;
            }

            if (!ApplyApiDraft())
            {
                SetFieldError("Api.BaseUrl", "Enter a valid absolute API URL that starts with http:// or https://.");
                _settingsError = "Review the highlighted settings and try again.";
                return;
            }

            if (saveResult.Data is not null)
            {
                ApplyRuntimeSettings(saveResult.Data);
            }

            _settingsSuccess = null;
            StartAppliedState();
        }
        finally
        {
            _savingSettings = false;
        }
    }

    private bool ApplyApiDraft()
    {
        var success = ApiConnection.TryApply(_draftApiBaseUrl, _draftApiKey, _draftApiAuthEnabled);
        if (!success)
            return false;

        _draftApiBaseUrl = ApiConnection.ApiBaseUrl;
        AppState.SetApiSettings(_draftApiBaseUrl, _draftApiKey, _draftApiAuthEnabled);
        CaptureApiBaseline();
        return true;
    }

    private void OnApiBaseUrlChanged(ChangeEventArgs e)
    {
        _draftApiBaseUrl = e.Value?.ToString() ?? string.Empty;
        ResetSettingsMessages();
        ClearAllApiTestFeedback();
        _fieldErrors.Remove("Api.BaseUrl");
    }

    private void OnApiKeyChanged(ChangeEventArgs e)
    {
        _draftApiKey = e.Value?.ToString() ?? string.Empty;
        ResetSettingsMessages();
        ClearAllApiTestFeedback();
    }

    private void OnApiAuthChanged(ChangeEventArgs e)
    {
        _draftApiAuthEnabled = e.Value is bool value && value;
        ResetSettingsMessages();
        ClearAllApiTestFeedback();
    }

    private void OnChatEndpointChanged(ChangeEventArgs e)
    {
        _draftChatConfig.Endpoint = e.Value?.ToString() ?? string.Empty;
        ResetSettingsMessages();
        ClearChatApiTestFeedback();
        _fieldErrors.Remove("ChatConfig.Endpoint");
        ResetChatModelOptionsState();
    }

    private void OnChatModelIdChanged(ChangeEventArgs e)
    {
        _draftChatConfig.ModelId = e.Value?.ToString() ?? string.Empty;
        ResetSettingsMessages();
        _fieldErrors.Remove("ChatConfig.ModelId");
    }

    private void OnChatApiKeyChanged(ChangeEventArgs e)
    {
        _draftChatApiKey = e.Value?.ToString() ?? string.Empty;
        ResetSettingsMessages();
        ClearChatApiTestFeedback();
        _fieldErrors.Remove("ChatConfig.ApiKey");
        ResetChatModelOptionsState();
    }

    private void OnChatAuthChanged(ChangeEventArgs e)
    {
        _draftChatAuthEnabled = e.Value is bool value && value;
        ResetSettingsMessages();
        ClearChatApiTestFeedback();
        ResetChatModelOptionsState();
    }

    private void OnCrawlEndpointChanged(ChangeEventArgs e)
    {
        _draftCrawlConfig.Endpoint = e.Value?.ToString() ?? string.Empty;
        ResetSettingsMessages();
        ClearCrawlApiTestFeedback();
        _fieldErrors.Remove("CrawlConfig.Endpoint");
    }

    private void OnCrawlApiKeyChanged(ChangeEventArgs e)
    {
        _draftCrawlApiKey = e.Value?.ToString() ?? string.Empty;
        ResetSettingsMessages();
        ClearCrawlApiTestFeedback();
        _fieldErrors.Remove("CrawlConfig.ApiKey");
    }

    private void OnCrawlAuthChanged(ChangeEventArgs e)
    {
        _draftCrawlAuthEnabled = e.Value is bool value && value;
        ResetSettingsMessages();
        ClearCrawlApiTestFeedback();
    }

    private void OnChatMaxContextLengthChanged(ChangeEventArgs e)
    {
        var value = e.Value?.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            _draftChatConfig.MaxContextLength = null;
        }
        else if (int.TryParse(value, out var parsed))
        {
            _draftChatConfig.MaxContextLength = parsed;
        }

        ResetSettingsMessages();
        _fieldErrors.Remove("ChatConfig.MaxContextLength");
    }

    private async Task TestApiConnectionAsync()
    {
        ResetSettingsMessages();
        ClearApiConnectionTestFeedback();
        ClearFieldErrors();
        ValidateApiDraft();

        if (_fieldErrors.Count > 0)
        {
            SetApiConnectionTestFeedback(false, "Review the highlighted API settings and try again.");
            return;
        }

        var normalizedBaseUrl = ApiConnectionSettings.NormalizeBaseUrl(_draftApiBaseUrl);
        if (normalizedBaseUrl is null)
        {
            SetFieldError("Api.BaseUrl", "Enter a valid absolute API URL that starts with http:// or https://.");
            SetApiConnectionTestFeedback(false, "Review the highlighted API settings and try again.");
            return;
        }

        _testingApiConnection = true;
        try
        {
            var probe = await ProbeEndpointDirectAsync(
                normalizedBaseUrl,
                "api/ping",
                _draftApiAuthEnabled,
                _draftApiKey,
                CancellationToken.None);

            if (probe.Status != HttpStatusCode.OK)
            {
                SetApiConnectionTestFeedback(false, probe.Status is { } status
                    ? $"API request failed with HTTP {(int)status}."
                    : probe.Error ?? "API request failed. Check URL, API key, CORS, and API availability.");
                return;
            }

            var loaded = await LoadRuntimeSettingsForDraftConnectionAsync(showFailureMessage: true);
            if (!loaded)
            {
                var message = _settingsError
                    ?? "API request succeeded, but runtime settings could not be loaded.";
                _settingsError = null;
                SetApiConnectionTestFeedback(false, message);
                return;
            }

            SetApiConnectionTestFeedback(true, "ResearchEngine API request succeeded and runtime settings loaded.");
        }
        finally
        {
            _testingApiConnection = false;
        }
    }

    private async Task TestChatApiAsync()
    {
        ResetSettingsMessages();
        ClearChatApiTestFeedback();
        _fieldErrors.Remove("ChatConfig.Endpoint");
        _fieldErrors.Remove("ChatConfig.ApiKey");

        ValidateApiDraft();
        if (_fieldErrors.Count > 0)
        {
            SetChatApiTestFeedback(false, "Review the highlighted API settings and try again.");
            return;
        }

        if (ApiConnectionSettings.NormalizeBaseUrl(_draftChatConfig.Endpoint) is null)
        {
            SetFieldError("ChatConfig.Endpoint", "Enter a valid absolute URL that starts with http:// or https://.");
            SetChatApiTestFeedback(false, "Review the highlighted chat settings and try again.");
            return;
        }

        _testingChatApi = true;
        try
        {
            var result = await LoadChatModelOptionsAsync(CancellationToken.None);
            if (!result)
            {
                SetChatApiTestFeedback(false, _chatModelOptionsError ?? "Chat LLM API test failed.");
                return;
            }

            SetChatApiTestFeedback(true, "Chat LLM API test succeeded.");
        }
        finally
        {
            _testingChatApi = false;
        }
    }

    private async Task TestCrawlApiAsync()
    {
        ResetSettingsMessages();
        ClearCrawlApiTestFeedback();
        _fieldErrors.Remove("CrawlConfig.Endpoint");
        _fieldErrors.Remove("CrawlConfig.ApiKey");

        ValidateApiDraft();
        if (_fieldErrors.Count > 0)
        {
            SetCrawlApiTestFeedback(false, "Review the highlighted API settings and try again.");
            return;
        }

        var normalizedEndpoint = ApiConnectionSettings.NormalizeBaseUrl(_draftCrawlConfig.Endpoint);
        if (normalizedEndpoint is null)
        {
            SetFieldError("CrawlConfig.Endpoint", "Enter a valid absolute URL that starts with http:// or https://.");
            SetCrawlApiTestFeedback(false, "Review the highlighted crawl settings and try again.");
            return;
        }

        _testingCrawlApi = true;
        try
        {
            var result = await ProbeCrawlApiDirectAsync(
                _draftApiBaseUrl,
                _draftApiAuthEnabled,
                _draftApiKey,
                new CrawlApiProbeRequestModel
                {
                    Endpoint = normalizedEndpoint,
                    ApiKey = _draftCrawlAuthEnabled ? _draftCrawlApiKey : string.Empty,
                    UseStoredApiKey = _draftCrawlAuthEnabled && string.IsNullOrWhiteSpace(_draftCrawlApiKey)
                },
                CancellationToken.None);

            if (!result.Success)
            {
                ApplyServerValidationErrors(NormalizeCrawlProbeFieldErrors(result.FieldErrors));
                SetCrawlApiTestFeedback(false, result.Error ?? "Crawl API test failed.");
                return;
            }

            SetCrawlApiTestFeedback(true, "Crawl API test succeeded.");
        }
        finally
        {
            _testingCrawlApi = false;
        }
    }

    private async Task<bool> LoadRuntimeSettingsForDraftConnectionAsync(bool showFailureMessage)
    {
        _loadingRuntimeSettings = true;
        try
        {
            var result = await GetRuntimeSettingsDirectAsync(
                _draftApiBaseUrl,
                _draftApiAuthEnabled,
                _draftApiKey,
                CancellationToken.None);

            if (result.Data is not null)
            {
                ApplyRuntimeSettings(result.Data);
                return true;
            }

            _runtimeSettingsLoaded = false;
            ResetChatModelOptionsState();

            if (showFailureMessage && !string.IsNullOrWhiteSpace(result.Error))
                _settingsError = result.Error;

            return false;
        }
        finally
        {
            _loadingRuntimeSettings = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void ApplyRuntimeSettings(RuntimeSettingsResponseModel response)
    {
        _draftResearchOptions = CloneResearchOptions(response.ResearchOrchestratorConfig);
        _draftLearningOptions = CloneLearningOptions(response.LearningSimilarityOptions);
        _draftChatConfig = CloneRuntimeChatConfig(response.ChatConfig);
        _draftCrawlConfig = CloneRuntimeCrawlConfig(response.CrawlConfig);
        _draftChatApiKey = string.Empty;
        _draftCrawlApiKey = string.Empty;
        _draftChatAuthEnabled = response.ChatConfig.HasApiKey;
        _draftCrawlAuthEnabled = response.CrawlConfig.HasApiKey;
        _baselineResearchOptions = CloneResearchOptions(response.ResearchOrchestratorConfig);
        _baselineLearningOptions = CloneLearningOptions(response.LearningSimilarityOptions);
        _baselineChatConfig = CloneRuntimeChatConfig(response.ChatConfig);
        _baselineCrawlConfig = CloneRuntimeCrawlConfig(response.CrawlConfig);
        _runtimeSettingsLoaded = true;
        ResetChatModelOptionsState();
        ClearChatApiTestFeedback();
        ClearCrawlApiTestFeedback();
    }

    private void ValidateApiDraft()
    {
        if (ApiConnectionSettings.NormalizeBaseUrl(_draftApiBaseUrl) is null)
            SetFieldError("Api.BaseUrl", "Enter a valid absolute API URL that starts with http:// or https://.");
    }

    private void ValidateRuntimeDraft()
    {
        ValidateRange("ResearchOrchestratorConfig.LimitSearches", _draftResearchOptions.LimitSearches, 1, 1000);
        ValidateRange("ResearchOrchestratorConfig.MaxUrlParallelism", _draftResearchOptions.MaxUrlParallelism, 1, 1000);
        ValidateRange("ResearchOrchestratorConfig.MaxUrlsPerSerpQuery", _draftResearchOptions.MaxUrlsPerSerpQuery, 1, 1000);

        ValidateRange("LearningSimilarityOptions.MinImportance", _draftLearningOptions.MinImportance, 0f, 1f);
        ValidateRange("LearningSimilarityOptions.DiversityMaxPerUrl", _draftLearningOptions.DiversityMaxPerUrl, 1, 1000);
        ValidateRange("LearningSimilarityOptions.DiversityMaxTextSimilarity", _draftLearningOptions.DiversityMaxTextSimilarity, 0d, 1d);
        ValidateRange("LearningSimilarityOptions.MaxLearningsPerSegment", _draftLearningOptions.MaxLearningsPerSegment, 1, 1000);
        ValidateRange("LearningSimilarityOptions.MinLearningsPerSegment", _draftLearningOptions.MinLearningsPerSegment, 1, 1000);
        ValidateRange("LearningSimilarityOptions.GroupAssignSimilarityThreshold", _draftLearningOptions.GroupAssignSimilarityThreshold, 0f, 1f);
        ValidateRange("LearningSimilarityOptions.GroupSearchTopK", _draftLearningOptions.GroupSearchTopK, 1, 50);
        ValidateRange("LearningSimilarityOptions.MaxEvidenceLength", _draftLearningOptions.MaxEvidenceLength, 1, 1_000_000);

        if (ApiConnectionSettings.NormalizeBaseUrl(_draftChatConfig.Endpoint) is null)
            SetFieldError("ChatConfig.Endpoint", "Enter a valid absolute URL that starts with http:// or https://.");

        if (string.IsNullOrWhiteSpace(_draftChatConfig.ModelId))
            SetFieldError("ChatConfig.ModelId", "Model id is required.");

        if (_draftChatConfig.MaxContextLength is int maxContextLength &&
            maxContextLength < 10_000)
        {
            SetFieldError("ChatConfig.MaxContextLength", "Value must be at least 10000.");
        }

        if (_draftChatAuthEnabled && !_draftChatConfig.HasApiKey && string.IsNullOrWhiteSpace(_draftChatApiKey))
            SetFieldError("ChatConfig.ApiKey", "Enter the chat backend API key.");

        if (ApiConnectionSettings.NormalizeBaseUrl(_draftCrawlConfig.Endpoint) is null)
            SetFieldError("CrawlConfig.Endpoint", "Enter a valid absolute URL that starts with http:// or https://.");

        if (_draftLearningOptions.MinLearningsPerSegment > _draftLearningOptions.MaxLearningsPerSegment)
        {
            const string message = "Min learnings per segment must be less than or equal to max learnings per segment.";
            SetFieldError("LearningSimilarityOptions.MinLearningsPerSegment", message);
            SetFieldError("LearningSimilarityOptions.MaxLearningsPerSegment", message);
        }
    }

    private void ValidateRange(string key, int value, int min, int max)
    {
        if (value < min || value > max)
            SetFieldError(key, $"Value must be between {min} and {max}.");
    }

    private void ValidateRange(string key, float value, float min, float max)
    {
        if (value < min || value > max)
            SetFieldError(key, $"Value must be between {min:0.##} and {max:0.##}.");
    }

    private void ValidateRange(string key, double value, double min, double max)
    {
        if (value < min || value > max)
            SetFieldError(key, $"Value must be between {min:0.##} and {max:0.##}.");
    }

    private void SetFieldError(string key, string message)
    {
        _fieldErrors[key] = message;
    }

    private void ApplyServerValidationErrors(Dictionary<string, string>? serverErrors)
    {
        if (serverErrors is null)
            return;

        foreach (var pair in serverErrors)
            _fieldErrors[pair.Key] = pair.Value;
    }

    private static Dictionary<string, string>? NormalizeCrawlProbeFieldErrors(Dictionary<string, string>? serverErrors)
    {
        if (serverErrors is null || serverErrors.Count == 0)
            return serverErrors;

        var mapped = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in serverErrors)
        {
            if (key.EndsWith(".Endpoint", StringComparison.Ordinal) || string.Equals(key, "Endpoint", StringComparison.Ordinal))
            {
                mapped["CrawlConfig.Endpoint"] = value;
                continue;
            }

            if (key.EndsWith(".ApiKey", StringComparison.Ordinal) || string.Equals(key, "ApiKey", StringComparison.Ordinal))
            {
                mapped["CrawlConfig.ApiKey"] = value;
                continue;
            }

            mapped[key] = value;
        }

        return mapped;
    }

    private bool TryGetFieldError(string key, out string message)
        => _fieldErrors.TryGetValue(key, out message!);

    private string GetInputClass(string key)
        => _fieldErrors.ContainsKey(key) ? "settings-input is-invalid" : "settings-input";

    private void ClearFieldErrors()
    {
        _fieldErrors.Clear();
    }

    private void CaptureApiBaseline()
    {
        _baselineApiBaseUrl = _draftApiBaseUrl;
        _baselineApiKey = _draftApiKey;
        _baselineApiAuthEnabled = _draftApiAuthEnabled;
    }

    private void StartAppliedState()
    {
        _applyCompleted = true;
        var version = ++_applyFeedbackVersion;
        _ = HideAppliedStateAsync(version);
    }

    private async Task HideAppliedStateAsync(int version)
    {
        await InvokeAsync(StateHasChanged);

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1.5));
        }
        catch
        {
            return;
        }

        if (version != _applyFeedbackVersion)
            return;

        _applyCompleted = false;
        await InvokeAsync(StateHasChanged);
    }

    private static bool ResearchOptionsEqual(
        ResearchOrchestratorConfigModel left,
        ResearchOrchestratorConfigModel right)
        => left.LimitSearches == right.LimitSearches
            && left.MaxUrlParallelism == right.MaxUrlParallelism
            && left.MaxUrlsPerSerpQuery == right.MaxUrlsPerSerpQuery;

    private static bool LearningOptionsEqual(
        LearningSimilarityOptionsModel left,
        LearningSimilarityOptionsModel right)
        => left.MinImportance == right.MinImportance
            && left.DiversityMaxPerUrl == right.DiversityMaxPerUrl
            && left.DiversityMaxTextSimilarity == right.DiversityMaxTextSimilarity
            && left.MaxLearningsPerSegment == right.MaxLearningsPerSegment
            && left.MinLearningsPerSegment == right.MinLearningsPerSegment
            && left.GroupAssignSimilarityThreshold == right.GroupAssignSimilarityThreshold
            && left.GroupSearchTopK == right.GroupSearchTopK
            && left.MaxEvidenceLength == right.MaxEvidenceLength;

    private static bool ChatConfigEqual(
        RuntimeChatConfigModel left,
        RuntimeChatConfigModel right)
        => string.Equals(left.Endpoint, right.Endpoint, StringComparison.Ordinal)
            && string.Equals(left.ModelId, right.ModelId, StringComparison.Ordinal)
            && left.MaxContextLength == right.MaxContextLength
            && left.HasApiKey == right.HasApiKey;

    private static bool CrawlConfigEqual(
        RuntimeCrawlConfigModel left,
        RuntimeCrawlConfigModel right)
        => string.Equals(left.Endpoint, right.Endpoint, StringComparison.Ordinal)
            && left.HasApiKey == right.HasApiKey;

    private static async Task<(HttpStatusCode? Status, string? Error)> ProbeEndpointDirectAsync(
        string baseUrl,
        string relativePath,
        bool authEnabled,
        string apiKey,
        CancellationToken ct)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(4));

        try
        {
            using var client = new HttpClient
            {
                BaseAddress = new Uri(baseUrl, UriKind.Absolute)
            };

            using var request = CreateAuthorizedRequest(HttpMethod.Get, relativePath, authEnabled, apiKey);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);
            return (response.StatusCode, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (null, "Timed out while contacting the API.");
        }
        catch (HttpRequestException)
        {
            return (null, "Could not reach the API.");
        }
    }

    private static async Task<RuntimeSettingsCallResult> GetRuntimeSettingsDirectAsync(
        string baseUrl,
        bool authEnabled,
        string apiKey,
        CancellationToken ct)
    {
        var normalizedBaseUrl = ApiConnectionSettings.NormalizeBaseUrl(baseUrl);
        if (normalizedBaseUrl is null)
            return new RuntimeSettingsCallResult(null, "Enter a valid absolute API URL that starts with http:// or https://.");

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(8));

        try
        {
            using var client = new HttpClient
            {
                BaseAddress = new Uri(normalizedBaseUrl, UriKind.Absolute)
            };

            using var request = CreateAuthorizedRequest(HttpMethod.Get, "api/settings/runtime", authEnabled, apiKey);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);
            var body = await response.Content.ReadAsStringAsync(linkedCts.Token);

            if (!response.IsSuccessStatusCode)
                return new RuntimeSettingsCallResult(null, BuildRuntimeSettingsErrorMessage(response.StatusCode), ParseValidationErrors(body));

            var data = JsonSerializer.Deserialize<RuntimeSettingsResponseModel>(body, RuntimeSettingsJsonOptions);
            return data is null
                ? new RuntimeSettingsCallResult(null, "The API returned an empty runtime settings payload.")
                : new RuntimeSettingsCallResult(data);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new RuntimeSettingsCallResult(null, "Timed out while loading runtime settings from the API.");
        }
        catch (HttpRequestException)
        {
            return new RuntimeSettingsCallResult(null, "Could not reach the API while loading runtime settings.");
        }
    }

    private static async Task<RuntimeSettingsCallResult> UpdateRuntimeSettingsDirectAsync(
        string baseUrl,
        bool authEnabled,
        string apiKey,
        UpdateRuntimeSettingsRequestModel requestModel,
        CancellationToken ct)
    {
        var normalizedBaseUrl = ApiConnectionSettings.NormalizeBaseUrl(baseUrl);
        if (normalizedBaseUrl is null)
            return new RuntimeSettingsCallResult(null, "Enter a valid absolute API URL that starts with http:// or https://.");

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(12));

        try
        {
            using var client = new HttpClient
            {
                BaseAddress = new Uri(normalizedBaseUrl, UriKind.Absolute)
            };

            var json = JsonSerializer.Serialize(requestModel, RuntimeSettingsJsonOptions);
            using var request = CreateAuthorizedRequest(HttpMethod.Put, "api/settings/runtime", authEnabled, apiKey);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);
            var body = await response.Content.ReadAsStringAsync(linkedCts.Token);

            if (!response.IsSuccessStatusCode)
                return new RuntimeSettingsCallResult(null, BuildRuntimeSettingsErrorMessage(response.StatusCode), ParseValidationErrors(body));

            var data = JsonSerializer.Deserialize<RuntimeSettingsResponseModel>(body, RuntimeSettingsJsonOptions);
            return data is null
                ? new RuntimeSettingsCallResult(null, "The API returned an empty runtime settings payload.")
                : new RuntimeSettingsCallResult(data);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new RuntimeSettingsCallResult(null, "Timed out while saving runtime settings to the API.");
        }
        catch (HttpRequestException)
        {
            return new RuntimeSettingsCallResult(null, "Could not reach the API while saving runtime settings.");
        }
    }

    private static async Task<ChatModelCatalogCallResult> GetChatModelCatalogDirectAsync(
        string baseUrl,
        bool authEnabled,
        string apiKey,
        ChatModelCatalogRequestModel requestModel,
        CancellationToken ct)
    {
        var normalizedBaseUrl = ApiConnectionSettings.NormalizeBaseUrl(baseUrl);
        if (normalizedBaseUrl is null)
            return new ChatModelCatalogCallResult(null, "Enter a valid absolute API URL that starts with http:// or https://.");

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(8));

        try
        {
            using var client = new HttpClient
            {
                BaseAddress = new Uri(normalizedBaseUrl, UriKind.Absolute)
            };

            var json = JsonSerializer.Serialize(requestModel, RuntimeSettingsJsonOptions);
            using var request = CreateAuthorizedRequest(HttpMethod.Post, "api/settings/runtime/chat-models", authEnabled, apiKey);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);
            var body = await response.Content.ReadAsStringAsync(linkedCts.Token);

            if (!response.IsSuccessStatusCode)
                return new ChatModelCatalogCallResult(null, BuildChatModelCatalogErrorMessage(response.StatusCode), ParseValidationErrors(body));

            var data = JsonSerializer.Deserialize<ChatModelCatalogResponseModel>(body, RuntimeSettingsJsonOptions);
            return data is null
                ? new ChatModelCatalogCallResult(null, "The API returned an empty chat model catalog.")
                : new ChatModelCatalogCallResult(data);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ChatModelCatalogCallResult(null, "Timed out while loading chat models.");
        }
        catch (HttpRequestException)
        {
            return new ChatModelCatalogCallResult(null, "Could not reach the API while loading chat models.");
        }
    }

    private static async Task<CrawlApiProbeCallResult> ProbeCrawlApiDirectAsync(
        string baseUrl,
        bool authEnabled,
        string apiKey,
        CrawlApiProbeRequestModel requestModel,
        CancellationToken ct)
    {
        var normalizedBaseUrl = ApiConnectionSettings.NormalizeBaseUrl(baseUrl);
        if (normalizedBaseUrl is null)
            return new CrawlApiProbeCallResult(false, "Enter a valid absolute API URL that starts with http:// or https://.");

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(8));

        try
        {
            using var client = new HttpClient
            {
                BaseAddress = new Uri(normalizedBaseUrl, UriKind.Absolute)
            };

            var json = JsonSerializer.Serialize(requestModel, RuntimeSettingsJsonOptions);
            using var request = CreateAuthorizedRequest(HttpMethod.Post, "api/settings/runtime/crawl-probe", authEnabled, apiKey);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);
            var body = await response.Content.ReadAsStringAsync(linkedCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                return new CrawlApiProbeCallResult(
                    false,
                    BuildCrawlProbeErrorMessage(response.StatusCode),
                    ParseValidationErrors(body));
            }

            return new CrawlApiProbeCallResult(true);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new CrawlApiProbeCallResult(false, "Timed out while testing crawl API.");
        }
        catch (HttpRequestException)
        {
            return new CrawlApiProbeCallResult(false, "Could not reach the API while testing crawl API.");
        }
    }

    private static HttpRequestMessage CreateAuthorizedRequest(
        HttpMethod method,
        string relativePath,
        bool authEnabled,
        string apiKey)
    {
        var request = new HttpRequestMessage(method, relativePath);

        if (authEnabled && !string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

        return request;
    }

    private static string BuildRuntimeSettingsErrorMessage(HttpStatusCode statusCode)
        => statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                => "Authentication failed while accessing runtime settings.",
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity
                => "The API rejected one or more settings values.",
            HttpStatusCode.Conflict
                => "Runtime settings cannot be changed while a research job or synthesis is running.",
            HttpStatusCode.NotFound
                => "This API does not expose runtime settings.",
            _ => $"Runtime settings request failed with HTTP {(int)statusCode}."
        };

    private static string BuildChatModelCatalogErrorMessage(HttpStatusCode statusCode)
        => statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                => "Authentication failed while loading chat models.",
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity
                => "The API rejected the chat backend model lookup request.",
            HttpStatusCode.NotFound
                => "This API does not expose chat model lookup.",
            _ => $"Chat model lookup failed with HTTP {(int)statusCode}."
        };

    private static string BuildCrawlProbeErrorMessage(HttpStatusCode statusCode)
        => statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                => "Authentication failed while testing crawl API.",
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity
                => "The API rejected the crawl API test request.",
            HttpStatusCode.NotFound
                => "This API does not expose crawl API testing.",
            _ => $"Crawl API test failed with HTTP {(int)statusCode}."
        };

    private static Dictionary<string, string>? ParseValidationErrors(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("errors", out var errorsElement)
                || errorsElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in errorsElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Array)
                    continue;

                var message = property.Value.EnumerateArray()
                    .Select(x => x.GetString())
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

                if (!string.IsNullOrWhiteSpace(message))
                    result[property.Name] = message!;
            }

            return result.Count == 0 ? null : result;
        }
        catch
        {
            return null;
        }
    }

    private static ResearchOrchestratorConfigModel CloneResearchOptions(ResearchOrchestratorConfigModel source)
        => new()
        {
            LimitSearches = source.LimitSearches,
            MaxUrlParallelism = source.MaxUrlParallelism,
            MaxUrlsPerSerpQuery = source.MaxUrlsPerSerpQuery
        };

    private static LearningSimilarityOptionsModel CloneLearningOptions(LearningSimilarityOptionsModel source)
        => new()
        {
            MinImportance = source.MinImportance,
            DiversityMaxPerUrl = source.DiversityMaxPerUrl,
            DiversityMaxTextSimilarity = source.DiversityMaxTextSimilarity,
            MaxLearningsPerSegment = source.MaxLearningsPerSegment,
            MinLearningsPerSegment = source.MinLearningsPerSegment,
            GroupAssignSimilarityThreshold = source.GroupAssignSimilarityThreshold,
            GroupSearchTopK = source.GroupSearchTopK,
            MaxEvidenceLength = source.MaxEvidenceLength
        };

    private static RuntimeChatConfigModel CloneRuntimeChatConfig(RuntimeChatConfigModel source)
        => new()
        {
            Endpoint = source.Endpoint,
            ModelId = source.ModelId,
            MaxContextLength = source.MaxContextLength,
            HasApiKey = source.HasApiKey
        };

    private static RuntimeCrawlConfigModel CloneRuntimeCrawlConfig(RuntimeCrawlConfigModel source)
        => new()
        {
            Endpoint = source.Endpoint,
            HasApiKey = source.HasApiKey
        };

    private static UpdateChatConfigModel CloneChatConfigForUpdate(RuntimeChatConfigModel source, string apiKey, bool authEnabled)
        => new()
        {
            Endpoint = source.Endpoint,
            ModelId = source.ModelId,
            MaxContextLength = source.MaxContextLength,
            ApiKey = authEnabled ? apiKey : string.Empty
        };

    private static UpdateCrawlConfigModel CloneCrawlConfigForUpdate(RuntimeCrawlConfigModel source, string apiKey, bool authEnabled)
        => new()
        {
            Endpoint = source.Endpoint,
            ApiKey = authEnabled ? apiKey : string.Empty
        };

    private IReadOnlyList<string> BuildChatModelDropdownOptions()
    {
        var values = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var selectedModelId = (_draftChatConfig.ModelId ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(selectedModelId) && seen.Add(selectedModelId))
            values.Add(selectedModelId);

        foreach (var modelId in _chatModelOptions)
        {
            if (string.IsNullOrWhiteSpace(modelId) || !seen.Add(modelId))
                continue;

            values.Add(modelId);
        }

        return values;
    }

    private async Task<bool> LoadChatModelOptionsAsync(CancellationToken ct)
    {
        if (!_runtimeSettingsLoaded)
            return false;

        var normalizedEndpoint = ApiConnectionSettings.NormalizeBaseUrl(_draftChatConfig.Endpoint);
        if (normalizedEndpoint is null)
        {
            ResetChatModelOptionsState();
            return false;
        }

        _loadingChatModelOptions = true;
        _chatModelOptionsError = null;
        await InvokeAsync(StateHasChanged);

        try
        {
            var result = await GetChatModelCatalogDirectAsync(
                _draftApiBaseUrl,
                _draftApiAuthEnabled,
                _draftApiKey,
                new ChatModelCatalogRequestModel
                {
                    Endpoint = normalizedEndpoint,
                    ApiKey = _draftChatAuthEnabled ? _draftChatApiKey : string.Empty,
                    UseStoredApiKey = _draftChatAuthEnabled && string.IsNullOrWhiteSpace(_draftChatApiKey)
                },
                ct);

            _chatModelOptions.Clear();

            if (result.Data?.ModelIds is { Count: > 0 } modelIds)
            {
                _chatModelOptions.AddRange(modelIds
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.Ordinal));
                _chatModelOptionsError = null;

                if (string.IsNullOrWhiteSpace(_draftChatConfig.ModelId) && _chatModelOptions.Count > 0)
                    _draftChatConfig.ModelId = _chatModelOptions[0];

                return true;
            }

            _chatModelOptionsError = result.FieldErrors?.Values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                ?? result.Error
                ?? "No chat models were returned by the backend.";
            return false;
        }
        finally
        {
            _loadingChatModelOptions = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void ResetChatModelOptionsState()
    {
        _loadingChatModelOptions = false;
        _chatModelOptionsError = null;
        _chatModelOptions.Clear();
    }

    private void ResetAppliedState()
    {
        _applyCompleted = false;
        _applyFeedbackVersion++;
    }

    private static string GetTestBadgeClass(ApiTestStatus status)
        => status switch
        {
            ApiTestStatus.Success => "settings-test-badge is-success",
            ApiTestStatus.Failure => "settings-test-badge is-failure",
            _ => "settings-test-badge"
        };

    private static string GetTestFeedbackClass(ApiTestStatus status)
        => status switch
        {
            ApiTestStatus.Success => "settings-test-feedback is-success",
            ApiTestStatus.Failure => "settings-test-feedback is-failure",
            _ => "settings-test-feedback"
        };

    private static string GetTestBadgeLabel(ApiTestStatus status)
        => status switch
        {
            ApiTestStatus.Success => "Passed",
            ApiTestStatus.Failure => "Failed",
            _ => string.Empty
        };

    private void SetApiConnectionTestFeedback(bool success, string message)
    {
        _apiConnectionTestStatus = success ? ApiTestStatus.Success : ApiTestStatus.Failure;
        _apiConnectionTestMessage = message;
    }

    private void SetChatApiTestFeedback(bool success, string message)
    {
        _chatApiTestStatus = success ? ApiTestStatus.Success : ApiTestStatus.Failure;
        _chatApiTestMessage = message;
    }

    private void SetCrawlApiTestFeedback(bool success, string message)
    {
        _crawlApiTestStatus = success ? ApiTestStatus.Success : ApiTestStatus.Failure;
        _crawlApiTestMessage = message;
    }

    private void ClearApiConnectionTestFeedback()
    {
        _apiConnectionTestStatus = ApiTestStatus.None;
        _apiConnectionTestMessage = null;
    }

    private void ClearChatApiTestFeedback()
    {
        _chatApiTestStatus = ApiTestStatus.None;
        _chatApiTestMessage = null;
    }

    private void ClearCrawlApiTestFeedback()
    {
        _crawlApiTestStatus = ApiTestStatus.None;
        _crawlApiTestMessage = null;
    }

    private void ClearAllApiTestFeedback()
    {
        ClearApiConnectionTestFeedback();
        ClearChatApiTestFeedback();
        ClearCrawlApiTestFeedback();
    }

    private void ResetSettingsMessages()
    {
        _settingsError = null;
        _settingsSuccess = null;
    }
}
