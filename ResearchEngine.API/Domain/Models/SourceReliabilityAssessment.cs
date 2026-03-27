namespace ResearchEngine.Domain;

public sealed record SourceReliabilityAssessment(
    string? Domain,
    string? SearchCategory,
    SourceClassification Classification,
    SourceReliabilityTier Tier,
    double Score,
    bool IsPrimarySource,
    string Rationale);
