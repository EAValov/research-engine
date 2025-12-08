namespace ResearchApi.Domain;

public class Clarification
{
    public int Id { get; set; }
    public string Question { get; set; } = null!;
    public string Answer { get; set; } = null!;

    public Guid JobId { get; set; }
    public ResearchJob Job { get; set; } = null!;
}
