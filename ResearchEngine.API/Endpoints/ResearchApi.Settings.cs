using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ResearchEngine.Configuration;
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
            new RuntimeModelInfoDto(
                chatOptions.CurrentValue.ModelId,
                embeddingOptions.CurrentValue.ModelId)));
    }

    /// <summary>
    /// PUT /api/research/settings/runtime
    /// Updates live runtime settings by writing them to appsettings.json and reloading configuration.
    /// </summary>
    private static async Task<IResult> UpdateRuntimeSettingsAsync(
        [FromBody] UpdateRuntimeSettingsRequest request,
        AppSettingsJsonWriter settingsWriter,
        IOptionsMonitor<ResearchOrchestratorConfig> researchOptions,
        IOptionsMonitor<LearningSimilarityOptions> learningOptions,
        IOptionsMonitor<ChatConfig> chatOptions,
        IOptionsMonitor<EmbeddingConfig> embeddingOptions,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateObject(request.ResearchOrchestratorConfig, nameof(request.ResearchOrchestratorConfig), errors);
        ValidateObject(request.LearningSimilarityOptions, nameof(request.LearningSimilarityOptions), errors);

        if (request.LearningSimilarityOptions.MinLearningsPerSegment >
            request.LearningSimilarityOptions.MaxLearningsPerSegment)
        {
            errors[$"{nameof(request.LearningSimilarityOptions)}.{nameof(LearningSimilarityOptions.MinLearningsPerSegment)}"] =
            [
                $"{nameof(LearningSimilarityOptions.MinLearningsPerSegment)} must be less than or equal to {nameof(LearningSimilarityOptions.MaxLearningsPerSegment)}."
            ];
        }

        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        await settingsWriter.WriteRuntimeSettingsAsync(
            request.ResearchOrchestratorConfig,
            request.LearningSimilarityOptions,
            ct);

        return Results.Ok(new RuntimeSettingsResponse(
            researchOptions.CurrentValue,
            learningOptions.CurrentValue,
            new RuntimeModelInfoDto(
                chatOptions.CurrentValue.ModelId,
                embeddingOptions.CurrentValue.ModelId)));
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
}
