using System.Text;
using ResearchApi.Domain;

namespace ResearchApi.Prompts;

using System.Text;

public static class SectionPlanningPromptFactory
{
    /// <summary>
    /// Builds a prompt for planning report sections.
    ///
    /// If outline is provided, it must be treated as the authoritative structure:
    /// - Use the SAME number of sections, SAME order.
    /// - Use outline titles (minor normalization allowed) and fill descriptions.
    /// - The last section must be the conclusion (if outline last item isn't a conclusion, mark it as conclusion anyway).
    /// - Do NOT add extra sections.
    ///
    /// If outline is not provided, propose 3–7 sections with the final one as conclusion.
    /// </summary>
    public static Prompt BuildPlanningPrompt(
        string query,
        string? clarifications,
        string? instructions,
        string? targetLanguage = "en")
    {
        targetLanguage ??= "en";

        var systemSb = new StringBuilder();
        systemSb.AppendLine("You are an expert research planner.");
        systemSb.AppendLine("Your task is to design a clear, logical section structure for a report.");
        systemSb.AppendLine($"Plan the sections in language: {targetLanguage}.");
        systemSb.AppendLine();
        systemSb.AppendLine("You MUST respond using the structured JSON schema provided by the system.");
        systemSb.AppendLine("Do NOT include any extra keys not allowed by the schema.");
        systemSb.AppendLine();
        systemSb.AppendLine("Critical rules:");
        systemSb.AppendLine("- Output an ordered list of sections.");
        systemSb.AppendLine("- Each section has: index, title, description, isConclusion.");
        systemSb.AppendLine("- index MUST be 1-based, unique, and strictly increasing by 1.");
        systemSb.AppendLine("- Exactly ONE section must have isConclusion=true, and it MUST be the LAST section.");
        systemSb.AppendLine("- Section titles must be short and descriptive.");
        systemSb.AppendLine("- Descriptions must be 1–2 sentences.");
        systemSb.AppendLine();

        var systemPrompt = systemSb.ToString();

        var userSb = new StringBuilder();
        userSb.AppendLine("Main research query:");
        userSb.AppendLine(query.Trim());
        userSb.AppendLine();

        if (!string.IsNullOrWhiteSpace(clarifications))
        {
            userSb.AppendLine("Additional context / clarifications:");
            userSb.AppendLine(clarifications.Trim());
            userSb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(instructions))
        {
            userSb.AppendLine("Additional user instructions (must be followed):");
            userSb.AppendLine(instructions.Trim());
            userSb.AppendLine();
        }

        userSb.AppendLine("Task:");
        userSb.AppendLine("Propose 3–7 logical sections for a structured analytical report on this topic.");
        userSb.AppendLine("The LAST section must be the conclusion and have isConclusion=true.");
        
        userSb.AppendLine();
        userSb.AppendLine("Formatting rules:");
        userSb.AppendLine("- Output ONLY JSON that matches the schema.");
        userSb.AppendLine("- No Markdown, no prose outside the JSON.");
        userSb.AppendLine("- Do NOT include HTML tags or <references> blocks.");

        var userPrompt = userSb.ToString();

        return new Prompt(systemPrompt, userPrompt);
    }
}