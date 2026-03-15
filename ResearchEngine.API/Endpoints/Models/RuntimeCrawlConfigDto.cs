namespace ResearchEngine.API;

public sealed record RuntimeCrawlConfigDto(
    string Endpoint,
    bool HasApiKey
);
