using System.ComponentModel.DataAnnotations;

namespace ResearchEngine.API;

public sealed record ProtocolParametersRequest(
    [Required] string Query,
    IReadOnlyList<ClarificationDto>? Clarifications,
    Dictionary<string, object>? Overrides
);
