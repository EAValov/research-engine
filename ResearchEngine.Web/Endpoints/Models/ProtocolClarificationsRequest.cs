using System.ComponentModel.DataAnnotations;

namespace ResearchEngine.Web;

public sealed record ProtocolClarificationsRequest(
    [Required] string Query,
    bool IncludeConfigureQuestions = false
);
