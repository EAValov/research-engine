namespace ResearchEngine.Domain;

public class ScrapedPage
{
    public Guid Id { get; set; }
    public string Url { get; set; } = null!;
    public string? Language { get; set; }
    public string? Region { get; set; }
    public string Content { get; set; } = null!;
    public string ContentHash { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<Learning> Learnings { get; set; } = new List<Learning>();
}
