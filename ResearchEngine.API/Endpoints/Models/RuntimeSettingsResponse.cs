using ResearchEngine.Configuration;

namespace ResearchEngine.API;

public sealed record RuntimeSettingsResponse(
    ResearchOrchestratorConfig ResearchOrchestratorConfig,
    LearningSimilarityOptions LearningSimilarityOptions,
    RuntimeModelInfoDto Models
);
