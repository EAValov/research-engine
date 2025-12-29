namespace ResearchEngine.Domain;

public sealed class Source
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public ResearchJob Job { get; set; } = null!;

    // Source identity
    public string Url { get; set; } = null!;
    public string ContentHash { get; set; } = null!; 
    public string? Title { get; set; }
    public string Content { get; set; } = null!;

    // Metadata (keep for filtering/UX)
    public string? Language { get; set; }
    public string? Region { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<Learning> Learnings { get; set; } = new List<Learning>();
}

public sealed record SourceListItemDto(
    Guid SourceId,
    string Url,
    string? Title,
    string? Language,
    string? Region,
    DateTimeOffset CreatedAt,
    int LearningCount);
