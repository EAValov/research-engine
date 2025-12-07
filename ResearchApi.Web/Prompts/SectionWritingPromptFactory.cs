using System.Text;
using ResearchApi.Domain;

namespace ResearchApi.Prompts;

public static class SectionWritingPromptFactory
{
    public static Prompt BuildSectionPrompt(
        string query,
        string? clarifications,
        string targetLanguage,
        SectionPlan section)
    {
        var systemPrompt = SynthesisSystemPromptFactory.BuildSystemPrompt(targetLanguage);

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

        userSb.AppendLine("You are now writing ONE section of the full report.");
        userSb.AppendLine();
        userSb.AppendLine("Section specification:");
        userSb.AppendLine($"- Title: {section.Title}");
        userSb.AppendLine($"- Scope: {section.Description}");
        userSb.AppendLine();
        userSb.AppendLine("Constraints for this section:");
        userSb.AppendLine("- Focus ONLY on the scope of this section; do not write other sections.");
        userSb.AppendLine("- DO NOT include the section title as a Markdown heading.");
        userSb.AppendLine("  The caller will add the section heading separately.");
        userSb.AppendLine("- Start directly with the body text (paragraphs, lists, tables, etc.).");
        userSb.AppendLine("- Before or while drafting this section, you MUST call `get_similar_learnings`");
        userSb.AppendLine("  at least once with a focused query relevant to this section.");
        userSb.AppendLine("- Use the evidence returned by the tool and preserve [n] citation markers.");
        userSb.AppendLine("- Do NOT add your own citation numbers.");
        userSb.AppendLine("- Do NOT write the conclusion for the whole report here; only this section.");

        var userPrompt = userSb.ToString();

        return new Prompt(systemPrompt, userPrompt);
    }
}
