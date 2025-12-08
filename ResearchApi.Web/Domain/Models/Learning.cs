using Pgvector;

namespace ResearchApi.Domain;

public class Learning
{
    public Guid Id { get; set; }
    public string QueryHash { get; set; } = null!;
    public string Text { get; set; } = null!;
    public string SourceUrl { get; set; } = null!;
    public float? ImportanceScore { get; set; }

    public Vector? Embedding { get; set; }

    public Guid JobId { get; set; }
    public ResearchJob Job { get; set; } = null!;

    public Guid PageId { get; set; }
    public ScrapedPage Page { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
}
