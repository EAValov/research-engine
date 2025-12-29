using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace ResearchEngine.Domain;

public sealed class SerpQueryPlan
{
    [Description("List of high-value search queries, ordered from broader/overview to narrower/deeper.")]
    public required List<string> Queries { get; init; }

    public static ChatResponseFormat JsonResponseSchema(JsonSerializerOptions? jsonSerializerOptions = default)
    {
        var jsonElement = AIJsonUtilities.CreateJsonSchema(
            typeof(SerpQueryPlan),
            description: "Planned SERP queries for deep research",
            serializerOptions: jsonSerializerOptions);

        return new ChatResponseFormatJson(jsonElement);
    }
}