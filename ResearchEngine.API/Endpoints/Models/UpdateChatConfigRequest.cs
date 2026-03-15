using System.ComponentModel.DataAnnotations;

namespace ResearchEngine.API;

public sealed record UpdateChatConfigRequest(
    [Required(AllowEmptyStrings = false)] string Endpoint,
    [Required(AllowEmptyStrings = false)] string ModelId,
    string? ApiKey,
    int? MaxContextLength
);
