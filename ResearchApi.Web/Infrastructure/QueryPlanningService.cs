using System.Text;
using System.Text.Json;
using ResearchApi.Domain;
using ResearchApi.Prompts;

namespace ResearchApi.Infrastructure;

public class QueryPlanningService (IChatModel chatModel, ILogger<QueryPlanningService> logger) : IQueryPlanningService
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

        var rawResponse = await chatModel.ChatAsync(prompt, cancellationToken:ct);

        var withoutThink = chatModel.StripThinkBlock(rawResponse.Text);
        
        var jsonStart = withoutThink.IndexOf('{');
        if (jsonStart > 0)
        {
            withoutThink = withoutThink[jsonStart..];
        }

        var plan = JsonSerializer.Deserialize<SerpQueryPlan>(withoutThink, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        var queries = plan?.Queries?
            .Where(q => !string.IsNullOrWhiteSpace(q))
            .Select(q => q.Trim())
            .Take(breadth)
            .ToList() ?? new List<string>();

        logger.LogInformation("Generated {Count} SERP queries for query '{Query}' with depth={Depth}, breadth={Breadth}", 
            queries.Count, query, depth, breadth);

        return queries;
    }
}
