using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace ResearchEngine.Domain;

public sealed record ExtractedLearningItemWithEvidence(
    string Text,
    float Importance,
    string EvidenceText);

public sealed class ExtractedLearningItem
{
    [Description("Single, self-contained learning text in the target language, highly relevant to the user's query.")]
    public required string Text { get; init; }

    [Description("Importance score between 0.0 (barely relevant) and 1.0 (critical for answering the query).")]
    public required float Importance { get; init; }
}

public sealed class LearningExtractionResponse
{
    [Description("Array of extracted learnings.")]
    public required List<ExtractedLearningItem> Learnings { get; init; }

    public static ChatResponseFormat JsonResponseSchema(JsonSerializerOptions? jsonSerializerOptions = default)
    {
        var jsonElement = AIJsonUtilities.CreateJsonSchema(
            typeof(LearningExtractionResponse),
            description: "Structured learnings extraction result",
            serializerOptions: jsonSerializerOptions);

        return new ChatResponseFormatJson(jsonElement);
    }
}