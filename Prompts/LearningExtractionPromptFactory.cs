using System.Text;

namespace ResearchApi.Prompts;

public static class LearningExtractionPromptFactory
{
    /// <summary>
    /// Builds a prompt to extract dense learnings from fetched page content for a given query.
    /// Clarifications are optional and will be used as extra context if present.
    /// </summary>
    public static string Build(
    string query,
    string content,
    string? clarificationsText = null,
    int? maxLearnings = null)
    {
        var effectiveMaxLearnings = maxLearnings is > 0 ? maxLearnings.Value : 3;

        var sb = new StringBuilder();
        sb.AppendLine("Your task is to read the provided content and extract concise, information-dense LEARNINGS that are directly useful for the research query.");
        sb.AppendLine();
        sb.AppendLine("You are NOT summarizing the whole text; you are cherry-picking the most valuable, concrete pieces of information.");
        sb.AppendLine();
        sb.AppendLine("You MUST follow these rules:");
        sb.AppendLine($"- Return up to {effectiveMaxLearnings} learnings (use fewer if the content is repetitive, low-signal, or off-topic).");
        sb.AppendLine("- Each learning must be a single, self-contained sentence or very short paragraph.");
        sb.AppendLine("- Each learning must make sense on its own without referring to “this article”, “the author”, or pronouns like “it/they/this”.");
        sb.AppendLine("- Make each learning as SPECIFIC and information-dense as possible.");
        sb.AppendLine("- Whenever available, preserve important metrics, numbers, percentages, time ranges, and dates EXACTLY as written.");
        sb.AppendLine("- Include key domain entities (e.g., regulations, organizations, products, regions, technologies) when they are relevant.");
        sb.AppendLine("- Strongly prefer concrete facts, definitions, quantitative estimates, or clearly stated consensus views over vague commentary or marketing fluff.");
        sb.AppendLine("- Avoid restating the same idea in different words; each learning should add NEW information.");
        sb.AppendLine("- If large portions of the content are unrelated to the query, IGNORE them rather than extracting generic or off-topic learnings.");
        sb.AppendLine();

        sb.AppendLine("The original research query is:");
        sb.AppendLine($"<query>{query}</query>");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(clarificationsText))
        {
            sb.AppendLine("Here is additional context from clarifications that indicate what matters most to the user:");
            sb.AppendLine("<clarifications>");
            sb.AppendLine(clarificationsText.Trim());
            sb.AppendLine("</clarifications>");
            sb.AppendLine("When choosing what to extract, prioritize information that most directly helps answer the query given this context.");
            sb.AppendLine("If the content discusses multiple topics, focus ONLY on the parts that match the query and clarifications.");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("No additional clarifications were provided.");
            sb.AppendLine("Infer what is most relevant from the query itself, and deprioritize tangential topics.");
            sb.AppendLine();
        }

        sb.AppendLine("Here is the content retrieved from SERP results:");
        sb.AppendLine("<contents>");
        sb.AppendLine(content);
        sb.AppendLine("</contents>");
        sb.AppendLine();
        sb.AppendLine("Return ONLY the final learnings as a simple list, one per line.");
        sb.AppendLine("Do NOT add numbering, bullets, headings, labels, or any extra commentary.");

        return sb.ToString();
    }
}