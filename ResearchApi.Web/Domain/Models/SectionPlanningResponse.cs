using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace ResearchApi.Domain;

public sealed class SectionPlanningResponse
{
    [Description("Ordered list of planned sections for the report.")]
    public required List<SectionPlanItem> Sections { get; init; }

    public static ChatResponseFormat JsonResponseSchema(JsonSerializerOptions? jsonSerializerOptions = default)
    {
        var jsonElement = AIJsonUtilities.CreateJsonSchema(
            typeof(SectionPlanningResponse),
            description: "Structured section planning result for a research report",
            serializerOptions: jsonSerializerOptions);

        return new ChatResponseFormatJson(jsonElement);
    }

    public IReadOnlyList<SectionPlan> ToSectionPlans()
    {
        var result = new List<SectionPlan>();

        if (Sections is null || Sections.Count == 0)
            return result;

        foreach (var item in Sections)
        {
            if (string.IsNullOrWhiteSpace(item.Title))
                continue;

            result.Add(new SectionPlan
            {
                Title       = item.Title,
                Description = item.Description ?? string.Empty
            });
        }

        if (result.Count > 0)
        {
            result[^1].IsConclusion = true;
        }

        return result;
    }
}
public sealed class SectionPlanItem
{
    [Description("Short, informative title of the report section.")]
    public required string Title { get; init; }

    [Description("One or two sentences describing what this section should cover.")]
    public required string Description { get; init; }
}