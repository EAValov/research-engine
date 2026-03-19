using System.ComponentModel.DataAnnotations;

namespace ResearchEngine.API;

public sealed record ProtocolClarificationsRequest(
    [Required] string Query
);
