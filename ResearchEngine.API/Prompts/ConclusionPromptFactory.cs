using System.Text;
using ResearchEngine.Domain;

namespace ResearchEngine.Prompts;

public static class ConclusionPromptFactory
{
    public static Prompt BuildConclusionPrompt(
        string query,
        string? clarifications,
        string targetLanguage,
        IReadOnlyList<SectionResult> sectionsWithSummaries)
    {
        var systemSb = new StringBuilder();
        systemSb.AppendLine("You are an expert research synthesizer.");
        systemSb.AppendLine("You will now write ONLY the final conclusion/summary section of a report.");
        systemSb.AppendLine("Base your conclusion on the provided section summaries.");
        systemSb.AppendLine("Do NOT introduce new sources or new citation numbers.");
        systemSb.AppendLine("Do NOT include the section title as a heading; start directly with the conclusion text.");
        systemSb.AppendLine("Preserve calibration: keep well-supported findings distinct from uncertain, source-dependent, or contested points.");
        systemSb.AppendLine("Do NOT turn forecasts, attributed claims, commentary, or disputed points into settled facts.");
        systemSb.AppendLine($"Write in: {targetLanguage}.");
        var systemPrompt = systemSb.ToString();

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

        userSb.AppendLine("Here are the sections and their key points:");
        userSb.AppendLine();

        foreach (var section in sectionsWithSummaries)
        {
            if (string.IsNullOrWhiteSpace(section.Summary))
                continue;

            userSb.AppendLine($"### {section.Plan.Title}");
            userSb.AppendLine(section.Summary.Trim());
            userSb.AppendLine();
        }

        userSb.AppendLine("Task:");
        userSb.AppendLine("- Write a single, coherent conclusion section answering the main query.");
        userSb.AppendLine("- Answer the query directly, but calibrate the wording to the strength of the evidence in the section summaries.");
        userSb.AppendLine("- Surface the strongest supported findings first.");
        userSb.AppendLine("- Explicitly mention major uncertainties, evidence limitations, and contested points when they affect the answer.");
        userSb.AppendLine("- Do NOT use a generic template or introduce topic framing that is not supported by the summaries.");
        userSb.AppendLine("- Do NOT add new [n] citations or new facts not present in the summaries.");
        
        var userPrompt = userSb.ToString();

        return new Prompt(systemPrompt, userPrompt);
    }
}
