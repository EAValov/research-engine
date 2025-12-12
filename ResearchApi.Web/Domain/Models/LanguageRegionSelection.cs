using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;

public sealed class LanguageRegionSelection
{
    [Description("2-letter ISO 639-1 language code in lowercase (e.g. \"en\", \"de\").")]
    public required string Language { get; init; }

    [Description("Human-readable location string (e.g. \"Germany\", \"Berlin,Germany\") or null if no specific region.")]
    public string? Region { get; init; }

    public static ChatResponseFormat JsonResponseSchema(JsonSerializerOptions? jsonSerializerOptions = default)
    {
        var jsonElement = AIJsonUtilities.CreateJsonSchema(
            typeof(LanguageRegionSelection),
            description: "Selected language and region for web research",
            serializerOptions: jsonSerializerOptions);

        return new ChatResponseFormatJson(jsonElement);
    }
}