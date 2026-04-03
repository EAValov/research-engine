using Microsoft.Extensions.Configuration;

namespace ResearchEngine.WebUI.Services;

public sealed class AppRuntimeInfo(IConfiguration configuration)
{
    public string CurrentVersion { get; } = NormalizeVersion(configuration["AppVersion"]) is { Length: > 0 } version
        ? version
        : "dev";

    public bool IsStableRelease => TryParseStableVersion(CurrentVersion, out _);

    private static string NormalizeVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim();
        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[1..];

        return normalized;
    }

    private static bool TryParseStableVersion(string? value, out Version version)
    {
        version = new Version(0, 0);

        var normalized = NormalizeVersion(value);
        var parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
            return false;

        if (!Version.TryParse(normalized, out var parsed) || parsed is null)
            return false;

        version = parsed;
        return true;
    }
}
