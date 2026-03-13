namespace ResearchEngine.Domain;

public interface IResearchSynthesisOverridesRepository
{
    Task AddOrUpdateSynthesisSourceOverridesAsync(
        Guid synthesisId,
        IEnumerable<SynthesisSourceOverrideDto> overrides,
        CancellationToken ct = default);

    Task AddOrUpdateSynthesisLearningOverridesAsync(
        Guid synthesisId,
        IEnumerable<SynthesisLearningOverrideDto> overrides,
        CancellationToken ct = default);

    Task<SynthesisOverridesSnapshot> GetSynthesisOverridesAsync(
        Guid synthesisId,
        CancellationToken ct = default);
}
