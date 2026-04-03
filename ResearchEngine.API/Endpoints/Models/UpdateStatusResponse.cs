namespace ResearchEngine.API;

public sealed record UpdateStatusResponse(
    bool CheckEnabled,
    string CurrentVersion,
    string? LatestVersion,
    bool UpdateAvailable,
    string? ReleaseUrl,
    string? ReleaseTitle,
    DateTimeOffset? PublishedAtUtc
);
