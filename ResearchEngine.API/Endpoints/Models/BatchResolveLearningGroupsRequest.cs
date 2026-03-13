using System.ComponentModel.DataAnnotations;
using ResearchEngine.Domain;

namespace ResearchEngine.API;

public sealed record BatchResolveLearningGroupsRequest
{
    [Required]
    [MinLength(1)]
    public required List<Guid> LearningIds { get; init; } = new();
}

public sealed record BatchResolveLearningGroupsResponse(IReadOnlyList<ResolvedLearningGroupDto> Items);
