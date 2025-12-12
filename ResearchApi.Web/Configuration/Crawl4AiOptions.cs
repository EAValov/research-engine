public sealed class Crawl4AiOptions
{
    public string BaseUrl { get; set; } = "http://crawl4ai:11235";
    public string? ApiToken { get; set; }

    public long? HttpClientTimeoutSeconds {get; set;}
}