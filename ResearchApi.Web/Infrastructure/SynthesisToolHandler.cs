using ResearchApi.Domain;

namespace ResearchApi.Infrastructure;

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

        return new GetSimilarLearningsToolResult
        {
            TotalAvailable = learnings.Count,
            Learnings = learnings.Select(l =>
            {
                var url = l.Source?.Url ?? string.Empty;
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
                    Citation = citation
                };
            }).ToList()
        };
    }

    public sealed class ToolLearningDto
    {
        public Guid Id { get; set; }
        public string Text { get; set; } = null!;
        public string SourceUrl { get; set; } = null!;
        public string Citation { get; set; } = null!;
    }

    public sealed class GetSimilarLearningsToolResult
    {
        public List<ToolLearningDto> Learnings { get; set; } = new();
        public int TotalAvailable { get; set; }
    }
}
