using System.ComponentModel.DataAnnotations;

namespace ResearchEngine.API;

public sealed record ChatModelCatalogRequest(
    [Required(AllowEmptyStrings = false)] string Endpoint,
    string? ApiKey
);
