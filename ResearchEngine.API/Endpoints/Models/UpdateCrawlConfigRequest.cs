using System.ComponentModel.DataAnnotations;

namespace ResearchEngine.API;

public sealed record UpdateCrawlConfigRequest(
    [Required(AllowEmptyStrings = false)] string Endpoint,
    string? ApiKey
);
