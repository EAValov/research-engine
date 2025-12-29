using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace ResearchEngine.Domain;

public sealed class ClarificationQuestionsResponse
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
