namespace ResearchEngine.Domain;

public enum LearningStatementType
{
    Unknown = 0,
    Finding = 1,
    Requirement = 2,
    Forecast = 3,
    Claim = 4,
    Commentary = 5,
    Contested = 6
}

public static class LearningStatementTypeExtensions
{
    public static bool TryParse(string? value, out LearningStatementType type)
    {
        type = LearningStatementType.Unknown;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.Trim().ToLowerInvariant() switch
        {
            "unknown" => Set(LearningStatementType.Unknown, out type),
            "finding" or "fact" or "observation" => Set(LearningStatementType.Finding, out type),
            "requirement" or "obligation" or "rule" => Set(LearningStatementType.Requirement, out type),
            "forecast" or "projection" or "scenario" => Set(LearningStatementType.Forecast, out type),
            "claim" or "attributedclaim" or "vendorclaim" => Set(LearningStatementType.Claim, out type),
            "commentary" or "analysis" or "opinion" => Set(LearningStatementType.Commentary, out type),
            "contested" or "disputed" or "conflicting" => Set(LearningStatementType.Contested, out type),
            _ => false
        };
    }

    public static string ToApiValue(this LearningStatementType value)
        => value switch
        {
            LearningStatementType.Unknown => "Unknown",
            LearningStatementType.Finding => "Finding",
            LearningStatementType.Requirement => "Requirement",
            LearningStatementType.Forecast => "Forecast",
            LearningStatementType.Claim => "Claim",
            LearningStatementType.Commentary => "Commentary",
            LearningStatementType.Contested => "Contested",
            _ => value.ToString()
        };

    private static bool Set(LearningStatementType value, out LearningStatementType type)
    {
        type = value;
        return true;
    }
}
