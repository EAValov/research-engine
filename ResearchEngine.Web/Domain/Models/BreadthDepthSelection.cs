using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace ResearchEngine.Domain;

public sealed class BreadthDepthSelection
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
