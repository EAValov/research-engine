using System.Text;
using System.Text.Json;
using ResearchApi.Domain;
using ResearchApi.Prompts;

namespace ResearchApi.Infrastructure;

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

        var responseFormat = SerpQueryPlan.JsonResponseSchema(jsonOptions);

        var rawResponse = await chatModel.ChatAsync(
            prompt,
            tools: null,
            responseFormat: responseFormat,
            cancellationToken: ct);

        // In case if model still emits a <think> block
        var withoutThink = chatModel.StripThinkBlock(rawResponse.Text).Trim();

        SerpQueryPlan? plan = null;

        try
        {
            plan = JsonSerializer.Deserialize<SerpQueryPlan>(withoutThink, jsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to deserialize SERP planning JSON for query '{Query}'. Raw response: {Response}",
                query,
                withoutThink);
        }

        var queries = plan?.Queries?
            .Where(q => !string.IsNullOrWhiteSpace(q))
            .Select(q => q.Trim())
            .Take(breadth)
            .ToList() ?? new List<string>();

        logger.LogInformation(
            "Generated {Count} SERP queries for query '{Query}' with depth={Depth}, breadth={Breadth}",
            queries.Count,
            query,
            depth,
            breadth);

        return queries;
    }
}
