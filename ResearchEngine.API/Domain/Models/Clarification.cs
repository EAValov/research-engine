namespace ResearchEngine.Domain;

public sealed class Clarification
{
    public Guid Id { get; set; }

    public Guid JobId { get; set; }
    public ResearchJob Job { get; set; } = null!;

    public string Question { get; set; } = null!;
    public string Answer { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
}
