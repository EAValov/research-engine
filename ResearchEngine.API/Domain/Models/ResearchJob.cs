namespace ResearchEngine.Domain;

public enum ResearchJobStatus { Pending, Running, Completed, Failed, Canceled }

public sealed class ResearchJob
{
    public Guid Id { get; set; }

    public string Query { get; set; } = null!;
    public string ChatModelName { get; set; } = null!;
    public string EmbeddingModelName { get; set; } = null!;
    public int Breadth { get; set; }
    public int Depth { get; set; }
    public ResearchJobStatus Status { get; set; }

    public string TargetLanguage { get; set; } = "en";
    public string? Region { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public string? HangfireJobId { get; set; }
    public bool CancelRequested { get; set; }
    public DateTimeOffset? CancelRequestedAt { get; set; }
    public string? CancelReason { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedReason { get; set; }

    public ICollection<Clarification> Clarifications { get; set; } = new List<Clarification>();
    public ICollection<ResearchEvent> Events { get; set; } = new List<ResearchEvent>();
    public ICollection<Source> Sources { get; set; } = new List<Source>();
    public ICollection<Synthesis> Syntheses { get; set; } = new List<Synthesis>();
}


