using System.Text;

namespace ResearchApi.Prompts;

public static class FeedbackPromptFactory
{
    /// <summary>
    /// Builds a feedback prompt to generate SERP queries for the given research query.
    /// </summary>
    public static string Build(string query, int MaxQuestions = 3, bool includeBreadthDepthQuestions = false)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Given the following query from the user, ask some follow up questions to clarify the research direction.");
        sb.AppendLine($"Return a maximum of {MaxQuestions} questions, but feel free to return less if the original query is clear.");
        sb.AppendLine();

        sb.AppendLine("The user's main research query is:");
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

        return sb.ToString();
    }
}