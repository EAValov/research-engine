using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using ResearchEngine.Domain;
using ResearchEngine.Prompts;

namespace ResearchEngine.Infrastructure;

public class QueryPlanningService(IChatModel chatModel, ILogger<QueryPlanningService> logger)
    : IQueryPlanningService
{
    public async Task<IReadOnlyList<string>> GenerateSerpQueriesAsync(
        string query,
        string clarificationsText,
        int depth,
        int breadth,
        string targetLanguage,
        CancellationToken ct)
    {
        var prompt = PlanningPromptFactory.Build(
            query,
            clarificationsText: clarificationsText,
            breadth: breadth,
            depth: depth,
            targetLanguage: targetLanguage);

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var responseFormat = SerpQueryPlanResponse.JsonResponseSchema(jsonOptions);

        var rawResponse = await chatModel.ChatAsync(
            prompt,
            tools: null,
            responseFormat: responseFormat,
            cancellationToken: ct);

        // In case if model still emits a <think> block
        var withoutThink = chatModel.StripThinkBlock(rawResponse.Text).Trim();

        SerpQueryPlanResponse? plan = null;

        try
        {
            plan = JsonSerializer.Deserialize<SerpQueryPlanResponse>(withoutThink, jsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to deserialize SERP planning JSON for query '{Query}'. Raw response: {Response}",
                query,
                withoutThink);
        }

        var maxQueries = Math.Max(1, breadth);

        var queries = plan?.Queries?
            .Where(q => !string.IsNullOrWhiteSpace(q))
            .Select(q => q.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxQueries)
            .ToList() ?? new List<string>();

        if (queries.Count == 0)
        {
            var fallbackQuery = BuildFallbackQuery(query);
            queries.Add(fallbackQuery);

            logger.LogWarning(
                "SERP planner returned 0 queries for '{Query}'. Falling back to a single query '{FallbackQuery}'. Raw response: {RawResponse}",
                query,
                fallbackQuery,
                Truncate(withoutThink, 400));
        }

        logger.LogInformation(
            "Generated {Count} SERP queries for query '{Query}' with depth={Depth}, breadth={Breadth}",
            queries.Count,
            query,
            depth,
            breadth);

        return queries;
    }

    private static string BuildFallbackQuery(string query)
    {
        var trimmed = query?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed))
            return trimmed;

        return "latest overview";
    }

    private static string Truncate(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
            return value;

        return value[..maxChars] + "...";
    }

    private sealed class SerpQueryPlanResponse
    {
        [Description("List of high-value search queries, ordered from broader/overview to narrower/deeper.")]
        public required List<string> Queries { get; init; }

        public static ChatResponseFormat JsonResponseSchema(JsonSerializerOptions? jsonSerializerOptions = default)
        {
            var jsonElement = AIJsonUtilities.CreateJsonSchema(
                typeof(SerpQueryPlanResponse),
                description: "Planned SERP queries for deep research",
                serializerOptions: jsonSerializerOptions);

            return new ChatResponseFormatJson(jsonElement);
        }
    }
}
