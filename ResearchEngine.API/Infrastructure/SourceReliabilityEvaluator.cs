using System.Text.RegularExpressions;
using ResearchEngine.Domain;

namespace ResearchEngine.Infrastructure;

public sealed class SourceReliabilityEvaluator : ISourceReliabilityEvaluator
{
    private static readonly HashSet<string> SocialHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "instagram.com",
        "facebook.com",
        "x.com",
        "twitter.com",
        "tiktok.com",
        "linkedin.com",
        "youtube.com",
        "m.youtube.com"
    };

    private static readonly HashSet<string> ForumHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "reddit.com",
        "old.reddit.com",
        "quora.com",
        "stackoverflow.com",
        "stackexchange.com"
    };

    private static readonly HashSet<string> ReferenceHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "wikipedia.org",
        "britannica.com"
    };

    private static readonly HashSet<string> NewsHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "reuters.com",
        "apnews.com",
        "bbc.com",
        "nytimes.com",
        "wsj.com",
        "bloomberg.com",
        "ft.com",
        "npr.org",
        "economist.com"
    };

    private static readonly HashSet<string> JournalHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "nature.com",
        "science.org",
        "sciencedirect.com",
        "springer.com",
        "link.springer.com",
        "ieee.org",
        "ieeexplore.ieee.org",
        "wiley.com",
        "cell.com",
        "tandfonline.com",
        "mdpi.com",
        "pubmed.ncbi.nlm.nih.gov",
        "ncbi.nlm.nih.gov"
    };

    private static readonly HashSet<string> PreprintHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "arxiv.org",
        "biorxiv.org",
        "medrxiv.org",
        "ssrn.com"
    };

    private static readonly string[] OfficialPathSignals =
    [
        "/docs",
        "/documentation",
        "/developers",
        "/developer",
        "/support",
        "/help",
        "/press",
        "/newsroom",
        "/investor",
        "/investors",
        "/legal",
        "/policy",
        "/policies"
    ];

    public SourceReliabilityAssessment Evaluate(SearchResult result, string? content = null, SourceKind kind = SourceKind.Web)
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

        if (MatchesHost(host, SocialHosts))
        {
            reasons.Add("Social platform content");
            return Build(host, category, SourceClassification.Social, SourceReliabilityTier.Low, 0.12, false, reasons);
        }

        if (MatchesHost(host, ForumHosts))
        {
            reasons.Add("Forum or community discussion");
            return Build(host, category, SourceClassification.Forum, SourceReliabilityTier.Low, 0.20, false, reasons);
        }

        if (IsGovernmentHost(host))
        {
            reasons.Add("Government or public institution domain");
            if (LooksLikePdf(uri))
                reasons.Add("Document-style source");
            return Build(host, category, SourceClassification.Government, SourceReliabilityTier.High, 0.98, true, reasons);
        }

        if (MatchesHost(host, JournalHosts) || LooksLikeJournalContent(normalizedContent))
        {
            reasons.Add("Scholarly publisher or journal signal");
            if (LooksLikePdf(uri))
                reasons.Add("Document-style source");
            return Build(host, category, SourceClassification.Journal, SourceReliabilityTier.High, 0.96, true, reasons);
        }

        if (MatchesHost(host, PreprintHosts))
        {
            reasons.Add("Preprint repository");
            return Build(host, category, SourceClassification.Preprint, SourceReliabilityTier.Medium, 0.88, true, reasons);
        }

        if (IsAcademicHost(host) || string.Equals(category, "research", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add(string.Equals(category, "research", StringComparison.OrdinalIgnoreCase)
                ? "Research search category"
                : "Academic institution domain");
            if (LooksLikeAcademicContent(normalizedContent))
                reasons.Add("Academic paper structure detected");
            return Build(host, category, SourceClassification.Academic, SourceReliabilityTier.High, 0.94, true, reasons);
        }

        if (MatchesHost(host, NewsHosts))
        {
            reasons.Add("Established news publisher");
            return Build(host, category, SourceClassification.News, SourceReliabilityTier.Medium, 0.72, false, reasons);
        }

        if (MatchesHost(host, ReferenceHosts))
        {
            reasons.Add("Reference or encyclopedia source");
            return Build(host, category, SourceClassification.Reference, SourceReliabilityTier.Medium, 0.64, false, reasons);
        }

        if (LooksOfficial(uri, host, title, description))
        {
            reasons.Add("Official site or documentation signal");
            return Build(host, category, SourceClassification.Official, SourceReliabilityTier.High, 0.90, true, reasons);
        }

        if (LooksLikeBlogHost(host))
        {
            reasons.Add("Blog or self-published hosting pattern");
            return Build(host, category, SourceClassification.Blog, SourceReliabilityTier.Low, 0.38, false, reasons);
        }

        if (LooksLikeAcademicContent(normalizedContent))
        {
            reasons.Add("Academic paper structure detected");
            return Build(host, category, SourceClassification.Academic, SourceReliabilityTier.Medium, 0.70, true, reasons);
        }

        reasons.Add("General web source without strong authority signals");
        return Build(host, category, SourceClassification.Unknown, SourceReliabilityTier.Low, 0.45, false, reasons);
    }

    public bool ShouldInclude(SourceReliabilityAssessment assessment, SourceDiscoveryMode mode)
        => mode switch
        {
            SourceDiscoveryMode.Balanced => assessment.Tier != SourceReliabilityTier.Blocked,
            SourceDiscoveryMode.ReliableOnly => assessment.Tier == SourceReliabilityTier.High,
            SourceDiscoveryMode.AcademicOnly => assessment.Classification is SourceClassification.Academic
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

    private static bool MatchesHost(string host, IEnumerable<string> patterns)
        => patterns.Any(pattern => host.Equals(pattern, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith($".{pattern}", StringComparison.OrdinalIgnoreCase));

    private static bool IsGovernmentHost(string host)
        => host.EndsWith(".gov", StringComparison.OrdinalIgnoreCase)
           || host.Contains(".gov.", StringComparison.OrdinalIgnoreCase)
           || host.EndsWith(".mil", StringComparison.OrdinalIgnoreCase)
           || host.Equals("gov.uk", StringComparison.OrdinalIgnoreCase)
           || host.EndsWith(".gov.uk", StringComparison.OrdinalIgnoreCase)
           || host.Equals("europa.eu", StringComparison.OrdinalIgnoreCase)
           || host.EndsWith(".europa.eu", StringComparison.OrdinalIgnoreCase);

    private static bool IsAcademicHost(string host)
        => host.EndsWith(".edu", StringComparison.OrdinalIgnoreCase)
           || host.Contains(".edu.", StringComparison.OrdinalIgnoreCase)
           || host.EndsWith(".ac.uk", StringComparison.OrdinalIgnoreCase)
           || host.Contains(".ac.", StringComparison.OrdinalIgnoreCase);

    private static bool LooksOfficial(Uri? uri, string host, string title, string description)
    {
        var path = uri?.AbsolutePath?.ToLowerInvariant() ?? string.Empty;
        var officialPath = OfficialPathSignals.Any(signal => path.Contains(signal, StringComparison.Ordinal));

        if (officialPath)
            return true;

        if (host.StartsWith("docs.", StringComparison.OrdinalIgnoreCase)
            || host.StartsWith("developer.", StringComparison.OrdinalIgnoreCase)
            || host.StartsWith("support.", StringComparison.OrdinalIgnoreCase)
            || host.StartsWith("help.", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var combined = $"{title} {description}";
        return combined.Contains("official", StringComparison.OrdinalIgnoreCase)
               || combined.Contains("documentation", StringComparison.OrdinalIgnoreCase)
               || combined.Contains("developer", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeBlogHost(string host)
        => host.Contains("medium.com", StringComparison.OrdinalIgnoreCase)
           || host.Contains("substack.com", StringComparison.OrdinalIgnoreCase)
           || host.Contains("blogspot.", StringComparison.OrdinalIgnoreCase)
           || host.Contains("wordpress.", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikePdf(Uri? uri)
        => uri?.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) == true;

    private static bool LooksLikeAcademicContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        return Regex.IsMatch(content, @"\babstract\b", RegexOptions.IgnoreCase)
               && Regex.IsMatch(content, @"\breferences\b", RegexOptions.IgnoreCase);
    }

    private static bool LooksLikeJournalContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        return Regex.IsMatch(content, @"\bdoi\b", RegexOptions.IgnoreCase)
               || Regex.IsMatch(content, @"10\.\d{4,9}/", RegexOptions.IgnoreCase);
    }
}
