namespace ResearchEngine.API;

public sealed record ListLearningsResponse(
    Guid JobId,
    int Skip,
    int Take,
    int Total,
    int Page,
    IReadOnlyList<LearningListItemDto> Learnings);
