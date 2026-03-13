namespace ResearchEngine.Configuration;

public sealed record FirecrawlOptions 
{
    public string BaseUrl { get; init; } = default!;
    public string? ApiKey { get; init; }
    public int HttpClientTimeoutSeconds { get; init; } = default!;
};
