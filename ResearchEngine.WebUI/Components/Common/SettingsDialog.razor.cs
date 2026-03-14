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
    private sealed record RuntimeSettingsCallResult(
        RuntimeSettingsResponseModel? Data,
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
    private bool _testingSettings;
    private bool _loadingRuntimeSettings;
    private bool _runtimeSettingsLoaded;
    private bool _applyCompleted;
    private int _applyFeedbackVersion;
    private string _draftApiBaseUrl = string.Empty;
    private string _draftBearerToken = string.Empty;
    private bool _draftApiAuthEnabled = true;
    private ResearchOrchestratorConfigModel _draftResearchOptions = new();
    private LearningSimilarityOptionsModel _draftLearningOptions = new();
    private ResearchOrchestratorConfigModel _baselineResearchOptions = new();
    private LearningSimilarityOptionsModel _baselineLearningOptions = new();
    private RuntimeModelInfoModel _runtimeModels = new();
    private readonly Dictionary<string, string> _fieldErrors = new(StringComparer.Ordinal);
    private string? _settingsError;
    private string? _settingsSuccess;
    private string _baselineApiBaseUrl = string.Empty;
    private string _baselineBearerToken = string.Empty;
    private bool _baselineApiAuthEnabled;

    private bool CanTestSettings =>
        ApiConnectionSettings.NormalizeBaseUrl(_draftApiBaseUrl) is not null
        && !_testingSettings
        && !_savingSettings;

    private bool IsRuntimeSettingsDisabled => !_runtimeSettingsLoaded || _loadingRuntimeSettings || _savingSettings;

    private bool HasPendingApiConnectionChange =>
        !string.Equals(
            ApiConnectionSettings.NormalizeBaseUrl(_draftApiBaseUrl),
            ApiConnectionSettings.NormalizeBaseUrl(_baselineApiBaseUrl),
            StringComparison.OrdinalIgnoreCase)
        || !string.Equals(_draftBearerToken, _baselineBearerToken, StringComparison.Ordinal)
        || _draftApiAuthEnabled != _baselineApiAuthEnabled;

    private bool HasRuntimeSettingsChanges =>
        _runtimeSettingsLoaded
        && (!ResearchOptionsEqual(_draftResearchOptions, _baselineResearchOptions)
            || !LearningOptionsEqual(_draftLearningOptions, _baselineLearningOptions));

    private bool HasAnyChanges
        => HasPendingApiConnectionChange || HasRuntimeSettingsChanges;

    private bool CanApplySettings
        => HasAnyChanges && !_savingSettings && !_testingSettings && !_loadingRuntimeSettings;

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
        ClearFieldErrors();
        _settingsOpen = true;
        await LoadRuntimeSettingsForDraftConnectionAsync(showFailureMessage: true);
    }

    private void CloseSettings()
    {
        ResetAppliedState();
        _settingsOpen = false;
        ResetSettingsMessages();
        ClearFieldErrors();
    }

    private void LoadApiDraftFromRuntime()
    {
        _draftApiBaseUrl = ApiConnection.ApiBaseUrl;
        _draftBearerToken = ApiConnection.BearerToken;
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
                LearningSimilarityOptions = CloneLearningOptions(_draftLearningOptions)
            };

            var saveResult = await UpdateRuntimeSettingsDirectAsync(
                _draftApiBaseUrl,
                _draftApiAuthEnabled,
                _draftBearerToken,
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
                ApplyRuntimeSettings(saveResult.Data);

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
        var success = ApiConnection.TryApply(_draftApiBaseUrl, _draftBearerToken, _draftApiAuthEnabled);
        if (!success)
            return false;

        _draftApiBaseUrl = ApiConnection.ApiBaseUrl;
        AppState.SetApiSettings(_draftApiBaseUrl, _draftBearerToken, _draftApiAuthEnabled);
        CaptureApiBaseline();
        return true;
    }

    private void OnApiBaseUrlChanged(ChangeEventArgs e)
    {
        _draftApiBaseUrl = e.Value?.ToString() ?? string.Empty;
        ResetSettingsMessages();
        _fieldErrors.Remove("Api.BaseUrl");
    }

    private void OnBearerTokenChanged(ChangeEventArgs e)
    {
        _draftBearerToken = e.Value?.ToString() ?? string.Empty;
        ResetSettingsMessages();
    }

    private void OnApiAuthChanged(ChangeEventArgs e)
    {
        _draftApiAuthEnabled = e.Value is bool value && value;
        ResetSettingsMessages();
    }

    private async Task TestSettingsAsync()
    {
        ResetSettingsMessages();
        ClearFieldErrors();
        ValidateApiDraft();

        if (_fieldErrors.Count > 0)
        {
            _settingsError = "Review the highlighted settings and try again.";
            return;
        }

        var normalizedBaseUrl = ApiConnectionSettings.NormalizeBaseUrl(_draftApiBaseUrl);
        if (normalizedBaseUrl is null)
        {
            SetFieldError("Api.BaseUrl", "Enter a valid absolute API URL that starts with http:// or https://.");
            _settingsError = "Review the highlighted settings and try again.";
            return;
        }

        _testingSettings = true;
        try
        {
            var probe = await ProbeEndpointDirectAsync(
                normalizedBaseUrl,
                "api/ping",
                _draftApiAuthEnabled,
                _draftBearerToken,
                CancellationToken.None);

            if (probe.Status != HttpStatusCode.OK)
            {
                _settingsError = probe.Status is { } status
                    ? $"API request failed with HTTP {(int)status}."
                    : "API request failed. Check URL, token, CORS, and API availability.";
                return;
            }

            var loaded = await LoadRuntimeSettingsForDraftConnectionAsync(showFailureMessage: true);
            _settingsSuccess = loaded
                ? "API request succeeded and runtime settings loaded."
                : "API request succeeded.";
        }
        finally
        {
            _testingSettings = false;
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
                _draftBearerToken,
                CancellationToken.None);

            if (result.Data is not null)
            {
                ApplyRuntimeSettings(result.Data);
                return true;
            }

            _runtimeSettingsLoaded = false;
            _runtimeModels = new RuntimeModelInfoModel();

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
        _baselineResearchOptions = CloneResearchOptions(response.ResearchOrchestratorConfig);
        _baselineLearningOptions = CloneLearningOptions(response.LearningSimilarityOptions);
        _runtimeModels = new RuntimeModelInfoModel
        {
            ChatModelId = response.Models.ChatModelId ?? string.Empty,
            EmbeddingModelId = response.Models.EmbeddingModelId ?? string.Empty
        };
        _runtimeSettingsLoaded = true;
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
        _baselineBearerToken = _draftBearerToken;
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

    private static async Task<(HttpStatusCode? Status, string? Error)> ProbeEndpointDirectAsync(
        string baseUrl,
        string relativePath,
        bool authEnabled,
        string bearerToken,
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

            using var request = CreateAuthorizedRequest(HttpMethod.Get, relativePath, authEnabled, bearerToken);
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
        string bearerToken,
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

            using var request = CreateAuthorizedRequest(HttpMethod.Get, "api/settings/runtime", authEnabled, bearerToken);
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
        string bearerToken,
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
            using var request = CreateAuthorizedRequest(HttpMethod.Put, "api/settings/runtime", authEnabled, bearerToken);
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

    private static HttpRequestMessage CreateAuthorizedRequest(
        HttpMethod method,
        string relativePath,
        bool authEnabled,
        string bearerToken)
    {
        var request = new HttpRequestMessage(method, relativePath);

        if (authEnabled && !string.IsNullOrWhiteSpace(bearerToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken.Trim());

        return request;
    }

    private static string BuildRuntimeSettingsErrorMessage(HttpStatusCode statusCode)
        => statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                => "Authentication failed while accessing runtime settings.",
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity
                => "The API rejected one or more settings values.",
            HttpStatusCode.NotFound
                => "This API does not expose runtime settings.",
            _ => $"Runtime settings request failed with HTTP {(int)statusCode}."
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

    private static string DisplayModelValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? "Not available" : value;

    private void ResetAppliedState()
    {
        _applyCompleted = false;
        _applyFeedbackVersion++;
    }

    private void ResetSettingsMessages()
    {
        _settingsError = null;
        _settingsSuccess = null;
    }
}
