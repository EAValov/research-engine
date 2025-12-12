public sealed class SearxngOptions
{
    public string BaseUrl { get; set; } = "http://searxng:8085";
    public string? DefaultLanguage { get; set; } = "en";
    public long? HttpClientTimeoutSeconds {get; set;}
}