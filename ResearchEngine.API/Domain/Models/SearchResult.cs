namespace ResearchEngine.Domain;

public sealed record SearchResult(
    string Url,
    string Title,
    string Description,
    string? Domain = null,
    string? SearchCategory = null,
    int? Position = null,
    string? PublishedDate = null);
