namespace ResearchEngine.API;

public sealed record RuntimeChatConfigDto(
    string Endpoint,
    string ModelId,
    int? MaxContextLength,
    bool HasApiKey
);
