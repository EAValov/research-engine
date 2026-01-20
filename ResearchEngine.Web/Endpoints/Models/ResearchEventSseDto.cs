namespace ResearchEngine.Web;

public sealed record ResearchEventSseDto(int Id, DateTimeOffset Timestamp, string Stage, string Message);
