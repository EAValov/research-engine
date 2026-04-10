namespace ResearchEngine.API;

public sealed record AddedLearningDto(
    Guid LearningId,
    Guid SourceId,
    Guid LearningGroupId,
    string SourceReference,
    float ImportanceScore,
    DateTimeOffset CreatedAt,
    string Text);
