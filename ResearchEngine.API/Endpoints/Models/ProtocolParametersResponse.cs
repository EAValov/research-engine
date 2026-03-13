namespace ResearchEngine.API;

public sealed record ProtocolParametersResponse(
    int Breadth,
    int Depth,
    string Language,
    string? Region);