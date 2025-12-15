using System.ComponentModel.DataAnnotations;

public sealed record ProtocolParametersRequest(
    [Required] string Query,
    IReadOnlyList<ClarificationDto>? Clarifications,
    Dictionary<string, object>? Overrides
);
