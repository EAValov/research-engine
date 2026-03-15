namespace ResearchEngine.API;

public sealed record ChatModelCatalogResponse(
    IReadOnlyList<string> ModelIds
);
