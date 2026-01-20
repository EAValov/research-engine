namespace ResearchEngine.Web;

public sealed record ListLearningsResponse(
    Guid JobId,
    int Skip,
    int Take,
    int Total,
    int Page,
    IReadOnlyList<LearningListItemDto> Learnings);
