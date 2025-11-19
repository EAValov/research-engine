using System.Text;

namespace ResearchApi.Prompts;

public static class SynthesisPromptFactory
{
    /// <summary>
    /// Builds a synthesis prompt to write the final report from all accumulated learnings.
    /// Clarifications are optional; learnings are expected as pre-formatted blocks.
    /// </summary>
    public static string Build(
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

        return sb.ToString();
    }
}