using System.Text;
using ResearchApi.Domain;

namespace ResearchApi.Prompts;

public static class SelectBreadthDepthPromptFactory
{
    public static Prompt Build(string query, IReadOnlyList<Clarification> clarifications)
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
        sb.AppendLine("You will respond in a structured JSON format provided by the system.");
        sb.AppendLine("- Select integer values for breadth and depth within the allowed ranges.");
        sb.AppendLine("- Output only the JSON payload required by the system, with no extra text.");

        return new Prompt(GetSystemPrompt(), sb.ToString());
    }

    private static string GetSystemPrompt()
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are configuring parameters for a deep web research engine.");
        sb.AppendLine("Your job is to pick a sensible breadth (1–8) and depth (1–4) for the research.");
        sb.AppendLine();
        sb.AppendLine("Definitions:");
        sb.AppendLine("- Breadth = how many distinct directions / subtopics to explore.");
        sb.AppendLine("  - 1–2: very focused, only the core aspects.");
        sb.AppendLine("  - 3–5: moderate coverage, several important angles.");
        sb.AppendLine("  - 6–8: wide coverage, many perspectives and edge cases.");
        sb.AppendLine("- Depth   = how multi-step and detailed the research and reasoning should be.");
        sb.AppendLine("  - 1: quick, high-level overview.");
        sb.AppendLine("  - 2–3: balanced, solid detail without being exhaustive.");
        sb.AppendLine("  - 4: very detailed, thorough, multi-step investigation.");
        sb.AppendLine();
        sb.AppendLine("Use the user's question and their clarifications to pick values.");
        sb.AppendLine("- If the user wants a quick, high-level overview, prefer lower depth (1–2) and lower breadth (1–3).");
        sb.AppendLine("- If the user wants detailed, thorough analysis, prefer higher depth (3–4).");
        sb.AppendLine("- If the user wants many perspectives or to “explore the space”, prefer higher breadth (4–8).");
        sb.AppendLine();
        sb.AppendLine("Explicit user instructions override your heuristics:");
        sb.AppendLine("- If the user explicitly specifies breadth and/or depth in plain text (e.g. \"use depth 4\",");
        sb.AppendLine("  \"depth = 3, breadth = 5\", \"breadth 6\"), you MUST respect those numeric values as much as possible.");
        sb.AppendLine("- Map any explicitly requested values to the allowed ranges:");
        sb.AppendLine("  - breadth: 1–8, depth: 1–4 (e.g. requested depth 10 => use depth 4).");
        sb.AppendLine();
        sb.AppendLine("Final output rules:");
        sb.AppendLine("- The system defines the exact JSON schema for your response.");
        sb.AppendLine("- You MUST output only that JSON structure, with no commentary or additional fields.");

        return sb.ToString();
    }
}
 
 