using System.Text;
using ResearchApi.Domain;

public static class SectionUnifiedRepairPromptFactory
{
    public static Prompt BuildRepairPrompt(
        string targetLanguage,
        SectionPlan section,
        string badDraft)
    {
        var system = $"You are fixing a Markdown report section in {targetLanguage}. Output ONLY the corrected Markdown.";

        var user = new StringBuilder();
        user.AppendLine("You will be given a draft for ONE report section that violates validation rules.");
        user.AppendLine("Fix it so it passes ALL rules below, while keeping it within the section scope.");
        user.AppendLine();
        user.AppendLine("SECTION CONTEXT:");
        user.AppendLine($"- Section title: {section.Title}");
        user.AppendLine($"- Section scope: {section.Description}");
        user.AppendLine();
        user.AppendLine("REPAIR RULES (ALL MUST PASS):");
        user.AppendLine("1) Citations are REQUIRED:");
        user.AppendLine("   - The section must contain at least ONE citation marker like [1].");
        user.AppendLine("   - Do NOT invent new citation numbers.");
        user.AppendLine("   - Preserve existing citation markers exactly as they appear.");
        user.AppendLine("   - Forbidden: chained citations like [1][2]. Use: [1], [2].");
        user.AppendLine();
        user.AppendLine("2) Mermaid diagrams must be valid (if present):");
        user.AppendLine("   - Mermaid blocks are allowed only as fenced code blocks: ```mermaid ... ```");
        user.AppendLine("   - In Mermaid node labels like A[Label], REMOVE parentheses '(' and ')' from labels.");
        user.AppendLine("   - Keep the overall diagram structure (nodes/edges) the same when possible.");
        user.AppendLine("   - If you cannot confidently fix a Mermaid diagram, REMOVE the entire Mermaid block.");
        user.AppendLine();
        user.AppendLine("3) Output formatting:");
        user.AppendLine("   - Output ONLY the section body (no headings, no title line).");
        user.AppendLine("   - Valid GitHub-Flavored Markdown only.");
        user.AppendLine("   - No HTML tags.");
        user.AppendLine();
        user.AppendLine("4) Scope:");
        user.AppendLine("   - Do NOT add content for other sections.");
        user.AppendLine("   - Do NOT add a global conclusion.");
        user.AppendLine();
        user.AppendLine("DRAFT TO REPAIR:");
        user.AppendLine(badDraft);

        return new Prompt(system, user.ToString());
    }
}
