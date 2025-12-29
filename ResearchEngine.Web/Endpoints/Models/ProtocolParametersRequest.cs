using System.ComponentModel.DataAnnotations;

namespace ResearchEngine.Web;

public sealed record ProtocolParametersRequest(
    [Required] string Query,
    IReadOnlyList<ClarificationDto>? Clarifications,
    Dictionary<string, object>? Overrides
);
