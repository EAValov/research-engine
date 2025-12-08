namespace ResearchApi.Domain;

public class VisitedUrl
{
    public int Id { get; set; }
    public string Url { get; set; } = null!;

    public Guid JobId { get; set; }
    public ResearchJob Job { get; set; } = null!;
}
