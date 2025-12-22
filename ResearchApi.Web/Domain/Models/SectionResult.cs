namespace ResearchApi.Domain;

public sealed class SectionResult
{
    public required SectionPlan Plan { get; init; }
    public required string Text { get; init; }
    public string? Summary { get; set; }
}
public sealed class SectionPlan
{
    public int Index { get; set; }   
    public string Title { get; set; } = null!;
    public string Description { get; set; } = string.Empty;
    public bool IsConclusion { get; set; }
}