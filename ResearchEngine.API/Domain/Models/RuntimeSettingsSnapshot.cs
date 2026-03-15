using ResearchEngine.Configuration;

namespace ResearchEngine.Domain;

public sealed record RuntimeSettingsSnapshot(
    ResearchOrchestratorConfig ResearchOrchestratorConfig,
    LearningSimilarityOptions LearningSimilarityOptions,
    ChatConfig ChatConfig,
    FirecrawlOptions CrawlConfig
);
