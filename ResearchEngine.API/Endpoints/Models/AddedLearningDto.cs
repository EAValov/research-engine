namespace ResearchEngine.API;

public sealed record AddedLearningDto(
    Guid LearningId,
    Guid SourceId,
    Guid LearningGroupId,
    float ImportanceScore,
    DateTimeOffset CreatedAt,
    string Text);
