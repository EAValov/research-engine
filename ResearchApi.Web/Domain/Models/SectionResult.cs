namespace ResearchApi.Domain;

public sealed class SectionResult
{
    public required SectionPlan Plan { get; init; }
    public required string Text { get; init; }
    public string? Summary { get; set; }
}
public sealed class SectionPlan
{
    public string Title { get; set; } = null!;
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Можно помечать, что эта секция — заключение (опционально).
    /// Пока будем считать, что последняя секция — это conclusion.
    /// </summary>
    public bool IsConclusion { get; set; }
}


