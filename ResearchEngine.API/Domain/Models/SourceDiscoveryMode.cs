namespace ResearchEngine.Domain;

public enum SourceDiscoveryMode
{
    Balanced = 0,
    ReliableOnly = 1,
    AcademicOnly = 2
}

public static class SourceDiscoveryModeExtensions
{
    public static SourceDiscoveryMode ParseOrDefault(string? value, SourceDiscoveryMode fallback = SourceDiscoveryMode.Balanced)
        => TryParse(value, out var mode) ? mode : fallback;

    public static bool TryParse(string? value, out SourceDiscoveryMode mode)
    {
        mode = SourceDiscoveryMode.Balanced;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.Trim().ToLowerInvariant() switch
        {
            "balanced" => Set(SourceDiscoveryMode.Balanced, out mode),
            "reliableonly" or "reliable_only" or "reliable-only" => Set(SourceDiscoveryMode.ReliableOnly, out mode),
            "academiconly" or "academic_only" or "academic-only" => Set(SourceDiscoveryMode.AcademicOnly, out mode),
            _ => false
        };
    }

    public static string ToApiValue(this SourceDiscoveryMode mode)
        => mode switch
        {
            SourceDiscoveryMode.Balanced => "Balanced",
            SourceDiscoveryMode.ReliableOnly => "ReliableOnly",
            SourceDiscoveryMode.AcademicOnly => "AcademicOnly",
            _ => mode.ToString()
        };

    private static bool Set(SourceDiscoveryMode value, out SourceDiscoveryMode mode)
    {
        mode = value;
        return true;
    }
}

public enum SourceClassification
{
    Unknown = 0,
    Official = 1,
    Government = 2,
    Academic = 3,
    Journal = 4,
    Preprint = 5,
    News = 6,
    Reference = 7,
    Blog = 8,
    Forum = 9,
    Social = 10,
    UserProvided = 11
}

public static class SourceClassificationExtensions
{
    public static string ToApiValue(this SourceClassification value)
        => value switch
        {
            SourceClassification.Unknown => "Unknown",
            SourceClassification.Official => "Official",
            SourceClassification.Government => "Government",
            SourceClassification.Academic => "Academic",
            SourceClassification.Journal => "Journal",
            SourceClassification.Preprint => "Preprint",
            SourceClassification.News => "News",
            SourceClassification.Reference => "Reference",
            SourceClassification.Blog => "Blog",
            SourceClassification.Forum => "Forum",
            SourceClassification.Social => "Social",
            SourceClassification.UserProvided => "UserProvided",
            _ => value.ToString()
        };
}

public enum SourceReliabilityTier
{
    Blocked = 0,
    Low = 1,
    Medium = 2,
    High = 3
}

public static class SourceReliabilityTierExtensions
{
    public static string ToApiValue(this SourceReliabilityTier value)
        => value switch
        {
            SourceReliabilityTier.Blocked => "Blocked",
            SourceReliabilityTier.Low => "Low",
            SourceReliabilityTier.Medium => "Medium",
            SourceReliabilityTier.High => "High",
            _ => value.ToString()
        };
}
