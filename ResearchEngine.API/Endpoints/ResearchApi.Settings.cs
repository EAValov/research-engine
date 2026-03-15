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
    private static IResult GetRuntimeSettings(
        IOptionsMonitor<ResearchOrchestratorConfig> researchOptions,
        IOptionsMonitor<LearningSimilarityOptions> learningOptions,
        IOptionsMonitor<ChatConfig> chatOptions,
        IOptionsMonitor<EmbeddingConfig> embeddingOptions)
    {
        return Results.Ok(new RuntimeSettingsResponse(
            researchOptions.CurrentValue,
            learningOptions.CurrentValue,
            ToRuntimeChatConfigDto(chatOptions.CurrentValue),
            new RuntimeModelInfoDto(
                chatOptions.CurrentValue.ModelId,
                embeddingOptions.CurrentValue.ModelId)));
    }

    /// <summary>
    /// POST /api/research/settings/runtime/chat-models
    /// Loads available chat model ids from the selected chat backend /models endpoint.
    /// </summary>
    private static async Task<IResult> GetRuntimeChatModelsAsync(
        [FromBody] ChatModelCatalogRequest request,
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<ChatConfig> chatOptions,
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

        var effectiveApiKey = string.IsNullOrWhiteSpace(request.ApiKey)
            ? chatOptions.CurrentValue.ApiKey
            : request.ApiKey.Trim();

        if (string.IsNullOrWhiteSpace(effectiveApiKey))
        {
            errors[$"{nameof(ChatModelCatalogRequest)}.{nameof(ChatModelCatalogRequest.ApiKey)}"] =
            ["API key is required to load model ids."];
        }

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
        AppSettingsJsonWriter settingsWriter,
        IHttpClientFactory httpClientFactory,
        IDbContextFactory<ResearchDbContext> dbContextFactory,
        IOptionsMonitor<ResearchOrchestratorConfig> researchOptions,
        IOptionsMonitor<LearningSimilarityOptions> learningOptions,
        IOptionsMonitor<ChatConfig> chatOptions,
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

        if (request.LearningSimilarityOptions.MinLearningsPerSegment >
            request.LearningSimilarityOptions.MaxLearningsPerSegment)
        {
            errors[$"{nameof(request.LearningSimilarityOptions)}.{nameof(LearningSimilarityOptions.MinLearningsPerSegment)}"] =
            [
                $"{nameof(LearningSimilarityOptions.MinLearningsPerSegment)} must be less than or equal to {nameof(LearningSimilarityOptions.MaxLearningsPerSegment)}."
            ];
        }

        var existingChatConfig = chatOptions.CurrentValue;
        var effectiveChatApiKey = string.IsNullOrWhiteSpace(request.ChatConfig.ApiKey)
            ? existingChatConfig.ApiKey
            : request.ChatConfig.ApiKey.Trim();

        ValidateChatConfigRequest(request.ChatConfig, effectiveChatApiKey, errors);

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

        var updatedChatConfig = new ChatConfig
        {
            Endpoint = request.ChatConfig.Endpoint.Trim(),
            ApiKey = effectiveChatApiKey,
            ModelId = request.ChatConfig.ModelId.Trim(),
            MaxContextLength = request.ChatConfig.MaxContextLength
        };

        await settingsWriter.WriteRuntimeSettingsAsync(
            request.ResearchOrchestratorConfig,
            request.LearningSimilarityOptions,
            updatedChatConfig,
            ct);

        return Results.Ok(new RuntimeSettingsResponse(
            researchOptions.CurrentValue,
            learningOptions.CurrentValue,
            ToRuntimeChatConfigDto(chatOptions.CurrentValue),
            new RuntimeModelInfoDto(
                chatOptions.CurrentValue.ModelId,
                embeddingOptions.CurrentValue.ModelId)));
    }

    private static RuntimeChatConfigDto ToRuntimeChatConfigDto(ChatConfig config)
        => new(
            config.Endpoint,
            config.ModelId,
            config.MaxContextLength,
            !string.IsNullOrWhiteSpace(config.ApiKey));

    private static void ValidateChatConfigRequest(
        UpdateChatConfigRequest request,
        string effectiveApiKey,
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

        if (string.IsNullOrWhiteSpace(effectiveApiKey))
        {
            errors[$"{nameof(UpdateRuntimeSettingsRequest.ChatConfig)}.{nameof(UpdateChatConfigRequest.ApiKey)}"] =
            ["API key is required. Enter a new key or keep an existing configured key."];
        }
    }

    private static async Task<Dictionary<string, string[]>> ValidateChatBackendAsync(
        UpdateChatConfigRequest request,
        string effectiveApiKey,
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

        using var tokenizeRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(endpointUri, "tokenize"));
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
        string apiKey,
        IHttpClientFactory httpClientFactory,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        using var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(8);

        using var modelsRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(endpointUri, "models"));
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
