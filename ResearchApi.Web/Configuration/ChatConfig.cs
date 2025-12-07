namespace ResearchApi.Configuration;

public record ChatConfig
{
    public string Endpoint { get; init; } = default!;
    public string ApiKey  { get; init; } = default!;
    public string ModelId { get; init; } = default!;
}

