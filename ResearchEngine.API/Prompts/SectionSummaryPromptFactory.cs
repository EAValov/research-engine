using System.Text;
using ResearchEngine.Domain;

namespace ResearchEngine.Prompts;

public static class SectionSummaryPromptFactory
{
    public static Prompt BuildSummaryPrompt(
        string sectionTitle,
        string sectionText,
        string targetLanguage)
    {
        var systemSb = new StringBuilder();
        systemSb.AppendLine("You are an expert summarizer.");
        systemSb.AppendLine("You will receive the text of one report section and must extract key points without losing calibration, attribution, or uncertainty.");
        systemSb.AppendLine($"Write the summary in: {targetLanguage}.");
        var systemPrompt = systemSb.ToString();

        var userSb = new StringBuilder();
        userSb.AppendLine($"Section title: {sectionTitle}");
        userSb.AppendLine();
        userSb.AppendLine("Section text:");
        userSb.AppendLine(sectionText.Trim());
        userSb.AppendLine();
        userSb.AppendLine("Task:");
        userSb.AppendLine("- Write a concise calibration-preserving summary in Markdown using these optional labels when relevant:");
        userSb.AppendLine("  Supported findings:");
        userSb.AppendLine("  Uncertainties and limits:");
        userSb.AppendLine("  Contested or source-dependent points:");
        userSb.AppendLine("- Under each used label, provide 1-3 bullet points.");
        userSb.AppendLine("- Preserve modality and attribution. Keep language like 'suggests', 'claims', 'projects', or 'is disputed' when that is what the section supports.");
        userSb.AppendLine("- Do NOT add new information; only compress what is already present.");
        userSb.AppendLine("- You may omit citation markers [lrn:...] in the summary.");
        
        var userPrompt = userSb.ToString();

        return new Prompt(systemPrompt, userPrompt);
    }
}
