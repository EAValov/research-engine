using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ResearchEngine.Configuration;
using ResearchEngine.Domain;
using ResearchEngine.Infrastructure;

namespace ResearchEngine.API;

public static partial class ResearchApi
{
    /// <summary>
    /// GET /api/research/settings/runtime
    /// Returns the currently effective runtime settings and active model ids.
    /// </summary>
    private static async Task<IResult> GetRuntimeSettings(
        IRuntimeSettingsRepository runtimeSettingsRepository,
        IOptionsMonitor<EmbeddingConfig> embeddingOptions)
    {
        var settings = await runtimeSettingsRepository.GetCurrentAsync();

        return Results.Ok(new RuntimeSettingsResponse(
            settings.ResearchOrchestratorConfig,
            settings.LearningSimilarityOptions,
            ToRuntimeChatConfigDto(settings.ChatConfig),
            ToRuntimeCrawlConfigDto(settings.CrawlConfig),
            new RuntimeModelInfoDto(
                settings.ChatConfig.ModelId,
                embeddingOptions.CurrentValue.ModelId)));
    }

    /// <summary>
    /// POST /api/research/settings/runtime/chat-models
    /// Loads available chat model ids from the selected chat backend /models endpoint.
    /// </summary>
    private static async Task<IResult> GetRuntimeChatModelsAsync(
        [FromBody] ChatModelCatalogRequest request,
        IHttpClientFactory httpClientFactory,
        IRuntimeSettingsRepository runtimeSettingsRepository,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        ValidateObject(request, nameof(ChatModelCatalogRequest), errors);

        if (!Uri.TryCreate(request.Endpoint, UriKind.Absolute, out var endpointUri) ||
            endpointUri.Scheme is not ("http" or "https"))
        {
            errors[$"{nameof(ChatModelCatalogRequest)}.{nameof(ChatModelCatalogRequest.Endpoint)}"] =
            ["Endpoint must be an absolute http:// or https:// URL."];
        }

        var settings = await runtimeSettingsRepository.GetCurrentAsync(ct);
        var useStoredApiKey = request.UseStoredApiKey && string.IsNullOrWhiteSpace(request.ApiKey);
        var effectiveApiKey = useStoredApiKey
            ? settings.ChatConfig.ApiKey
            : request.ApiKey?.Trim();

        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var modelCatalog = await TryLoadChatModelIdsAsync(
            endpointUri!,
            effectiveApiKey,
            httpClientFactory,
            ct);

        if (modelCatalog.Errors.Count > 0)
            return Results.ValidationProblem(modelCatalog.Errors);

        return Results.Ok(new ChatModelCatalogResponse(modelCatalog.ModelIds));
    }

