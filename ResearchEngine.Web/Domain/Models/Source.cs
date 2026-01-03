namespace ResearchEngine.Domain;

public enum SourceKind
{
    Web = 0,
    User = 1
}

public sealed class Source
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public ResearchJob Job { get; set; } = null!;

    /// <summary>
    /// A reference string. For web sources it's the URL; for user sources it can be a citation or note.
    /// Examples:
    /// - https://example.com/paper
    /// - "Smith 2020, Chapter 3, pp. 15-18"
    /// - "Lab notebook entry 2025-12-29"
    /// </summary>
    public string Reference { get; set; } = null!;
    public string ContentHash { get; set; } = null!; 
    public string? Title { get; set; }
    public string Content { get; set; } = null!;

    public SourceKind Kind { get; set; } = SourceKind.Web;

    public string? Language { get; set; }
    public string? Region { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<Learning> Learnings { get; set; } = new List<Learning>();
}

public sealed record SourceListItemDto(
    Guid SourceId,
    string Reference,
    string? Title,
    string? Language,
    string? Region,
    DateTimeOffset CreatedAt,
    int LearningCount);
