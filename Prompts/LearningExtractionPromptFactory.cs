using System.Text;

namespace ResearchApi.Prompts;

public static class LearningExtractionPromptFactory
{
    /// <summary>
    /// Builds a prompt to extract dense learnings from fetched page content for a given query.
    /// Clarifications are optional and will be used as extra context if present.
    /// </summary>
    public static Prompt Build(
        string query,
        string content,
        string? clarificationsText = null,
        int? maxLearnings = null,
        string? targetLanguage = "en")
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

        sb.AppendLine("When extracting legal or regulatory penalties (fines, percentages of turnover, etc.):");
        sb.AppendLine("- Only extract a specific fine or percentage if it is clearly tied in the text to a particular law, article, or official guideline.");
        sb.AppendLine("- If the text presents speculative or proposed penalties (e.g., future drafts, hypothetical scenarios), describe them qualitatively (\"some proposals suggest higher fines\") rather than as current, precise law.");
        sb.AppendLine("- If multiple conflicting numbers are given for the same penalty, either omit them or mention the existence of conflicting figures without choosing one as definitive.");
        sb.AppendLine("- If you are not confident that a numeric detail is precisely supported by the text, omit the number rather than guessing.");
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
        sb.AppendLine($"Always write extracted learnings IN {targetLanguage}.");
        sb.AppendLine("The content may be in another language; translate implicitly if necessary.");
        sb.AppendLine("Return only the learnings, one per line, with no numbering.");
        sb.AppendLine();
        sb.AppendLine("Return ONLY the final learnings as a simple list, one per line.");
        sb.AppendLine("Do NOT add numbering, bullets, headings, labels, or any extra commentary.");
        sb.AppendLine("Do NOT include any citation tags like [1], [2] in your response - just the raw learning text.");

        return new Prompt(GetSystemPrompt(), sb.ToString());
    }

    private static string GetSystemPrompt()
    {
        var dt = DateTime.UtcNow.ToString("s", System.Globalization.CultureInfo.InvariantCulture);
        var sb = new StringBuilder();

        sb.AppendLine($"You are an expert analyst extracting structured learnings from web pages. Today is {dt} (UTC).");
        sb.AppendLine("Your job is NOT to summarize everything, but to cherry-pick the most important, concrete information that helps answer the research query.");
        sb.AppendLine();
        sb.AppendLine("Core principles:");
        sb.AppendLine("- The user is an expert; focus on high-signal, domain-specific insights, not generic explanations.");
        sb.AppendLine("- Prioritize information that is directly relevant to the original research query and clarifications.");
        sb.AppendLine("- Prefer concrete facts, definitions, quantitative estimates, regulatory requirements, and clear consensus views.");
        sb.AppendLine("- Preserve important metrics, numbers, dates, and legal references EXACTLY as written when you are confident they are correctly stated.");
        sb.AppendLine("- If a number or legal detail looks uncertain, speculative, or inconsistent within the text, omit it rather than repeating a potentially incorrect figure.");
        sb.AppendLine("- If large parts of the content are off-topic, ignore them instead of forcing vague or generic learnings.");
        sb.AppendLine("- If the content provides nothing useful for the query, you may return fewer learnings or even none (if allowed by the task prompt).");
        sb.AppendLine();
        sb.AppendLine("Source handling:");
        sb.AppendLine("- You are not browsing yourself; trust the provided content as the page text, but do NOT add anything that is not explicitly supported.");
        sb.AppendLine("- Do NOT invent facts that are not supported by the content.");
        sb.AppendLine("- For sensitive legal or financial penalties, be especially conservative: when in doubt, prefer omission over potentially wrong precision.");
        sb.AppendLine();
        sb.AppendLine("Format discipline:");
        sb.AppendLine("- Always obey the local task instructions: if the prompt asks for 'one learning per line, no numbering', follow that exactly.");
        sb.AppendLine("- Do NOT add headings, explanations, or commentary beyond what the task requires.");

        return sb.ToString();
    }
}