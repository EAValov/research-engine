public sealed class SearxNgOptions
{
    /// <summary>
    /// Base URL of your SearxNG instance, e.g. "https://searxng.example.com"
    /// (no trailing slash needed; the client will append /search).
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Default language code, e.g. "en", "ja", "de".
    /// Used when no explicit language is passed to SearchAsync.
    /// </summary>
    public string? DefaultLanguage { get; set; }

    /// <summary>
    /// Optional default region/locale hint, e.g. "US", "JP", "DE".
    /// Combined with language into something like "en_US" for the "locale" param.
    /// </summary>
    public string? DefaultRegion { get; set; }

    /// <summary>
    /// Categories to search, e.g. "general", "news", "it".
    /// See SearxNG docs; you can leave null to use server defaults.
    /// </summary>
    public string? Categories { get; set; } = "general";

    /// <summary>
    /// SearxNG safesearch level (0=off, 1=moderate, 2=strict).
    /// </summary>
    public int SafeSearch { get; set; } = 1;
}