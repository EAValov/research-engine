using System.Text;
using ResearchApi.Domain;

public static class SelectBreadthDepthPromptFactory
{
    public static (string systemPrompt, string userPrompt) Build (string query, IReadOnlyList<Clarification> clarifications)
    {
        var systemPrompt =
            "You are configuring parameters for a deep web research engine.\n" +
            "Your job is to pick a sensible breadth (1-8) and depth (1-4) for the research.\n" +
            "- Breadth = how many distinct directions / subtopics to explore.\n" +
            "- Depth   = how multi-step and detailed the reasoning and reading should be.\n" +
            "Use the user's question and their clarifications to pick values.\n" +
            "If the user wants a quick, high-level overview, use lower depth.\n" +
            "If the user wants detailed, thorough analysis, use higher depth.\n" +
            "If the user wants many perspectives, use higher breadth.\n" +
            "Respond ONLY with a single JSON object and nothing else.";

        var sb = new StringBuilder();
        sb.AppendLine("User question:");
        sb.AppendLine(query);
        sb.AppendLine();
        sb.AppendLine("Clarifications:");
        if (clarifications.Count == 0)
        {
            sb.AppendLine("(none)");
        }
        else
        {
            for (int i = 0; i < clarifications.Count; i++)
            {
                sb.AppendLine($"Q{i + 1}: {clarifications[i].Question}");
                sb.AppendLine($"A{i + 1}: {clarifications[i].Answer}");
                sb.AppendLine();
            }
        }

        sb.AppendLine();
        sb.AppendLine("Now respond with JSON like:");
        sb.AppendLine(@"{""breadth"":3,""depth"":2}");

        return (systemPrompt, sb.ToString());
    }
}
 
 