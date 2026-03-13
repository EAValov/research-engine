namespace ResearchEngine.API;

public sealed record ResearchEventSseDto(int Id, DateTimeOffset Timestamp, string Stage, string Message);
