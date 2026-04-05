using ResearchEngine.Domain;

namespace ResearchEngine.Infrastructure;

public sealed class SynthesisToolHandler(
    ILearningIntelService retrieval,
    Guid synthesisId,
    string? language = null,
    string? region = null
)
{
    public async Task<GetSimilarLearningsToolResult> HandleGetSimilarLearningsAsync(
        string queryText,
        CancellationToken ct = default)
    {
        var learnings = await retrieval.GetSimilarLearningsAsync(
            queryText: queryText,
            synthesisId: synthesisId,
            language: language,
            region: region,
            topK: 20,
            ct: ct);

        var toolLearnings = learnings.Select(l =>
        {
            var url = l.Source?.Reference ?? string.Empty;
            if (string.IsNullOrWhiteSpace(url))
                url = "about:blank";

            var citation = $"[lrn:{l.Id:N}]";

            var textWithCitation = l.Text.Trim();
            if (!textWithCitation.EndsWith(citation, StringComparison.Ordinal))
                textWithCitation = $"{textWithCitation} {citation}";

            return new ToolLearningDto
            {
                Id = l.Id,
                Text = textWithCitation,
                SourceUrl = url,
                SourceTitle = l.Source?.Title,
                SourceDomain = l.Source?.Domain,
                SourceClassification = l.Source is not null
                    ? l.Source.Classification.ToApiValue()
                    : SourceClassification.Unknown.ToApiValue(),
                ReliabilityTier = l.Source is not null
                    ? l.Source.ReliabilityTier.ToApiValue()
                    : SourceReliabilityTier.Low.ToApiValue(),
                ReliabilityScore = l.Source?.ReliabilityScore ?? 0.0,
                IsPrimarySource = l.Source?.IsPrimarySource ?? false,
                StatementType = l.StatementType.ToApiValue(),
                Citation = citation
            };
        }).ToList();

        return new GetSimilarLearningsToolResult
        {
            TotalAvailable = learnings.Count,
            EvidenceProfile = BuildEvidenceProfile(toolLearnings),
            Learnings = toolLearnings
        };
    }

    private static EvidenceProfileDto BuildEvidenceProfile(IReadOnlyList<ToolLearningDto> learnings)
    {
        var distinctDomains = learnings
            .Select(l => l.SourceDomain)
            .Where(static domain => !string.IsNullOrWhiteSpace(domain))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var highTrustCount = learnings.Count(l => string.Equals(l.ReliabilityTier, SourceReliabilityTier.High.ToApiValue(), StringComparison.OrdinalIgnoreCase));
        var mediumTrustCount = learnings.Count(l => string.Equals(l.ReliabilityTier, SourceReliabilityTier.Medium.ToApiValue(), StringComparison.OrdinalIgnoreCase));
        var lowTrustCount = learnings.Count(l => string.Equals(l.ReliabilityTier, SourceReliabilityTier.Low.ToApiValue(), StringComparison.OrdinalIgnoreCase));
        var primarySourceCount = learnings.Count(l => l.IsPrimarySource);

        var statementTypes = learnings
            .GroupBy(l => l.StatementType, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CountByLabelDto { Label = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sourceClassifications = learnings
            .GroupBy(l => l.SourceClassification, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CountByLabelDto { Label = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var notes = new List<string>();
        if (lowTrustCount > 0)
            notes.Add("Mixed evidence includes low-trust sources. Avoid definitive wording unless higher-trust material clearly supports the claim.");

        if (primarySourceCount == 0)
            notes.Add("No primary sources are present in this result set. Treat the section as a synthesis of secondary reporting or commentary.");

        if (learnings.Count(l => string.Equals(l.StatementType, LearningStatementType.Forecast.ToApiValue(), StringComparison.OrdinalIgnoreCase)) > 0)
            notes.Add("Forecasts are present. Keep future-facing claims attributed and conditional rather than settled.");

        if (learnings.Count(l => string.Equals(l.StatementType, LearningStatementType.Claim.ToApiValue(), StringComparison.OrdinalIgnoreCase)) > 0)
            notes.Add("Attributed claims are present. Name the source or source type instead of rewriting those claims as neutral facts.");

        if (learnings.Count(l => string.Equals(l.StatementType, LearningStatementType.Commentary.ToApiValue(), StringComparison.OrdinalIgnoreCase)) > 0)
            notes.Add("Some evidence is commentary or interpretation. Use it as framing, not as hard proof.");

        if (learnings.Count(l => string.Equals(l.StatementType, LearningStatementType.Contested.ToApiValue(), StringComparison.OrdinalIgnoreCase)) > 0)
            notes.Add("Some points are explicitly contested or unresolved. Surface the disagreement instead of smoothing it away.");

        if (highTrustCount >= 2 && primarySourceCount >= 1 && distinctDomains >= 2)
            notes.Add("There is at least some corroboration across multiple domains, including higher-trust material.");

        return new EvidenceProfileDto
        {
            DistinctDomains = distinctDomains,
            HighTrustCount = highTrustCount,
            MediumTrustCount = mediumTrustCount,
            LowTrustCount = lowTrustCount,
            PrimarySourceCount = primarySourceCount,
            StatementTypes = statementTypes,
            SourceClassifications = sourceClassifications,
            CalibrationNotes = notes
        };
    }

    public sealed class ToolLearningDto
    {
        public Guid Id { get; set; }
        public string Text { get; set; } = null!;
        public string SourceUrl { get; set; } = null!;
        public string? SourceTitle { get; set; }
        public string? SourceDomain { get; set; }
        public string SourceClassification { get; set; } = null!;
        public string ReliabilityTier { get; set; } = null!;
        public double ReliabilityScore { get; set; }
        public bool IsPrimarySource { get; set; }
        public string StatementType { get; set; } = null!;
        public string Citation { get; set; } = null!;
    }

    public sealed class GetSimilarLearningsToolResult
    {
        public List<ToolLearningDto> Learnings { get; set; } = new();
        public int TotalAvailable { get; set; }
        public EvidenceProfileDto EvidenceProfile { get; set; } = new();
    }

    public sealed class EvidenceProfileDto
    {
        public int DistinctDomains { get; set; }
        public int HighTrustCount { get; set; }
        public int MediumTrustCount { get; set; }
        public int LowTrustCount { get; set; }
        public int PrimarySourceCount { get; set; }
        public List<CountByLabelDto> StatementTypes { get; set; } = new();
        public List<CountByLabelDto> SourceClassifications { get; set; } = new();
        public List<string> CalibrationNotes { get; set; } = new();
    }

    public sealed class CountByLabelDto
    {
        public string Label { get; set; } = null!;
        public int Count { get; set; }
    }
}
