namespace ResearchEngine.Configuration;

public sealed record ReleaseCheckOptions
{
    public bool Enabled { get; init; } = true;
    public string RepositoryOwner { get; init; } = "EAValov";
    public string RepositoryName { get; init; } = "research-engine";
    public int SuccessCacheMinutes { get; init; } = 360;
    public int FailureCacheMinutes { get; init; } = 30;
}