    /// <summary>
    /// PUT /api/research/settings/runtime
    /// Updates live runtime settings by writing them to appsettings.json and reloading configuration.
    /// </summary>
    private static async Task<IResult> UpdateRuntimeSettingsAsync(
        [FromBody] UpdateRuntimeSettingsRequest request,
        IHttpClientFactory httpClientFactory,
        IRuntimeSettingsRepository runtimeSettingsRepository,
        IDbContextFactory<ResearchDbContext> dbContextFactory,
        IOptionsMonitor<EmbeddingConfig> embeddingOptions,
        CancellationToken ct)
    {
        if (await HasActiveRuntimeWorkAsync(dbContextFactory, ct))
        {
            return Results.Problem(
                title: "Runtime settings update blocked",
                detail: "Runtime settings cannot be changed while a research job or synthesis is running.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateObject(request.ResearchOrchestratorConfig, nameof(request.ResearchOrchestratorConfig), errors);
        ValidateObject(request.LearningSimilarityOptions, nameof(request.LearningSimilarityOptions), errors);
        ValidateObject(request.ChatConfig, nameof(request.ChatConfig), errors);
        ValidateObject(request.CrawlConfig, nameof(request.CrawlConfig), errors);

        if (request.LearningSimilarityOptions.MinLearningsPerSegment >
            request.LearningSimilarityOptions.MaxLearningsPerSegment)
        {
            errors[$"{nameof(request.LearningSimilarityOptions)}.{nameof(LearningSimilarityOptions.MinLearningsPerSegment)}"] =
            [
                $"{nameof(LearningSimilarityOptions.MinLearningsPerSegment)} must be less than or equal to {nameof(LearningSimilarityOptions.MaxLearningsPerSegment)}."
            ];
        }

        var existingSettings = await runtimeSettingsRepository.GetCurrentAsync(ct);
        var existingChatConfig = existingSettings.ChatConfig;
        var effectiveChatApiKey = string.IsNullOrWhiteSpace(request.ChatConfig.ApiKey)
            ? existingChatConfig.ApiKey
            : request.ChatConfig.ApiKey.Trim();
        var existingCrawlConfig = existingSettings.CrawlConfig;
        var effectiveCrawlApiKey = string.IsNullOrWhiteSpace(request.CrawlConfig.ApiKey)
            ? existingCrawlConfig.ApiKey
            : request.CrawlConfig.ApiKey.Trim();

        ValidateChatConfigRequest(request.ChatConfig, errors);
        ValidateCrawlConfigRequest(request.CrawlConfig, errors);

        if (errors.Count == 0)
        {
            var chatValidationErrors = await ValidateChatBackendAsync(
                request.ChatConfig,
                effectiveChatApiKey,
                httpClientFactory,
                ct);

            foreach (var error in chatValidationErrors)
                errors[error.Key] = error.Value;
        }

        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var updatedSettings = await runtimeSettingsRepository.UpdateAsync(
            new RuntimeSettingsSnapshot(
                request.ResearchOrchestratorConfig,
                request.LearningSimilarityOptions,
                new ChatConfig
                {
                    Endpoint = request.ChatConfig.Endpoint.Trim(),
                    ApiKey = effectiveChatApiKey,
                    ModelId = request.ChatConfig.ModelId.Trim(),
                    MaxContextLength = request.ChatConfig.MaxContextLength
                },
                new FirecrawlOptions
                {
                    BaseUrl = request.CrawlConfig.Endpoint.Trim(),
                    ApiKey = effectiveCrawlApiKey,
                    HttpClientTimeoutSeconds = existingCrawlConfig.HttpClientTimeoutSeconds
                }),
            ct);

        return Results.Ok(new RuntimeSettingsResponse(
            updatedSettings.ResearchOrchestratorConfig,
            updatedSettings.LearningSimilarityOptions,
            ToRuntimeChatConfigDto(updatedSettings.ChatConfig),
            ToRuntimeCrawlConfigDto(updatedSettings.CrawlConfig),
            new RuntimeModelInfoDto(
                updatedSettings.ChatConfig.ModelId,
                embeddingOptions.CurrentValue.ModelId)));
    }

    private static RuntimeChatConfigDto ToRuntimeChatConfigDto(ChatConfig config)
        => new(
            config.Endpoint,
            config.ModelId,
            config.MaxContextLength,
            !string.IsNullOrWhiteSpace(config.ApiKey));

    private static RuntimeCrawlConfigDto ToRuntimeCrawlConfigDto(FirecrawlOptions config)
        => new(
            config.BaseUrl,
            !string.IsNullOrWhiteSpace(config.ApiKey));

    private static void ValidateChatConfigRequest(
        UpdateChatConfigRequest request,
        IDictionary<string, string[]> errors)
    {
        if (!Uri.TryCreate(request.Endpoint, UriKind.Absolute, out var endpointUri) ||
            endpointUri.Scheme is not ("http" or "https"))
        {
            errors[$"{nameof(UpdateRuntimeSettingsRequest.ChatConfig)}.{nameof(UpdateChatConfigRequest.Endpoint)}"] =
            ["Endpoint must be an absolute http:// or https:// URL."];
        }

        if (string.IsNullOrWhiteSpace(request.ModelId))
        {
            errors[$"{nameof(UpdateRuntimeSettingsRequest.ChatConfig)}.{nameof(UpdateChatConfigRequest.ModelId)}"] =
            ["Model id is required."];
        }

        if (request.MaxContextLength is int maxContextLength &&
            maxContextLength < TokenizerBase.MinimumContextLength)
        {
            errors[$"{nameof(UpdateRuntimeSettingsRequest.ChatConfig)}.{nameof(UpdateChatConfigRequest.MaxContextLength)}"] =
            [$"MaxContextLength must be at least {TokenizerBase.MinimumContextLength}."];
        }

    }

    private static void ValidateCrawlConfigRequest(
        UpdateCrawlConfigRequest request,
        IDictionary<string, string[]> errors)
    {
        if (!Uri.TryCreate(request.Endpoint, UriKind.Absolute, out var endpointUri) ||
            endpointUri.Scheme is not ("http" or "https"))
        {
            errors[$"{nameof(UpdateRuntimeSettingsRequest.CrawlConfig)}.{nameof(UpdateCrawlConfigRequest.Endpoint)}"] =
            ["Endpoint must be an absolute http:// or https:// URL."];
        }
    }

    private static async Task<Dictionary<string, string[]>> ValidateChatBackendAsync(
        UpdateChatConfigRequest request,
        string? effectiveApiKey,
        IHttpClientFactory httpClientFactory,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var endpointUri = new Uri(request.Endpoint.Trim(), UriKind.Absolute);
        var modelCatalog = await TryLoadChatModelIdsAsync(endpointUri, effectiveApiKey, httpClientFactory, ct);

        if (modelCatalog.Errors.Count > 0)
        {
            foreach (var error in modelCatalog.Errors)
            {
                errors[$"{nameof(UpdateRuntimeSettingsRequest.ChatConfig)}.{nameof(UpdateChatConfigRequest.Endpoint)}"] =
                    error.Value;
            }

            return errors;
        }

        if (!modelCatalog.ModelIds.Contains(request.ModelId.Trim(), StringComparer.Ordinal))
        {
            errors[$"{nameof(UpdateRuntimeSettingsRequest.ChatConfig)}.{nameof(UpdateChatConfigRequest.ModelId)}"] =
            ["The selected model id was not found in the chat backend /models response."];
            return errors;
        }

        if (request.MaxContextLength is not null)
            return errors;

        using var tokenizeRequest = new HttpRequestMessage(
            HttpMethod.Post,
            OpenAiEndpointUri.AppendServerPath(endpointUri, "tokenize"));
        if (!string.IsNullOrWhiteSpace(effectiveApiKey))
            tokenizeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", effectiveApiKey);
        tokenizeRequest.Content = JsonContent.Create(new
        {
            model = request.ModelId.Trim(),
            messages = new object[]
            {
                new { role = "system", content = "ping" },
                new { role = "user", content = "ping" }
            }
        });

        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(8);
            using var tokenizeResponse = await client.SendAsync(tokenizeRequest, ct);
            if (tokenizeResponse.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
            {
                errors[$"{nameof(UpdateRuntimeSettingsRequest.ChatConfig)}.{nameof(UpdateChatConfigRequest.MaxContextLength)}"] =
                ["Provide MaxContextLength or use a chat backend that exposes /tokenize."];
            }
        }
        catch (Exception)
        {
            errors[$"{nameof(UpdateRuntimeSettingsRequest.ChatConfig)}.{nameof(UpdateChatConfigRequest.MaxContextLength)}"] =
            ["Provide MaxContextLength or use a chat backend that exposes /tokenize."];
        }

        return errors;
    }

    private static async Task<ChatModelCatalogFetchResult> TryLoadChatModelIdsAsync(
        Uri endpointUri,
        string? apiKey,
        IHttpClientFactory httpClientFactory,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        using var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(8);

        using var modelsRequest = new HttpRequestMessage(
            HttpMethod.Get,
            OpenAiEndpointUri.AppendV1Path(endpointUri, "models"));
        if (!string.IsNullOrWhiteSpace(apiKey))
            modelsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        HttpResponseMessage modelsResponse;
        string modelsBody;

        try
        {
            modelsResponse = await client.SendAsync(modelsRequest, ct);
            modelsBody = await modelsResponse.Content.ReadAsStringAsync(ct);
        }
        catch (Exception)
        {
            errors["Endpoint"] = ["Could not reach the chat backend /models endpoint."];
            return new ChatModelCatalogFetchResult(Array.Empty<string>(), errors);
        }

        if (!modelsResponse.IsSuccessStatusCode)
        {
            errors["Endpoint"] =
            [$"Chat backend /models request failed with HTTP {(int)modelsResponse.StatusCode} {modelsResponse.StatusCode}."];
            return new ChatModelCatalogFetchResult(Array.Empty<string>(), errors);
        }

        var modelIds = ExtractModelIdsFromModelsPayload(modelsBody);
        if (modelIds.Count == 0)
        {
            errors["Endpoint"] = ["The chat backend /models response did not contain any model ids."];
            return new ChatModelCatalogFetchResult(Array.Empty<string>(), errors);
        }

        return new ChatModelCatalogFetchResult(modelIds, errors);
    }

    /// <summary>
    /// POST /api/research/settings/runtime/crawl-probe
    /// Probes the configured crawl backend using a minimal /v1/search request.
    /// </summary>
    private static async Task<IResult> GetRuntimeCrawlProbeAsync(
        [FromBody] CrawlApiProbeRequest request,
        IHttpClientFactory httpClientFactory,
        IRuntimeSettingsRepository runtimeSettingsRepository,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        ValidateObject(request, nameof(CrawlApiProbeRequest), errors);

        if (!Uri.TryCreate(request.Endpoint, UriKind.Absolute, out var endpointUri) ||
            endpointUri.Scheme is not ("http" or "https"))
        {
            errors[$"{nameof(CrawlApiProbeRequest)}.{nameof(CrawlApiProbeRequest.Endpoint)}"] =
            ["Endpoint must be an absolute http:// or https:// URL."];
        }

        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var settings = await runtimeSettingsRepository.GetCurrentAsync(ct);
        var useStoredApiKey = request.UseStoredApiKey && string.IsNullOrWhiteSpace(request.ApiKey);
        var effectiveApiKey = useStoredApiKey
            ? settings.CrawlConfig.ApiKey
            : request.ApiKey?.Trim();

        var probeErrors = await TryProbeCrawlApiAsync(
            endpointUri!,
            effectiveApiKey,
            httpClientFactory,
            ct);

        if (probeErrors.Count > 0)
            return Results.ValidationProblem(probeErrors);

        return Results.Ok(new { status = "ok" });
    }

    private static async Task<IDictionary<string, string[]>> TryProbeCrawlApiAsync(
        Uri endpointUri,
        string? apiKey,
        IHttpClientFactory httpClientFactory,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        using var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(8);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{endpointUri.ToString().TrimEnd('/')}/v1/search");

        AddApiKeyHeaders(request, apiKey);
        request.Content = JsonContent.Create(new
        {
            query = "research engine health check",
            limit = 1
        });

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (Exception)
        {
            errors["Endpoint"] = ["Could not reach the crawl backend /v1/search endpoint."];
            return errors;
        }

        if (response.IsSuccessStatusCode)
            return errors;

        errors["Endpoint"] =
        [$"Crawl backend /v1/search request failed with HTTP {(int)response.StatusCode} {response.StatusCode}."];

        return errors;
    }

    private static void AddApiKeyHeaders(HttpRequestMessage request, string? apiKey)
    {
        var normalizedApiKey = apiKey?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedApiKey))
            return;

        if (normalizedApiKey.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.TryAddWithoutValidation("Authorization", normalizedApiKey);
        }
        else
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", normalizedApiKey);
        }

