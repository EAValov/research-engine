namespace ResearchEngine.Domain;

public sealed record SearchRequest(
    string Query,
    int Limit,
    string? Location,
    SourceDiscoveryMode DiscoveryMode);
