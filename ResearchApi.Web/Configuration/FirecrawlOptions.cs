namespace ResearchApi.Configuration;

public record FirecrawlOptions 
{
    public string BaseUrl { get; init; } = default!;
    public int HttpClientTimeoutSeconds { get; init; } = default!;
};
