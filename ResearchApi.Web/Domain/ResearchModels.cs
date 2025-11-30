using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Pgvector;

namespace ResearchApi.Domain;

public enum ResearchJobStatus { Pending, Running, Completed, Failed }

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

public class Clarification
{
    public int Id { get; set; }

    public Guid JobId { get; set; }
    public ResearchJob Job { get; set; } = null!;

    public string Question { get; set; } = null!;
    public string Answer { get; set; } = null!;
}

public class ResearchEvent
{
    public ResearchEvent()  { }

    public ResearchEvent (DateTimeOffset dt, string stage, string message )
    {
        Timestamp = dt;
        Stage = stage;
        Message = message;
    }

    public int Id { get; set; }

    public Guid JobId { get; set; }
    public ResearchJob Job { get; set; } = null!;

    public DateTimeOffset Timestamp { get; set; }
    public string Stage { get; set; } = null!;
    public string Message { get; set; } = null!;
}

public class VisitedUrl
{
    public int Id { get; set; }

    public Guid JobId { get; set; }
    public ResearchJob Job { get; set; } = null!;

    public string Url { get; set; } = null!;
}

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

public class Learning
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public Guid PageId { get; set; }
    public string QueryHash { get; set; } = null!;
    public string Text { get; set; } = null!;
    public string SourceUrl { get; set; } = null!;

    public Vector? Embedding { get; set; }
    public ResearchJob Job { get; set; } = null!;
    public ScrapedPage Page { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
}
