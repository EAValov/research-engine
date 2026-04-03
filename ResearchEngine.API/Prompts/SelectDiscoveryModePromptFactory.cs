using System.Text;
using ResearchEngine.Domain;

namespace ResearchEngine.Prompts;

public static class SelectDiscoveryModePromptFactory
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
            for (var i = 0; i < clarifications.Count; i++)
            {
                sb.AppendLine($"Q{i + 1}: {clarifications[i].Question}");
                sb.AppendLine($"A{i + 1}: {clarifications[i].Answer}");
                sb.AppendLine();
            }
        }

        sb.AppendLine("You will respond in the structured JSON schema provided by the system.");
        sb.AppendLine("- Choose exactly one discoveryMode value: Balanced, ReliableOnly, or AcademicOnly.");
        sb.AppendLine("- Balanced is for mixed web research and general discovery.");
        sb.AppendLine("- ReliableOnly is for queries that depend heavily on official, institutional, or otherwise high-trust sources.");
        sb.AppendLine("- AcademicOnly is for literature-style or paper-first research where journals, preprints, and academic material should dominate.");
        sb.AppendLine("- Output only the JSON payload required by the system, with no extra text.");

        return new Prompt(GetSystemPrompt(), sb.ToString());
    }

    private static string GetSystemPrompt()
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are configuring a deep web research engine.");
        sb.AppendLine("Your task is to choose the most appropriate source discovery mode for the user's research question.");
        sb.AppendLine();
        sb.AppendLine("Mode guidance:");
        sb.AppendLine("- Balanced: default for most broad product, market, technical, and current-events research.");
        sb.AppendLine("- ReliableOnly: use when the answer should lean on official documentation, government pages, standards bodies, academic institutions, journals, or other high-trust primary sources.");
        sb.AppendLine("- AcademicOnly: use when the question is primarily scholarly, literature-review oriented, or explicitly asks for papers, journals, preprints, or research-only evidence.");
        sb.AppendLine();
        sb.AppendLine("Selection rules:");
        sb.AppendLine("- If the user explicitly asks for papers, journals, studies, literature review, or academic-only evidence, choose AcademicOnly.");
        sb.AppendLine("- If the user explicitly asks for only reliable, official, authoritative, or primary sources, choose ReliableOnly.");
        sb.AppendLine("- If the query is technical and compares protocols, APIs, standards, laws, regulations, or official product behavior, prefer ReliableOnly.");
        sb.AppendLine("- Otherwise choose Balanced.");
        sb.AppendLine();
        sb.AppendLine("Final output rules:");
        sb.AppendLine("- The system defines the exact JSON schema for your response.");
        sb.AppendLine("- You MUST output only that JSON structure, with no commentary or additional fields.");

        return sb.ToString();
    }
}
