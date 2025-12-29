namespace ResearchEngine.Configuration;

public sealed record EmbeddingConfig
{
    public string Endpoint { get; init; } = default!;
    public string ApiKey  { get; init; } = default!;
    public string ModelId { get; init; } = default!;
    public int Dimension { get; init; }
}