using System.ComponentModel.DataAnnotations;

namespace ResearchEngine.API;

public sealed record CreateResearchJobRequest(
    [Required]
    [MinLength(1)]
    [MaxLength(4000)]
    string Query,
    
    IReadOnlyList<ClarificationDto>? Clarifications,
    
    [Range(1, 8)]
    int? Breadth,
    
    [Range(1, 4)]
    int? Depth,
    
    [MinLength(2)]
    [MaxLength(2)]
    string? Language,
    
    [MaxLength(100)]
    string? Region,

    [MaxLength(32)]
    string? DiscoveryMode
);
