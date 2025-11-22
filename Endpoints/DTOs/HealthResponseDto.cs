namespace ResearchApi.Endpoints.DTOs;

public record DependencyHealth(
    bool IsHealthy,
    string? Message = null
);

public record ResearchHealthResponse(
    string Status,
    DependencyHealth Llm,
    DependencyHealth Firecrawl
);