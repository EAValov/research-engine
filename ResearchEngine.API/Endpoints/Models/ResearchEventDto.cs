namespace ResearchEngine.API;

public sealed record ResearchEventDto(int Id, DateTimeOffset Timestamp, string Stage, string Message);
