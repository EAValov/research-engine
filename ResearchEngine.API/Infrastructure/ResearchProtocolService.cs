using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using ResearchEngine.Domain;
using ResearchEngine.Prompts;

namespace ResearchEngine.Infrastructure;

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

        var responseFormat = LanguageRegionSelectionResponse.JsonResponseSchema(jsonOptions);

        var raw = await _chatModel.ChatAsync(
            prompt,
            tools: null,
            responseFormat: responseFormat,
            cancellationToken: ct);

        if (string.IsNullOrWhiteSpace(raw.Text))
            return (defaultLang, defaultRegion);

        var rawText = _chatModel.StripThinkBlock(raw.Text).Trim();

        LanguageRegionSelectionResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<LanguageRegionSelectionResponse>(rawText, jsonOptions);
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

    private sealed class BreadthDepthSelection
    {
        [Description("How many distinct directions / subtopics to explore (1–8).")]
        public required int Breadth { get; init; }

        [Description("How deep and multi-step the research should be (1–4).")]
        public required int Depth { get; init; }

        public static ChatResponseFormat JsonResponseSchema(JsonSerializerOptions? jsonSerializerOptions = default)
        {
            var jsonElement = AIJsonUtilities.CreateJsonSchema(
                typeof(BreadthDepthSelection),
                description: "Selected breadth and depth configuration for deep research",
                serializerOptions: jsonSerializerOptions);

            return new ChatResponseFormatJson(jsonElement);
        }
    }

    private sealed class ClarificationQuestionsResponse
    {
        [Description("Array of clarification questions for the user, in natural language.")]
        public required List<string> Queries { get; init; }

        public static ChatResponseFormat JsonResponseSchema(JsonSerializerOptions? jsonSerializerOptions = default)
        {
            var jsonElement = AIJsonUtilities.CreateJsonSchema(
                typeof(ClarificationQuestionsResponse),
                description: "Clarification questions to refine the user's research query",
                serializerOptions: jsonSerializerOptions);

            return new ChatResponseFormatJson(jsonElement);
        }
    }

    private sealed class LanguageRegionSelectionResponse
    {
        [Description("2-letter ISO 639-1 language code in lowercase (e.g. \"en\", \"de\").")]
        public required string Language { get; init; }

        [Description("Human-readable location string (e.g. \"Germany\", \"Berlin,Germany\") or null if no specific region.")]
        public string? Region { get; init; }

        public static ChatResponseFormat JsonResponseSchema(JsonSerializerOptions? jsonSerializerOptions = default)
        {
            var jsonElement = AIJsonUtilities.CreateJsonSchema(
                typeof(LanguageRegionSelectionResponse),
                description: "Selected language and region for web research",
                serializerOptions: jsonSerializerOptions);

            return new ChatResponseFormatJson(jsonElement);
        }
    }
}
