using System.Text;
using ResearchApi.Domain;

namespace ResearchApi.Prompts;

public static class SelectBreadthDepthPromptFactory
{
    public static Prompt Build (string query, IReadOnlyList<Clarification> clarifications)
    {
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

        return new Prompt(GetSystemPrompt(), sb.ToString());
    }

    private static string GetSystemPrompt()
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are configuring parameters for a deep web research engine.");
        sb.AppendLine("Your job is to pick a sensible breadth (1-8) and depth (1-4) for the research.");
        sb.AppendLine("- Breadth = how many distinct directions / subtopics to explore.");
        sb.AppendLine("- Depth   = how multi-step and detailed the reasoning and reading should be.");
        sb.AppendLine("Use the user's question and their clarifications to pick values.");
        sb.AppendLine("If the user wants a quick, high-level overview, use lower depth.");
        sb.AppendLine("If the user wants detailed, thorough analysis, use higher depth.");
        sb.AppendLine("If the user wants many perspectives, use higher breadth.");
        sb.AppendLine("Respond ONLY with a single JSON object and nothing else.");

        return sb.ToString();
    }
}
 
 