using ResearchEngine.Domain;

namespace ResearchEngine.Infrastructure;

public sealed record AppliedSourceTrustPolicy(
    IReadOnlyList<SourceTrustRule> Rules,
    IReadOnlyList<string> ActivePackNames);

public sealed record SourceTrustRule(
    string Name,
    SourceClassification Classification,
    SourceReliabilityTier Tier,
    double Score,
    bool IsPrimarySource,
    string Reason,
    string[]? HostEquals = null,
    string[]? HostEndsWith = null,
    string[]? HostContains = null,
    string[]? HostStartsWith = null,
    string[]? PathContains = null,
    string[]? SearchCategoryEquals = null,
    string[]? TextContains = null,
    string[]? ContentRegexAny = null,
    string[]? ContentRegexAll = null);

public sealed record SourceTrustRulePack(
    string Name,
    string[] RegionKeywords,
    string[] LanguageCodes,
    IReadOnlyList<SourceTrustRule> Rules);

public static class SourceTrustRuleCatalog
{
    private static readonly SourceTrustRulePack GlobalPack = new(
        Name: "Global",
        RegionKeywords: [],
        LanguageCodes: [],
        Rules:
        [
            new(
                Name: "global-social-hosts",
                Classification: SourceClassification.Social,
                Tier: SourceReliabilityTier.Low,
                Score: 0.12,
                IsPrimarySource: false,
                Reason: "Social platform content",
                HostEquals:
                [
                    "instagram.com",
                    "facebook.com",
                    "x.com",
                    "twitter.com",
                    "tiktok.com",
                    "linkedin.com",
                    "youtube.com",
                    "m.youtube.com"
                ]),
            new(
                Name: "global-forum-hosts",
                Classification: SourceClassification.Forum,
                Tier: SourceReliabilityTier.Low,
                Score: 0.20,
                IsPrimarySource: false,
                Reason: "Forum or community discussion",
                HostEquals:
                [
                    "reddit.com",
                    "old.reddit.com",
                    "quora.com",
                    "stackoverflow.com",
                    "stackexchange.com"
                ]),
            new(
                Name: "global-government-gov",
                Classification: SourceClassification.Government,
                Tier: SourceReliabilityTier.High,
                Score: 0.98,
                IsPrimarySource: true,
                Reason: "Government or public institution domain",
                HostEndsWith: [".gov"]),
            new(
                Name: "global-government-gov-subdomain",
                Classification: SourceClassification.Government,
                Tier: SourceReliabilityTier.High,
                Score: 0.98,
                IsPrimarySource: true,
                Reason: "Government or public institution domain",
                HostContains: [".gov."]),
            new(
                Name: "global-government-mil",
                Classification: SourceClassification.Government,
                Tier: SourceReliabilityTier.High,
                Score: 0.98,
                IsPrimarySource: true,
                Reason: "Government or public institution domain",
                HostEndsWith: [".mil"]),
            new(
                Name: "global-government-uk-eu",
                Classification: SourceClassification.Government,
                Tier: SourceReliabilityTier.High,
                Score: 0.98,
                IsPrimarySource: true,
                Reason: "Government or public institution domain",
                HostEquals: ["gov.uk", "europa.eu"],
                HostEndsWith: [".gov.uk", ".europa.eu"]),
            new(
                Name: "global-journal-hosts",
                Classification: SourceClassification.Journal,
                Tier: SourceReliabilityTier.High,
                Score: 0.96,
                IsPrimarySource: true,
                Reason: "Scholarly publisher or journal signal",
                HostEquals:
                [
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
                ]),
            new(
                Name: "global-journal-content",
                Classification: SourceClassification.Journal,
                Tier: SourceReliabilityTier.High,
                Score: 0.96,
                IsPrimarySource: true,
                Reason: "Scholarly publisher or journal signal",
                ContentRegexAny:
                [
                    @"\bdoi\b",
                    @"10\.\d{4,9}/"
                ]),
            new(
                Name: "global-preprint-hosts",
                Classification: SourceClassification.Preprint,
                Tier: SourceReliabilityTier.Medium,
                Score: 0.88,
                IsPrimarySource: true,
                Reason: "Preprint repository",
                HostEquals:
                [
                    "arxiv.org",
                    "biorxiv.org",
                    "medrxiv.org",
                    "ssrn.com"
                ]),
            new(
                Name: "global-academic-edu",
                Classification: SourceClassification.Academic,
                Tier: SourceReliabilityTier.High,
                Score: 0.94,
                IsPrimarySource: true,
                Reason: "Academic institution domain",
                HostEndsWith: [".edu"],
                HostContains: [".edu."]),
            new(
                Name: "global-academic-ac",
                Classification: SourceClassification.Academic,
                Tier: SourceReliabilityTier.High,
                Score: 0.94,
                IsPrimarySource: true,
                Reason: "Academic institution domain",
                HostEndsWith: [".ac.uk"],
                HostContains: [".ac."]),
            new(
                Name: "global-news-hosts",
                Classification: SourceClassification.News,
                Tier: SourceReliabilityTier.Medium,
                Score: 0.72,
                IsPrimarySource: false,
                Reason: "Established news publisher",
                HostEquals:
                [
                    "reuters.com",
                    "apnews.com",
                    "bbc.com",
                    "nytimes.com",
                    "wsj.com",
                    "bloomberg.com",
                    "ft.com",
                    "npr.org",
                    "economist.com"
                ]),
            new(
                Name: "global-reference-hosts",
                Classification: SourceClassification.Reference,
                Tier: SourceReliabilityTier.Medium,
                Score: 0.64,
                IsPrimarySource: false,
                Reason: "Reference or encyclopedia source",
                HostEquals:
                [
                    "wikipedia.org",
                    "britannica.com"
                ]),
            new(
                Name: "global-official-known-hosts",
                Classification: SourceClassification.Official,
                Tier: SourceReliabilityTier.High,
                Score: 0.90,
                IsPrimarySource: true,
                Reason: "Official site or documentation signal",
                HostEquals:
                [
                    "a2a-protocol.org",
                    "modelcontextprotocol.io"
                ]),
            new(
                Name: "global-official-doc-paths",
                Classification: SourceClassification.Official,
                Tier: SourceReliabilityTier.High,
                Score: 0.90,
                IsPrimarySource: true,
                Reason: "Official site or documentation signal",
                PathContains:
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
                ]),
            new(
                Name: "global-official-doc-host-prefixes",
                Classification: SourceClassification.Official,
                Tier: SourceReliabilityTier.High,
                Score: 0.90,
                IsPrimarySource: true,
                Reason: "Official site or documentation signal",
                HostStartsWith:
                [
                    "docs.",
                    "developer.",
                    "support.",
                    "help."
                ]),
            new(
                Name: "global-official-text-signals",
                Classification: SourceClassification.Official,
                Tier: SourceReliabilityTier.High,
                Score: 0.90,
                IsPrimarySource: true,
                Reason: "Official site or documentation signal",
                TextContains:
                [
                    "official",
                    "documentation",
                    "developer",
                    "specification"
                ]),
            new(
                Name: "global-blog-research-hosts",
                Classification: SourceClassification.Blog,
                Tier: SourceReliabilityTier.Medium,
                Score: 0.58,
                IsPrimarySource: false,
                Reason: "Blog or self-published publishing pattern; Research search category",
                HostContains:
                [
                    "medium.com",
                    "substack.com",
                    "blogspot.",
                    "wordpress."
                ],
                SearchCategoryEquals: ["research"]),
            new(
                Name: "global-blog-research-paths",
                Classification: SourceClassification.Blog,
                Tier: SourceReliabilityTier.Medium,
                Score: 0.58,
                IsPrimarySource: false,
                Reason: "Blog or self-published publishing pattern; Research search category",
                PathContains:
                [
                    "/blog",
                    "/posts",
                    "/article"
                ],
                SearchCategoryEquals: ["research"]),
            new(
                Name: "global-blog-hosts",
                Classification: SourceClassification.Blog,
                Tier: SourceReliabilityTier.Low,
                Score: 0.38,
                IsPrimarySource: false,
                Reason: "Blog or self-published publishing pattern",
                HostContains:
                [
                    "medium.com",
                    "substack.com",
                    "blogspot.",
                    "wordpress."
                ]),
            new(
                Name: "global-blog-paths",
                Classification: SourceClassification.Blog,
                Tier: SourceReliabilityTier.Low,
                Score: 0.38,
                IsPrimarySource: false,
                Reason: "Blog or self-published publishing pattern",
                PathContains:
                [
                    "/blog",
                    "/posts",
                    "/article"
                ]),
            new(
                Name: "global-blog-text",
                Classification: SourceClassification.Blog,
                Tier: SourceReliabilityTier.Low,
                Score: 0.38,
                IsPrimarySource: false,
                Reason: "Blog or self-published publishing pattern",
                TextContains: ["blog"]),
            new(
                Name: "global-academic-content",
                Classification: SourceClassification.Academic,
                Tier: SourceReliabilityTier.Medium,
                Score: 0.70,
                IsPrimarySource: true,
                Reason: "Academic paper structure detected",
                ContentRegexAll:
                [
                    @"\babstract\b",
                    @"\breferences\b"
                ]),
            new(
                Name: "global-research-category",
                Classification: SourceClassification.Unknown,
                Tier: SourceReliabilityTier.Medium,
                Score: 0.60,
                IsPrimarySource: false,
                Reason: "Research search category",
                SearchCategoryEquals: ["research"])
        ]);

    private static readonly SourceTrustRulePack RussiaPack = new(
        Name: "Russia",
        RegionKeywords:
        [
            "russia",
            "russian",
            "russian federation",
            "ru",
            "россия",
            "российская",
            "российская федерация"
        ],
        LanguageCodes:
        [
            "ru"
        ],
        Rules:
        [
            new(
                Name: "russia-government-gov-ru",
                Classification: SourceClassification.Government,
                Tier: SourceReliabilityTier.High,
                Score: 0.98,
                IsPrimarySource: true,
                Reason: "Government or public institution domain",
                HostEndsWith: [".gov.ru"]),
            new(
                Name: "russia-official-hosts",
                Classification: SourceClassification.Official,
                Tier: SourceReliabilityTier.High,
                Score: 0.95,
                IsPrimarySource: true,
                Reason: "Regional official domain",
                HostEquals:
                [
                    "government.ru",
                    "kremlin.ru",
                    "cbr.ru",
                    "publication.pravo.gov.ru",
                    "duma.gov.ru",
                    "council.gov.ru"
                ]),
            new(
                Name: "russia-academic-hosts",
                Classification: SourceClassification.Academic,
                Tier: SourceReliabilityTier.High,
                Score: 0.94,
                IsPrimarySource: true,
                Reason: "Regional academic institution domain",
                HostEquals:
                [
                    "hse.ru",
                    "msu.ru",
                    "spbu.ru",
                    "itmo.ru",
                    "skoltech.ru",
                    "urfu.ru"
                ],
                HostEndsWith:
                [
                    ".edu.ru",
                    ".ac.ru"
                ]),
            new(
                Name: "russia-news-hosts",
                Classification: SourceClassification.News,
                Tier: SourceReliabilityTier.Medium,
                Score: 0.72,
                IsPrimarySource: false,
                Reason: "Regional established news publisher",
                HostEquals:
                [
                    "tass.ru",
                    "tass.com",
                    "interfax.ru"
                ]),
                 new(
                Name: "russia-blog-hosts",
                Classification: SourceClassification.Blog,
                Tier: SourceReliabilityTier.Low,
                Score: 0.38,
                IsPrimarySource: false,
                Reason: "Blog or self-published publishing pattern",
                HostContains:
                [
                    "habr.com",
                    "dzen.ru",
                ]),
        ]);

    private static readonly SourceTrustRulePack ChinaPack = new(
        Name: "China",
        RegionKeywords:
        [
            "china",
            "chinese",
            "prc",
            "cn",
            "mainland china",
            "people's republic of china",
            "peoples republic of china",
            "中国",
            "中华人民共和国",
            "beijing",
            "北京",
            "shanghai",
            "上海"
        ],
        LanguageCodes:
        [
            "zh"
        ],
        Rules:
        [
            new(
                Name: "china-government-gov-cn",
                Classification: SourceClassification.Government,
                Tier: SourceReliabilityTier.High,
                Score: 0.98,
                IsPrimarySource: true,
                Reason: "Government or public institution domain",
                HostEndsWith: [".gov.cn"]),
            new(
                Name: "china-official-hosts",
                Classification: SourceClassification.Official,
                Tier: SourceReliabilityTier.High,
                Score: 0.95,
                IsPrimarySource: true,
                Reason: "Regional official domain",
                HostEquals:
                [
                    "gov.cn",
                    "stats.gov.cn",
                    "moe.gov.cn",
                    "miit.gov.cn",
                    "pbc.gov.cn"
                ]),
            new(
                Name: "china-academic-hosts",
                Classification: SourceClassification.Academic,
                Tier: SourceReliabilityTier.High,
                Score: 0.94,
                IsPrimarySource: true,
                Reason: "Regional academic institution domain",
                HostEquals:
                [
                    "cas.cn",
                    "tsinghua.edu.cn",
                    "pku.edu.cn",
                    "fudan.edu.cn",
                    "sjtu.edu.cn"
                ],
                HostEndsWith:
                [
                    ".edu.cn"
                ]),
            new(
                Name: "china-news-hosts",
                Classification: SourceClassification.News,
                Tier: SourceReliabilityTier.Medium,
                Score: 0.72,
                IsPrimarySource: false,
                Reason: "Regional established news publisher",
                HostEquals:
                [
                    "news.cn",
                    "xinhuanet.com",
                    "chinadaily.com.cn"
                ])
        ]);

    private static readonly IReadOnlyList<SourceTrustRulePack> RegionalPacks =
    [
        RussiaPack,
        ChinaPack
    ];

    public static AppliedSourceTrustPolicy BuildPolicy(string? region, string? language)
    {
        var activePacks = new List<SourceTrustRulePack> { GlobalPack };
        var activeNames = new List<string> { GlobalPack.Name };

        foreach (var pack in RegionalPacks)
        {
            if (!MatchesRegionPack(pack, region, language))
                continue;

            activePacks.Add(pack);
            activeNames.Add(pack.Name);
        }

        var rules = activePacks
            .SelectMany(static pack => pack.Rules)
            .ToList();

        return new AppliedSourceTrustPolicy(rules, activeNames);
    }

    private static bool MatchesRegionPack(SourceTrustRulePack pack, string? region, string? language)
    {
        var normalizedRegion = NormalizeRegion(region);
        var regionParts = GetRegionParts(normalizedRegion);
        var normalizedLanguage = NormalizeLanguage(language);

        if (!string.IsNullOrWhiteSpace(normalizedRegion))
        {
            foreach (var keyword in pack.RegionKeywords)
            {
                var normalizedKeyword = NormalizeToken(keyword);
                if (string.IsNullOrWhiteSpace(normalizedKeyword))
                    continue;

                if (regionParts.Contains(normalizedKeyword, StringComparer.OrdinalIgnoreCase))
                    return true;

                if (normalizedKeyword.Length <= 3)
                    continue;

                if (normalizedRegion.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(normalizedLanguage)
            && pack.LanguageCodes.Any(code => string.Equals(code, normalizedLanguage, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    private static string NormalizeRegion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        return NormalizeToken(raw.Replace('_', ' ').Replace('/', ' ').Replace('\\', ' '));
    }

    private static string NormalizeLanguage(string? raw)
    {
        var normalized = NormalizeToken(raw);
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        return normalized switch
        {
            "ru" or "ru-ru" or "russian" or "русский" => "ru",
            "zh" or "zh-cn" or "zh-hans" or "chinese" or "中文" => "zh",
            _ when normalized.Length == 2 => normalized,
            _ when normalized.Length > 2 && normalized[2] == '-' => normalized[..2],
            _ => normalized
        };
    }

    private static IReadOnlySet<string> GetRegionParts(string normalizedRegion)
    {
        if (string.IsNullOrWhiteSpace(normalizedRegion))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var parts = normalizedRegion
            .Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(static part => ExpandRegionPart(part))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return parts;
    }

    private static IEnumerable<string> ExpandRegionPart(string part)
    {
        if (string.IsNullOrWhiteSpace(part))
            yield break;

        var normalized = NormalizeToken(part);
        if (string.IsNullOrWhiteSpace(normalized))
            yield break;

        yield return normalized;

        foreach (var token in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return token;
    }

    private static string NormalizeToken(string? raw)
        => string.Join(
            " ",
            (raw ?? string.Empty)
                .Trim()
                .ToLowerInvariant()
                .Split([' ', ',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
