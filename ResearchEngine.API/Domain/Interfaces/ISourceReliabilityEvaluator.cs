namespace ResearchEngine.Domain;

public interface ISourceReliabilityEvaluator
{
    SourceReliabilityAssessment Evaluate(SearchResult result, string? content = null, SourceKind kind = SourceKind.Web);
    bool ShouldInclude(SourceReliabilityAssessment assessment, SourceDiscoveryMode mode);
}
