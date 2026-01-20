namespace ResearchEngine.Web;

public sealed record LearningListItemDto(
    Guid LearningId,
    Guid SourceId,
    string SourceReference,
    float ImportanceScore,
    DateTimeOffset CreatedAt,
    string Text);
