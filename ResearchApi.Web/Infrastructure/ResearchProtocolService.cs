using System.Text.Json;
using ResearchApi.Domain;
using ResearchApi.Infrastructure;
using ResearchApi.Prompts;

public class ResearchProtocolService : IResearchProtocolService
{
    private readonly IChatModel _chatModel;

    public ResearchProtocolService(IChatModel chatModel)
    {
        _chatModel = chatModel;
    }

    public async Task<IReadOnlyList<string>> GenerateFeedbackQueriesAsync(
        string query,
        bool includeBreadthDepthQuestions,
        CancellationToken ct = default)
    {
        var prompt = FeedbackPromptFactory.Build(query, includeBreadthDepthQuestions);

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var responseFormat = ClarificationQuestionsResponse.JsonResponseSchema(jsonOptions);

        var rawResponse = await _chatModel.ChatAsync(
            prompt,
            tools: null,
            responseFormat: responseFormat,
            cancellationToken: ct);

        var jsonText = _chatModel.StripThinkBlock(rawResponse.Text).Trim();

        ClarificationQuestionsResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ClarificationQuestionsResponse>(jsonText, jsonOptions);
        }
        catch
        {
            // If the LLM somehow returns invalid JSON despite the schema, just fall back to an empty list.
            return Array.Empty<string>();
        }

        var queries = parsed?.Queries?
            .Where(q => !string.IsNullOrWhiteSpace(q))
            .Select(q => q.Trim())
            .ToList() ?? new List<string>();

        return queries;
    }

    public async Task<(int breadth, int depth)> AutoSelectBreadthDepthAsync(
        string query,
        IReadOnlyList<Clarification> clarifications,
        CancellationToken ct = default)
    {
        const int defaultBreadth = 2;
        const int defaultDepth = 2;

        var prompt = SelectBreadthDepthPromptFactory.Build(query, clarifications);

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var responseFormat = BreadthDepthSelection.JsonResponseSchema(jsonOptions);

        var raw = await _chatModel.ChatAsync(
            prompt,
            tools: null,
            responseFormat: responseFormat,
            cancellationToken: ct);

        if (string.IsNullOrWhiteSpace(raw.Text))
            return (defaultBreadth, defaultDepth);

        var rawText = _chatModel.StripThinkBlock(raw.Text).Trim();

        BreadthDepthSelection? parsed = null;

        try
        {
            parsed = JsonSerializer.Deserialize<BreadthDepthSelection>(rawText, jsonOptions);
        }
        catch
        {
            // If the model somehow returns malformed JSON despite the schema, just fall back.
            return (defaultBreadth, defaultDepth);
        }

        var breadth = parsed?.Breadth ?? defaultBreadth;
        var depth = parsed?.Depth ?? defaultDepth;

        breadth = Math.Clamp(breadth, 1, 8);
        depth = Math.Clamp(depth, 1, 4);

        return (breadth, depth);
    }

    public async Task<(string language, string? region)> AutoSelectLanguageRegionAsync(
        string query,
        IReadOnlyList<Clarification> clarifications,
        CancellationToken ct = default)
    {
        const string defaultLang = "en";
        const string? defaultRegion = null;

        var prompt = LanguageRegionSelectionPromptFactory.Build(query, clarifications);

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var responseFormat = LanguageRegionSelection.JsonResponseSchema(jsonOptions);

        var raw = await _chatModel.ChatAsync(
            prompt,
            tools: null,
            responseFormat: responseFormat,
            cancellationToken: ct);

        if (string.IsNullOrWhiteSpace(raw.Text))
            return (defaultLang, defaultRegion);

        var rawText = _chatModel.StripThinkBlock(raw.Text).Trim();

        LanguageRegionSelection? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<LanguageRegionSelection>(rawText, jsonOptions);
        }
        catch
        {
            return (defaultLang, defaultRegion);
        }

        // Normalize language: lower-case, 2-letter, fallback if invalid
        var language = parsed?.Language?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(language) || language!.Length != 2)
        {
            language = defaultLang;
        }

        // Normalize region: treat empty/whitespace as null
        var region = parsed?.Region?.Trim();
        if (string.IsNullOrWhiteSpace(region))
        {
            region = defaultRegion;
        }

        return (language!, region);
    }
}
