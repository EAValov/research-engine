using System.Text;

namespace ResearchApi.Prompts;

public static class FeedbackPromptFactory
{
    /// <summary>
    /// Builds a  prompt to generate feedback queries for the given research query.
    /// </summary>
    public static Prompt Build(string query, int MaxQuestions = 3, bool includeBreadthDepthQuestions = false)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Given the following research query, ask up to {MaxQuestions} clarification questions that help disambiguate the user's intent, scope, constraints, or assumptions.");
        sb.AppendLine("Ask ONLY questions the user must answer, not questions that require external research.");
        sb.AppendLine();
        sb.AppendLine("The user's research query is:");
        sb.AppendLine($"<query>{query}</query>");
        sb.AppendLine();

        if (includeBreadthDepthQuestions)
        {       
            sb.AppendLine("In addition, at the END of the list, include two extra questions that explicitly ask:");
            sb.AppendLine("- how BROAD vs NARROW the user wants the research to be (many directions vs focused),");
            sb.AppendLine("- how DEEP vs QUICK they want the analysis (high-level vs very detailed).");
            sb.AppendLine();
        }

        sb.AppendLine("At the end, return your answer as a JSON object with this shape:");
        sb.AppendLine("{ \"queries\": [ \"first query here\", \"second query here\", \"third query here\" ] }");
        sb.AppendLine("Do not include any other text outside this JSON object.");

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
        sb.AppendLine("- Ask only about things the USER must decide: scope, region, business model, data types, regulatory classification, depth of analysis, constraints.");
        sb.AppendLine("- NEVER ask questions that are themselves research tasks or require technical/regulatory analysis.");
        sb.AppendLine("- NEVER expand the domain beyond what the user asked.");
        sb.AppendLine("- Keep each question short, specific, and actionable.");
        sb.AppendLine("- Prefer binary or categorical clarifications (e.g., 'Is this meant as a wellness app or a medical device?').");
        sb.AppendLine("- If the output format requires JSON, return ONLY valid JSON with no commentary.");
        sb.AppendLine();
        sb.AppendLine("If in doubt, ask fewer clarification questions rather than more.");
        
        return sb.ToString();
    }
}