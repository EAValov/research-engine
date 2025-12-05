using System.Text.Json;
using ResearchApi.Domain;
using ResearchApi.Infrastructure;
using Serilog;

namespace ResearchApi.Application;

public class SynthesisToolHandler
{
    private readonly ILearningEmbeddingService _retrieval;
    private readonly Dictionary<string, int> _sourceIndexMap;
    private readonly Guid _jobId;

    public SynthesisToolHandler(
        ILearningEmbeddingService retrieval,
        Dictionary<string, int> sourceIndexMap,
        Guid jobId)
    {
        _retrieval      = retrieval;
        _sourceIndexMap = sourceIndexMap;
        _jobId          = jobId;
    }

    public async Task<GetSimilarLearningsToolResult> HandleGetSimilarLearningsAsync(
        string queryText,
        string? language = null,
        string? region = null,
        CancellationToken ct = default)
    {
        Log.Logger.Information("[SynthesisTool] get_similar_learnings called. jobId={_jobId}, query='{queryText}', lang={language}, region={region}", _jobId, queryText, language, region);

        var learnings = await _retrieval.GetSimilarLearningsAsync(
            queryText: queryText,
            jobId: _jobId,
            queryHash: null,
            language: language,
            region: region,
            topK: 6,
            ct: ct);

        Log.Logger.Information("[SynthesisTool] retrieved {learnings} learnings from DB.", learnings.Count);

        return new GetSimilarLearningsToolResult
        {
            TotalAvailable = learnings.Count,
            Learnings = learnings
                .Select(l =>
                {
                    if (!_sourceIndexMap.TryGetValue(l.SourceUrl, out var idx))
                    {
                        // If URL not previously indexed, you can decide to:
                        // - ignore it, or
                        // - assign a new index and mutate _sourceIndexMap.
                        idx = _sourceIndexMap.Count + 1;
                        _sourceIndexMap[l.SourceUrl] = idx;
                    }

                    var citation = $"[{idx}]";

                    // Ensure citation is in the text (at the end) so the model sees it.
                    var textWithCitation = l.Text.Trim();
                    if (!textWithCitation.EndsWith(citation, StringComparison.Ordinal))
                    {
                        textWithCitation = $"{textWithCitation} {citation}";
                    }

                    return new ToolLearningDto
                    {
                        Id        = l.Id,
                        Text      = textWithCitation,
                        SourceUrl = l.SourceUrl,
                        Citation  = citation
                    };
                })
                .ToList()
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