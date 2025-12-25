using System.Globalization;
using System.Text;
using ResearchApi.Domain;

namespace ResearchApi.Prompts;

using System.Globalization;
using System.Text;

public static class SectionWritingPromptFactory
{
    /// <summary>
    /// Builds a prompt for writing ONE section body.
    /// </summary>
    public static Prompt BuildSectionPrompt(
        string query,
        string? clarifications,
        string targetLanguage,
        SectionPlan section,
        string? instructions)
    {
        targetLanguage ??= "en";

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

        if (!string.IsNullOrWhiteSpace(instructions))
        {
            userSb.AppendLine("Additional user instructions (AUTHORITATIVE):");
            userSb.AppendLine(instructions.Trim());
            userSb.AppendLine();
        }

        userSb.AppendLine("You are now writing ONE section of the full report.");
        userSb.AppendLine();
        userSb.AppendLine("Section specification (write ONLY this section):");
        userSb.AppendLine($"- Title: {section.Title}");
        userSb.AppendLine($"- Scope: {section.Description}");
        userSb.AppendLine();

        userSb.AppendLine("SECTION-LEVEL CONSTRAINTS:");
        userSb.AppendLine("- Focus ONLY on the scope of this section; do not write other sections.");
        userSb.AppendLine("- DO NOT include the section title as a Markdown heading.");
        userSb.AppendLine("  The caller will add the section heading separately.");
        userSb.AppendLine("- Start directly with the body text.");
        userSb.AppendLine("- Do NOT include a global conclusion for the whole report here.");
        userSb.AppendLine();

        userSb.AppendLine("STRUCTURE AND FORMATTING:");
        userSb.AppendLine("- You MAY use paragraphs, bullet lists, and tables.");
        userSb.AppendLine("- Use tables only when they add value (comparisons, metrics, frameworks).");
        userSb.AppendLine("- For simple points or short lists, bullets are often better than tables.");
        userSb.AppendLine("- You MAY include a Mermaid diagram ONLY if it clarifies a process/architecture:");
        userSb.AppendLine("  ```mermaid");
        userSb.AppendLine("  ...diagram definition...");
        userSb.AppendLine("  ```");
        userSb.AppendLine("- Mermaid node labels like A[Label] must avoid parentheses (), commas, and complex punctuation.");
        userSb.AppendLine();

        userSb.AppendLine("TOOL AND EVIDENCE USE (MANDATORY):");
        userSb.AppendLine("- BEFORE writing any substantive content, you MUST call `get_similar_learnings` at least once");
        userSb.AppendLine("  with a focused query that is specific to THIS section.");
        userSb.AppendLine("- You MAY call it multiple times for distinct sub-aspects within this section.");
        userSb.AppendLine("- The tool returns evidence snippets that contain citation tokens like: [lrn:2f0d...].");
        userSb.AppendLine("- Preserve these [lrn:...] citation tokens EXACTLY as provided.");
        userSb.AppendLine("- Do NOT invent citations and do NOT rewrite tokens.");
        userSb.AppendLine("- This section must contain at least ONE citation token like [lrn:0123abcd...].");
        userSb.AppendLine("- Do NOT output the tool call results verbatim; synthesize them into prose.");
        userSb.AppendLine();

        userSb.AppendLine("CITATION STYLE (CRITICAL):");
        userSb.AppendLine("- Citations must appear ONLY as [lrn:<32-hex>] tokens, e.g. [lrn:6f5c1a2b3c4d5e6f7a8b9c0d1e2f3a4b].");
        userSb.AppendLine("- If multiple, separate with commas/spaces: [lrn:...], [lrn:...].");
        userSb.AppendLine("- Never use chained citations like [lrn:...][lrn:...] (forbidden).");

        var userPrompt = userSb.ToString();
        return new Prompt(BuildSystemPrompt(targetLanguage), userPrompt);
    }

    private static string BuildSystemPrompt(string? targetLanguage = "en")
    {
        var dt = DateTime.UtcNow.ToString("s", CultureInfo.InvariantCulture);
        var sb = new StringBuilder();

        sb.AppendLine($"You are an expert research synthesizer. Today is {dt} (UTC).");
        sb.AppendLine("Your task is to write structured, well-supported analytical text for exactly one report section.");
        sb.AppendLine();

        sb.AppendLine("HARD OUTPUT RULES:");
        sb.AppendLine("- Output MUST be valid GitHub-Flavored Markdown.");
        sb.AppendLine("- Do NOT use any HTML tags.");
        sb.AppendLine("- Do NOT output code fences except Mermaid diagrams using ```mermaid ... ```.");
        sb.AppendLine("- Do NOT include the section title as a heading.");
        sb.AppendLine("- Do NOT include content for other sections.");
        sb.AppendLine();

        sb.AppendLine("TOOL USAGE (`get_similar_learnings`) IS MANDATORY:");
        sb.AppendLine("- You MUST call `get_similar_learnings` at least once before writing substantive content.");
        sb.AppendLine("- The tool returns evidence snippets that include citation tokens like [lrn:...].");
        sb.AppendLine("- Preserve citation tokens EXACTLY; do not invent, renumber, or rewrite them.");
        sb.AppendLine("- If you cannot find evidence for a claim, phrase it as uncertainty or omit it.");
        sb.AppendLine();

        sb.AppendLine("CITATIONS (CRITICAL):");
        sb.AppendLine("- Citations must be ONLY in the form [lrn:<32-hex-guid>]. Example: [lrn:0123abcd0123abcd0123abcd0123abcd].");
        sb.AppendLine("- Never output chained citations like [lrn:...][lrn:...]; always separate: [lrn:...], [lrn:...].");
        sb.AppendLine("- The final section text MUST include at least one [lrn:...] citation token.");
        sb.AppendLine();

        sb.AppendLine("SOURCE QUALITY:");
        sb.AppendLine("- Prefer authoritative sources when weighing evidence.");
        sb.AppendLine("- If evidence conflicts, mention the disagreement explicitly.");
        sb.AppendLine("- Do not present speculative tech as mature without strong support.");
        sb.AppendLine();

        sb.AppendLine($"Write the final text in: {targetLanguage ?? "en"}.");
        return sb.ToString();
    }
}