using System.Text.Json.Serialization;

namespace ResearchEngine.Domain;

public sealed class TokenizeResult
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("max_model_len")]
    public int MaxModelLen { get; set; }

    [JsonPropertyName("tokens")]
    public int[]? Tokens { get; set; }
}