using System.Text.Json.Serialization;


namespace ResearchApi.Configuration;

public sealed record TypedConfig<T>
{
    /// <summary>
    /// Name of the configuration type, e.g. "BrowserConfig" or "CrawlerRunConfig".
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Configuration parameters object.
    /// </summary>
    [JsonPropertyName("params")]
    public T Params { get; set; } = default!;
}
