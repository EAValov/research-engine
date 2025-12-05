using System.Text;
using ResearchApi.Infrastructure;
using ResearchApi.Prompts;

public static class SectionPlanningPromptFactory
{
    public static Prompt BuildPlanningPrompt(
        string query,
        string? clarifications,
        string? targetLanguage = "en")
    {
        var systemSb = new StringBuilder();
        systemSb.AppendLine("You are an expert research planner.");
        systemSb.AppendLine("Your task is to design a clear, logical section structure for a report.");
        systemSb.AppendLine($"Plan the sections in language: {targetLanguage ?? "en"}.");
        systemSb.AppendLine();
        systemSb.AppendLine("You will respond in a structured JSON format provided by the system,");
        systemSb.AppendLine("filling in an ordered list of sections with titles and descriptions.");

        var systemPrompt = systemSb.ToString();

        var userSb = new StringBuilder();
        userSb.AppendLine("Main research query:");
        userSb.AppendLine(query.Trim());
        userSb.AppendLine();

        if (!string.IsNullOrWhiteSpace(clarifications))
        {
            userSb.AppendLine("Additional clarifications:");
            userSb.AppendLine(clarifications.Trim());
            userSb.AppendLine();
        }

        userSb.AppendLine("Tasks:");
        userSb.AppendLine("1. Propose 3–7 logical sections for a structured analytical report on this topic.");
        userSb.AppendLine("2. For each section, provide:");
        userSb.AppendLine("   - a short, informative title;");
        userSb.AppendLine("   - 1–2 sentences describing what should be covered.");
        userSb.AppendLine("3. The FINAL section should serve as a conclusion/summary.");
        userSb.AppendLine("4. The system will enforce a JSON schema; you only need to fill it logically.");
        userSb.AppendLine();
        userSb.AppendLine("Use plain Markdown paragraphs, lists, and headings.");
        userSb.AppendLine("Do NOT add any HTML tags or <references> blocks.");

        var userPrompt = userSb.ToString();

        return new Prompt(systemPrompt, userPrompt);
    }

    public static IReadOnlyList<SectionPlan> ToSectionPlans(SectionPlanningResponse response)
    {
        var result = new List<SectionPlan>();

        if (response.Sections is null || response.Sections.Count == 0)
            return result;

        foreach (var item in response.Sections)
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
