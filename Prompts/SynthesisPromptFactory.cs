using System.Text;

namespace ResearchApi.Prompts;

public static class SynthesisPromptFactory
{
    /// <summary>
    /// Builds a synthesis prompt to write the final report from all accumulated learnings.
    /// Clarifications are optional; learnings are expected as pre-formatted blocks.
    /// </summary>
    public static Prompt Build(
        string query,
        string learningsBlock,
        string? clarificationsText = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Using the provided learnings from web research, write a thorough, well-structured report");
        sb.AppendLine("that answers the user's query as completely and accurately as possible.");
        sb.AppendLine();
        sb.AppendLine("You MUST follow these guidelines:");
        sb.AppendLine("- The report should be detailed and structured, suitable as a briefing or whitepaper.");
        sb.AppendLine("- Use clear headings and subheadings (Markdown format is preferred).");
        sb.AppendLine("- Integrate ALL of the provided learnings where relevant.");
        sb.AppendLine("- Reconcile any contradictions and note uncertainty explicitly.");
        sb.AppendLine("- Where helpful, use bullet points, tables, or short lists for clarity.");
        sb.AppendLine("- Do NOT fabricate sources or data beyond the provided learnings.");
        sb.AppendLine();

        sb.AppendLine("The original user query is:");
        sb.AppendLine($"<prompt>{query}</prompt>");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(clarificationsText))
        {
            sb.AppendLine("Here are clarifications from the user (questions and answers) that indicate");
            sb.AppendLine("their constraints, preferences, or what they care about most:");
            sb.AppendLine("<clarifications>");
            sb.AppendLine(clarificationsText.Trim());
            sb.AppendLine("</clarifications>");
            sb.AppendLine("Use these clarifications to choose emphasis, examples, and trade-offs in the report.");
            sb.AppendLine();
        }

        sb.AppendLine("Here are all the learnings from previous research steps:");
        sb.AppendLine("<learnings>");
        sb.AppendLine(learningsBlock);
        sb.AppendLine("</learnings>");
        sb.AppendLine();
        sb.AppendLine("Now write the final report in Markdown. Include an executive summary at the top,");
        sb.AppendLine("followed by detailed sections. Do not include the raw <learnings> block in the output.");

        return new Prompt(GetSystemPrompt(), sb.ToString());
    }

    private static string GetSystemPrompt()
    {
        var dt = DateTime.UtcNow.ToString("s", System.Globalization.CultureInfo.InvariantCulture);
        var sb = new StringBuilder();

        sb.AppendLine($"You are an expert research synthesizer. Today is {dt} (UTC).");
        sb.AppendLine("You receive structured learnings and/or excerpts from multiple sources and must synthesize them into a coherent, rigorous analysis.");
        sb.AppendLine();
        sb.AppendLine("Audience & depth:");
        sb.AppendLine("- The user is a highly experienced analyst; write at a professional / expert level.");
        sb.AppendLine("- Do NOT oversimplify; it is acceptable (and preferred) to be detailed and technical.");
        sb.AppendLine();
        sb.AppendLine("Core principles:");
        sb.AppendLine("- Accuracy is critical: do not fabricate data, numbers, or citations.");
        sb.AppendLine("- Distinguish clearly between:");
        sb.AppendLine("  - established facts from the sources,");
        sb.AppendLine("  - well-supported inferences, and");
        sb.AppendLine("  - speculation or forward-looking scenarios.");
        sb.AppendLine("- When you speculate or forecast, explicitly label it (e.g., 'Speculation:' or 'Forecast:').");
        sb.AppendLine("- When multiple sources disagree, surface the disagreement, explain possible reasons, and, if possible, assess which is more credible.");
        sb.AppendLine("- Preserve important metrics, dates, and legal references exactly as given in the learnings.");
        sb.AppendLine();
        sb.AppendLine("Use of sources and structure:");
        sb.AppendLine("- Integrate information across sources; avoid just listing them one by one.");
        sb.AppendLine("- Highlight edge cases, regulatory nuances, and second-order effects that a senior analyst would care about.");
        sb.AppendLine("- Be well-organized: use sections, subsections, and, where appropriate, tables or bullet lists (unless the task prompt forbids it).");
        sb.AppendLine();
        sb.AppendLine("Format discipline:");
        sb.AppendLine("- Always obey the local task instructions for the final answer: if the user wants a memo, slide-style bullets, or a specific structure, follow that.");
        sb.AppendLine("- Do NOT output JSON or machine formats unless explicitly requested for this stage.");

        return sb.ToString();
    }
}