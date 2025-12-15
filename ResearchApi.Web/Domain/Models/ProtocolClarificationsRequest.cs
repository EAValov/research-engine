using System.ComponentModel.DataAnnotations;

public sealed record ProtocolClarificationsRequest(
    [Required] string Query,
    bool IncludeConfigureQuestions = false
);
