namespace ResearchEngine.Domain;

public sealed record SourceMetadata(
    string? Domain,
    string? SearchCategory,
    SourceClassification Classification,
    SourceReliabilityTier ReliabilityTier,
    double ReliabilityScore,
    bool IsPrimarySource,
    string ReliabilityRationale);
