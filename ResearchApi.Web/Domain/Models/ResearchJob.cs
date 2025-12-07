namespace ResearchApi.Domain;

public class ResearchJob
{
    public Guid Id { get; set; }

    public string Query { get; set; } = null!;
    public int Breadth { get; set; }
    public int Depth { get; set; }
    public ResearchJobStatus Status { get; set; }

    public string TargetLanguage { get; set; } = "en";
    public string? Region { get; set; }

    public string? ReportMarkdown { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<Clarification> Clarifications { get; set; } = new List<Clarification>();
    public ICollection<ResearchEvent> Events { get; set; } = new List<ResearchEvent>();
    public ICollection<VisitedUrl> VisitedUrls { get; set; } = new List<VisitedUrl>();
    public ICollection<Learning> Learnings { get; set; } = new List<Learning>();
}
