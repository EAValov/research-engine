using System.ComponentModel.DataAnnotations;
using ResearchEngine.Configuration;

namespace ResearchEngine.API;

public sealed record UpdateRuntimeSettingsRequest(
    [Required] ResearchOrchestratorConfig ResearchOrchestratorConfig,
    [Required] LearningSimilarityOptions LearningSimilarityOptions,
    [Required] UpdateChatConfigRequest ChatConfig
);
