using System.Text.RegularExpressions;
using ResearchEngine.Domain;

namespace ResearchEngine.Infrastructure;

public sealed class SourceReliabilityEvaluator : ISourceReliabilityEvaluator
{
    public SourceReliabilityAssessment Evaluate(
        SearchResult result,
        AppliedSourceTrustPolicy policy,
        string? content = null,
        SourceKind kind = SourceKind.Web)
    {
        var reasons = new List<string>();
        var host = NormalizeHost(result.Domain ?? result.Url);
        var uri = TryCreateUri(result.Url);
        var category = NormalizeText(result.SearchCategory);
        var title = result.Title?.Trim() ?? string.Empty;
        var description = result.Description?.Trim() ?? string.Empty;
        var normalizedContent = content ?? string.Empty;

        if (kind == SourceKind.User)
        {
            reasons.Add("User-provided evidence");
            return new SourceReliabilityAssessment(
                Domain: host,
                SearchCategory: category,
                Classification: SourceClassification.UserProvided,
                Tier: SourceReliabilityTier.High,
                Score: 1.0,
                IsPrimarySource: true,
                Rationale: string.Join("; ", reasons));
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            reasons.Add("Could not determine source domain");
            return new SourceReliabilityAssessment(
                Domain: null,
                SearchCategory: category,
                Classification: SourceClassification.Unknown,
                Tier: SourceReliabilityTier.Low,
                Score: 0.30,
                IsPrimarySource: false,
                Rationale: string.Join("; ", reasons));
        }

        foreach (var rule in policy.Rules)
        {
            if (!MatchesRule(rule, host, uri, category, title, description, normalizedContent))
                continue;

            reasons.Add(rule.Reason);
            if (LooksLikePdf(uri))
                reasons.Add("Document-style source");

            return Build(host, category, rule.Classification, rule.Tier, rule.Score, rule.IsPrimarySource, reasons);
        }

        reasons.Add("General web source without strong authority signals");
        return Build(host, category, SourceClassification.Unknown, SourceReliabilityTier.Low, 0.45, false, reasons);
    }

    public bool ShouldInclude(
        SourceReliabilityAssessment assessment,
        SourceDiscoveryMode mode,
        SourceSelectionStage stage = SourceSelectionStage.Final)
        => mode switch
        {
            SourceDiscoveryMode.Auto or SourceDiscoveryMode.Balanced
                => assessment.Tier != SourceReliabilityTier.Blocked,
            SourceDiscoveryMode.ReliableOnly
                => stage == SourceSelectionStage.Candidate
                    ? assessment.Tier is SourceReliabilityTier.Medium or SourceReliabilityTier.High
                    : assessment.Tier == SourceReliabilityTier.High,
            SourceDiscoveryMode.AcademicOnly
                => stage == SourceSelectionStage.Candidate
                    ? assessment.Classification is SourceClassification.Academic
                        or SourceClassification.Journal
                        or SourceClassification.Preprint
                      || string.Equals(assessment.SearchCategory, "research", StringComparison.OrdinalIgnoreCase)
                    : assessment.Classification is SourceClassification.Academic
                        or SourceClassification.Journal
                        or SourceClassification.Preprint,
            _ => assessment.Tier != SourceReliabilityTier.Blocked
        };

    private static SourceReliabilityAssessment Build(
        string? domain,
        string? category,
        SourceClassification classification,
        SourceReliabilityTier tier,
        double score,
        bool isPrimarySource,
        List<string> reasons)
        => new(
            Domain: domain,
            SearchCategory: category,
            Classification: classification,
            Tier: tier,
            Score: score,
            IsPrimarySource: isPrimarySource,
            Rationale: string.Join("; ", reasons));

    private static Uri? TryCreateUri(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;

    private static string? NormalizeHost(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            raw = uri.Host;

        raw = raw.Trim().Trim('/').ToLowerInvariant();
        return raw.StartsWith("www.", StringComparison.Ordinal) ? raw[4..] : raw;
    }

    private static string? NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool MatchesRule(
        SourceTrustRule rule,
        string host,
        Uri? uri,
        string? category,
        string title,
        string description,
        string content)
    {
        var path = uri?.AbsolutePath?.ToLowerInvariant() ?? string.Empty;
        var combined = $"{title} {description}";

        return MatchesHost(rule, host)
            && MatchesAny(path, rule.PathContains, static (value, pattern) => value.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            && MatchesAny(category, rule.SearchCategoryEquals, static (value, pattern) => string.Equals(value, pattern, StringComparison.OrdinalIgnoreCase))
            && MatchesAny(combined, rule.TextContains, static (value, pattern) => value.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            && MatchesAllRegex(content, rule.ContentRegexAll)
            && MatchesAnyRegex(content, rule.ContentRegexAny);
    }

    private static bool MatchesHost(SourceTrustRule rule, string host)
    {
        var hasHostRules = false;
        var hostMatched = false;

        hostMatched |= MatchesConfiguredHostPatterns(
            host,
            rule.HostEquals,
            static (value, pattern) => value.Equals(pattern, StringComparison.OrdinalIgnoreCase),
            ref hasHostRules);
        hostMatched |= MatchesConfiguredHostPatterns(
            host,
            rule.HostEndsWith,
            static (value, pattern) => value.EndsWith(pattern, StringComparison.OrdinalIgnoreCase),
            ref hasHostRules);
        hostMatched |= MatchesConfiguredHostPatterns(
            host,
            rule.HostContains,
            static (value, pattern) => value.Contains(pattern, StringComparison.OrdinalIgnoreCase),
            ref hasHostRules);
        hostMatched |= MatchesConfiguredHostPatterns(
            host,
            rule.HostStartsWith,
            static (value, pattern) => value.StartsWith(pattern, StringComparison.OrdinalIgnoreCase),
            ref hasHostRules);

        return !hasHostRules || hostMatched;
    }

    private static bool LooksLikePdf(Uri? uri)
        => uri?.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) == true;

    private static bool MatchesAny(
        string? value,
        IEnumerable<string>? patterns,
        Func<string, string, bool> matcher)
    {
        if (patterns is null)
            return true;

        var list = patterns.Where(static x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (list.Count == 0)
            return true;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        return list.Any(pattern => matcher(value, pattern));
    }

    private static bool HasValues(IEnumerable<string>? values)
        => values?.Any(static x => !string.IsNullOrWhiteSpace(x)) == true;

    private static bool MatchesConfiguredHostPatterns(
        string host,
        IEnumerable<string>? patterns,
        Func<string, string, bool> matcher,
        ref bool hasHostRules)
    {
        if (!HasValues(patterns))
            return false;

        hasHostRules = true;
        return MatchesAny(host, patterns, matcher);
    }

    private static bool MatchesAnyRegex(string content, IEnumerable<string>? patterns)
    {
        if (patterns is null)
            return true;

        var list = patterns.Where(static x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (list.Count == 0)
            return true;

        if (string.IsNullOrWhiteSpace(content))
            return false;

        return list.Any(pattern => Regex.IsMatch(content, pattern, RegexOptions.IgnoreCase));
    }

    private static bool MatchesAllRegex(string content, IEnumerable<string>? patterns)
    {
        if (patterns is null)
            return true;

        var list = patterns.Where(static x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (list.Count == 0)
            return true;

        if (string.IsNullOrWhiteSpace(content))
            return false;

        return list.All(pattern => Regex.IsMatch(content, pattern, RegexOptions.IgnoreCase));
    }
}
