using System.Text;
using ResearchApi.Domain;

namespace ResearchApi.Prompts;

public static class SectionSummaryPromptFactory
{
    public static Prompt BuildSummaryPrompt(
        string sectionTitle,
        string sectionText,
        string targetLanguage)
    {
        var systemSb = new StringBuilder();
        systemSb.AppendLine("You are an expert summarizer.");
        systemSb.AppendLine("You will receive the text of one report section and must extract key points.");
        systemSb.AppendLine($"Write the summary in: {targetLanguage}.");
        var systemPrompt = systemSb.ToString();

        var userSb = new StringBuilder();
        userSb.AppendLine($"Section title: {sectionTitle}");
        userSb.AppendLine();
        userSb.AppendLine("Section text:");
        userSb.AppendLine(sectionText.Trim());
        userSb.AppendLine();
        userSb.AppendLine("Task:");
        userSb.AppendLine("- Provide 2–4 bullet points capturing the most important conclusions of this section.");
        userSb.AppendLine("- Do NOT add new information; only rephrase what is present.");
        userSb.AppendLine("- You may omit citation markers [n] in the summary.");
        
        var userPrompt = userSb.ToString();

        return new Prompt(systemPrompt, userPrompt);
    }
}
