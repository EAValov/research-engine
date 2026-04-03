using ResearchEngine.Infrastructure;

namespace ResearchEngine.Domain;

public interface ISourceReliabilityEvaluator
{
    SourceReliabilityAssessment Evaluate(SearchResult result, AppliedSourceTrustPolicy policy, string? content = null, SourceKind kind = SourceKind.Web);
    bool ShouldInclude(SourceReliabilityAssessment assessment, SourceDiscoveryMode mode, SourceSelectionStage stage = SourceSelectionStage.Final);
}
