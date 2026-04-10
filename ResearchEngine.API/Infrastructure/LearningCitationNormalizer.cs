using System.Text.RegularExpressions;

namespace ResearchEngine.Infrastructure;

public static class LearningCitationNormalizer
{
    private const string LooseIdPattern = @"[0-9a-fA-F\-]{28,36}";

    private static readonly Regex BracketedCitationRegex = new(
        $@"(?:\[|【)\s*lrn:(?<id>{LooseIdPattern})(?:(?:\|)(?<label>[^\]】]+))?\s*(?:\]|】)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex BareCitationRegex = new(
        $@"(?<![\[【\p{{L}}\p{{N}}_/\-])lrn:(?<id>{LooseIdPattern})(?![\p{{L}}\p{{N}}_])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static string Normalize(string markdown, IReadOnlyCollection<Guid>? knownLearningIds = null)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return markdown ?? string.Empty;

        var candidates = knownLearningIds?
            .Select(id => id.ToString("N"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? Array.Empty<string>();

        var normalized = BracketedCitationRegex.Replace(markdown, match => CanonicalizeMatch(match, candidates));
        normalized = BareCitationRegex.Replace(normalized, match => CanonicalizeBareMatch(match, candidates));

        return normalized;
    }

    private static string CanonicalizeMatch(Match match, IReadOnlyList<string> candidates)
    {
        var s = match.Groups["id"].Value;
        if (!TryResolveCanonicalId(s, candidates, out var canonicalId))
            return match.Value;

        var label = match.Groups["label"].Success
            ? match.Groups["label"].Value.Trim()
            : null;

        if (string.IsNullOrWhiteSpace(label))
            return $"[lrn:{canonicalId}]";

        return $"[lrn:{canonicalId}|{label}]";
    }

    private static string CanonicalizeBareMatch(Match match, IReadOnlyList<string> candidates)
    {
        var s = match.Groups["id"].Value;
        if (!TryResolveCanonicalId(s, candidates, out var canonicalId))
            return match.Value;

        return $"[lrn:{canonicalId}]";
    }

    private static bool TryResolveCanonicalId(
        string rawId,
        IReadOnlyList<string> candidates,
        out string canonicalId)
    {
        canonicalId = string.Empty;

        if (Guid.TryParse(rawId, out var id))
        {
            canonicalId = id.ToString("N");
            return true;
        }

        var compact = CompactHex(rawId);
        if (compact.Length is < 28 or > 32)
            return false;

        if (candidates.Count == 0)
            return false;

        var ranked = candidates
            .Select(candidate => new CandidateScore(candidate, LevenshteinDistance(compact, candidate)))
            .Where(x => x.Distance <= 2)
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.Candidate, StringComparer.Ordinal)
            .ToList();

        if (ranked.Count == 0)
            return false;

        if (ranked.Count > 1 && ranked[0].Distance == ranked[1].Distance)
            return false;

        canonicalId = ranked[0].Candidate;
        return true;
    }

    private static string CompactHex(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return new string(value
            .Where(static c => Uri.IsHexDigit(c))
            .Select(static c => char.ToLowerInvariant(c))
            .ToArray());
    }

    private static int LevenshteinDistance(string left, string right)
    {
        if (left.Length == 0)
            return right.Length;
        if (right.Length == 0)
            return left.Length;

        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var j = 0; j <= right.Length; j++)
            previous[j] = j;

        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private readonly record struct CandidateScore(string Candidate, int Distance);
}
