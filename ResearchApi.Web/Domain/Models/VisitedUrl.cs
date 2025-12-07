namespace ResearchApi.Domain;

public class VisitedUrl
{
    public int Id { get; set; }

    public Guid JobId { get; set; }
    public ResearchJob Job { get; set; } = null!;

    public string Url { get; set; } = null!;
}
