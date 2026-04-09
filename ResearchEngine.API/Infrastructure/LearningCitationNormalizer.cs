using System.Text.RegularExpressions;

namespace ResearchEngine.Infrastructure;

public static class LearningCitationNormalizer
{
    private const string GuidPattern =
        @"(?:[0-9a-fA-F]{32}|[0-9a-fA-F]{8}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{12})";

    private static readonly Regex BracketedCitationRegex = new(
        $@"(?:\[|【)\s*lrn:(?<id>{GuidPattern})(?:(?:\|)(?<label>[^\]】]+))?\s*(?:\]|】)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex BareCitationRegex = new(
        $@"(?<![\[【\p{{L}}\p{{N}}_/\-])lrn:(?<id>{GuidPattern})(?![\p{{L}}\p{{N}}_])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static string Normalize(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return markdown ?? string.Empty;

        var normalized = BracketedCitationRegex.Replace(markdown, CanonicalizeMatch);
        normalized = BareCitationRegex.Replace(normalized, match =>
        {
            var s = match.Groups["id"].Value;
            if (!Guid.TryParse(s, out var id))
                return match.Value;

            return $"[lrn:{id:N}]";
        });

        return normalized;
    }

    private static string CanonicalizeMatch(Match match)
    {
        var s = match.Groups["id"].Value;
        if (!Guid.TryParse(s, out var id))
            return match.Value;

        var label = match.Groups["label"].Success
            ? match.Groups["label"].Value.Trim()
            : null;

        if (string.IsNullOrWhiteSpace(label))
            return $"[lrn:{id:N}]";

        return $"[lrn:{id:N}|{label}]";
    }
}
