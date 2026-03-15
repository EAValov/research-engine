using System.ComponentModel.DataAnnotations;
using ResearchEngine.Configuration;
using ResearchEngine.Domain;

namespace ResearchEngine.Infrastructure;

public static class RuntimeSettingsBootstrap
{
    public static RuntimeSettingsSnapshot LoadValidated(IConfiguration configuration)
    {
        var snapshot = new RuntimeSettingsSnapshot(
            GetRequiredSection<ResearchOrchestratorConfig>(configuration, nameof(ResearchOrchestratorConfig)),
            GetRequiredSection<LearningSimilarityOptions>(configuration, nameof(LearningSimilarityOptions)),
            GetRequiredSection<ChatConfig>(configuration, nameof(ChatConfig)));

        ValidateObject(snapshot.ResearchOrchestratorConfig, nameof(ResearchOrchestratorConfig));
        ValidateObject(snapshot.LearningSimilarityOptions, nameof(LearningSimilarityOptions));

        if (snapshot.LearningSimilarityOptions.MinLearningsPerSegment >
            snapshot.LearningSimilarityOptions.MaxLearningsPerSegment)
        {
            throw new InvalidOperationException(
                $"{nameof(LearningSimilarityOptions.MinLearningsPerSegment)} must be less than or equal to {nameof(LearningSimilarityOptions.MaxLearningsPerSegment)}.");
        }

        if (!Uri.TryCreate(snapshot.ChatConfig.Endpoint, UriKind.Absolute, out var endpointUri) ||
            endpointUri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException($"{nameof(ChatConfig.Endpoint)} must be an absolute http:// or https:// URL.");
        }

        if (string.IsNullOrWhiteSpace(snapshot.ChatConfig.ApiKey))
            throw new InvalidOperationException($"{nameof(ChatConfig.ApiKey)} must be configured.");

        if (string.IsNullOrWhiteSpace(snapshot.ChatConfig.ModelId))
            throw new InvalidOperationException($"{nameof(ChatConfig.ModelId)} must be configured.");

        if (snapshot.ChatConfig.MaxContextLength is int maxContextLength &&
            maxContextLength < TokenizerBase.MinimumContextLength)
        {
            throw new InvalidOperationException(
                $"{nameof(ChatConfig.MaxContextLength)} must be at least {TokenizerBase.MinimumContextLength} when provided.");
        }

        return snapshot;
    }

    private static T GetRequiredSection<T>(IConfiguration configuration, string sectionName)
        where T : class
        => configuration.GetSection(sectionName).Get<T>()
           ?? throw new InvalidOperationException($"Missing configuration section: {sectionName}");

    private static void ValidateObject<T>(T model, string name)
    {
        var validationResults = new List<ValidationResult>();
        var context = new ValidationContext(model!);

        if (Validator.TryValidateObject(model!, context, validationResults, validateAllProperties: true))
            return;

        var error = validationResults.FirstOrDefault()?.ErrorMessage ?? "Validation failed.";
        throw new InvalidOperationException($"{name} is invalid: {error}");
    }
}
