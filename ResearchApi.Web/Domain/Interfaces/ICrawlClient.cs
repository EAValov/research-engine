namespace ResearchApi.Domain;

public interface ICrawlClient
{
    Task<string> FetchContentAsync(string url, CancellationToken ct = default);
}
