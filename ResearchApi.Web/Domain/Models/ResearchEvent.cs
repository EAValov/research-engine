namespace ResearchApi.Domain;

public class ResearchEvent
{
    public ResearchEvent()  { }

    public ResearchEvent (DateTimeOffset dt, ResearchEventStage stage, string message )
    {
        Timestamp = dt;
        Stage = stage;
        Message = message;
    }

    public int Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public ResearchEventStage Stage { get; set; }
    public string Message { get; set; } = null!;

    public Guid JobId { get; set; }
    public ResearchJob Job { get; set; } = null!;
}

public enum ResearchEventStage
{
    Created,
    Planning,
    Summarizing,
    Searching,
    LearningExtraction,
    Metrics,
    Completed,
    Failed
}