        request.Headers.TryAddWithoutValidation("x-api-key", normalizedApiKey);
    }

    private static IReadOnlyList<string> ExtractModelIdsFromModelsPayload(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<string>();

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("data", out var dataElement) ||
                dataElement.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            var modelIds = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var item in dataElement.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out var idElement) ||
                    idElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var modelId = idElement.GetString();
                if (string.IsNullOrWhiteSpace(modelId) || !seen.Add(modelId))
                    continue;

                modelIds.Add(modelId);
            }

            return modelIds;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static async Task<bool> HasActiveRuntimeWorkAsync(
        IDbContextFactory<ResearchDbContext> dbContextFactory,
        CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var hasRunningJobs = await db.ResearchJobs
            .AsNoTracking()
            .AnyAsync(j => j.Status == ResearchJobStatus.Running, ct);

        if (hasRunningJobs)
            return true;

        return await db.Syntheses
            .AsNoTracking()
            .AnyAsync(s => s.Status == SynthesisStatus.Running, ct);
    }

    private static void ValidateObject<T>(
        T model,
        string prefix,
        IDictionary<string, string[]> errors)
    {
        var validationResults = new List<ValidationResult>();
        var context = new ValidationContext(model!);

        if (Validator.TryValidateObject(model!, context, validationResults, validateAllProperties: true))
            return;

        foreach (var result in validationResults)
        {
            var members = result.MemberNames?.Any() == true
                ? result.MemberNames
                : [string.Empty];

            foreach (var memberName in members)
            {
                var key = string.IsNullOrWhiteSpace(memberName)
                    ? prefix
                    : $"{prefix}.{memberName}";

                if (errors.TryGetValue(key, out var existing))
                {
                    errors[key] = existing.Concat([result.ErrorMessage ?? "Validation failed."]).ToArray();
                }
                else
                {
                    errors[key] = [result.ErrorMessage ?? "Validation failed."];
                }
            }
        }
    }

    private sealed record ChatModelCatalogFetchResult(
        IReadOnlyList<string> ModelIds,
        IDictionary<string, string[]> Errors
    );
}
