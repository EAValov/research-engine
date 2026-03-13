using System.Text;
using ResearchEngine.Domain;

namespace ResearchEngine.Prompts;

public static class FeedbackPromptFactory
{
    /// <summary>
    /// Builds a prompt to generate clarification questions for the given research query.
    /// </summary>
    public static Prompt Build(string query, bool includeBreadthDepthQuestions = false)
    {
        const int MaxQuestions = 5;

        var sb = new StringBuilder();

        sb.AppendLine($"Given the following research query, ask up to {MaxQuestions} clarification questions that help");
        sb.AppendLine("disambiguate the user's intent, scope, constraints, or assumptions.");
        sb.AppendLine("Ask ONLY questions the user must answer, not questions that require external research.");
        sb.AppendLine();
        sb.AppendLine("The user's research query is:");
        sb.AppendLine($"<query>{query}</query>");
        sb.AppendLine();

        if (includeBreadthDepthQuestions)
        {
            sb.AppendLine("In addition, at the END of the list, include questions that explicitly ask:");
            sb.AppendLine("- how BROAD vs NARROW the user wants the research to be (many directions vs focused),");
            sb.AppendLine("- how DEEP vs QUICK they want the analysis (high-level vs very detailed).");
            sb.AppendLine("If you cannot fit everything within the maximum number of questions, prioritize:");
            sb.AppendLine("1) the most critical clarifications for understanding the task, then");
            sb.AppendLine("2) breadth/depth preference questions at the end.");
            sb.AppendLine();
        }

        sb.AppendLine("You will respond in a structured JSON format provided by the system.");
        sb.AppendLine("- Include each clarification question as a separate item in the collection.");
        sb.AppendLine("- Questions must be written in natural language.");
        sb.AppendLine("- Output only the JSON payload required by the system, with no extra text or formatting.");

        return new Prompt(GetSystemPrompt(), sb.ToString());
    }

    private static string GetSystemPrompt()
    {
        var dt = DateTime.UtcNow.ToString("s", System.Globalization.CultureInfo.InvariantCulture);
        var sb = new StringBuilder();

        sb.AppendLine($"You are an expert research-scoping assistant. Today is {dt}.");
        sb.AppendLine("Your ONLY task is to ask clarification questions that narrow or refine the user's research intent.");
        sb.AppendLine();
        sb.AppendLine("You MUST follow these rules:");
        sb.AppendLine("- Ask only about things the USER must decide: scope, region, audience, use-case, business model, data types,");
        sb.AppendLine("  regulatory classification, depth of analysis, constraints (time, budget, data availability, etc.).");
        sb.AppendLine("- NEVER ask questions that are themselves research tasks or require technical/regulatory analysis.");
        sb.AppendLine("- NEVER expand the domain beyond what the user asked.");
        sb.AppendLine("- Keep each question short, specific, and actionable.");
        sb.AppendLine("- Prefer binary or categorical clarifications (e.g., \"Is this meant for internal use or as a commercial product?\").");
        sb.AppendLine("- If the output format requires JSON, return ONLY valid JSON with no commentary.");
        sb.AppendLine();
        sb.AppendLine("If in doubt, ask fewer clarification questions rather than more.");
        sb.AppendLine("The system defines the exact JSON schema for your response.");
        sb.AppendLine("You MUST output only that JSON structure, with no headings, markdown fences, or commentary.");

        return sb.ToString();
    }
}