namespace ResearchApi.Domain;

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
    public DateTimeOffset Timestamp { get; set; }
    public string Stage { get; set; } = null!;
    public string Message { get; set; } = null!;

    public Guid JobId { get; set; }
    public ResearchJob Job { get; set; } = null!;
}
