using System.ComponentModel.DataAnnotations;

namespace ResearchEngine.API;

public sealed record CrawlApiProbeRequest(
    [Required(AllowEmptyStrings = false)] string Endpoint,
    string? ApiKey,
    bool UseStoredApiKey = true
);
