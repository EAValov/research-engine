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
            if (item.Index <= 0) continue;
            if (string.IsNullOrWhiteSpace(item.Title)) continue;

            result.Add(new SectionPlan
            {
                Index       = item.Index,
                Title       = item.Title.Trim(),
                Description = item.Description?.Trim() ?? string.Empty,
                IsConclusion = item.IsConclusion
            });
        }

        // Enforce deterministic order and conclusion constraints
        result = result
            .OrderBy(s => s.Index)
            .ToList();

        result = EnforceSingleConclusionAtEndByIndex(result);

        return result;
    }

    private static List<SectionPlan> EnforceSingleConclusionAtEndByIndex(List<SectionPlan> plans)
    {
        if (plans.Count == 0)
            return plans;

        // Clear all
        foreach (var p in plans)
            p.IsConclusion = false;

        // Ensure LAST by Index is conclusion
        plans[^1].IsConclusion = true;

        // Re-number defensively to be contiguous 1..N (optional but helpful)
        for (var i = 0; i < plans.Count; i++)
            plans[i].Index = i + 1;

        return plans;
    }
}
public sealed class SectionPlanItem
{
    [Description("1-based section order index. Must be unique and increasing.")]
    public required int Index { get; init; }     

    [Description("Short, informative title of the report section.")]
    public required string Title { get; init; }

    [Description("One or two sentences describing what this section should cover.")]
    public required string Description { get; init; }

    [Description("True only for the LAST section (conclusion).")]
    public required bool IsConclusion { get; init; }
}