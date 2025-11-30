using System;
using System.Text;

namespace ResearchApi.Prompts;

public static class PlanningPromptFactory
{
    /// <summary>
    /// Builds a planning prompt to generate SERP queries for the given research query.
    /// Clarifications, breadth and depth are optional and will be gracefully handled.
    /// </summary>
    public static Prompt Build(
        string query,
        string? clarificationsText = null,
        int? breadth = null,
        int? depth = null,
        string? targetLanguage = "en")
    {
        var effectiveBreadth = breadth is > 0 ? breadth.Value : 3;
        var effectiveDepth = depth is > 0 ? depth.Value : 2;

        var sb = new StringBuilder();

        sb.AppendLine("You are a research planning agent for web search.");
        sb.AppendLine("Your task is to design high-value, Google-style SERP queries for a separate crawler.");
        sb.AppendLine("Those queries will be used to gather sources for deep analysis of the topic.");
        sb.AppendLine();
        sb.AppendLine("You MUST follow these rules:");
        sb.AppendLine($"- Generate up to {effectiveBreadth} UNIQUE search queries (you may use fewer if the topic is very narrow).");
        sb.AppendLine("- Think step-by-step, but only output the final JSON (no explanations).");
        sb.AppendLine();
        sb.AppendLine("Query design rules:");
        sb.AppendLine($"- Think in terms of research DEPTH = {effectiveDepth}:");
        sb.AppendLine("  - Depth 1: broad, high-level overview queries about the main topic.");
        sb.AppendLine("  - Higher depth: more specific, technical, or edge-case queries that build on earlier aspects.");
        sb.AppendLine("- Avoid near-duplicates. Each query must target a DISTINCT aspect (e.g. regulation, market size, cost structure, competition, technology).");
        sb.AppendLine("- Queries MUST stay tightly focused on the user’s domain and use that domain’s key terms.");
        sb.AppendLine("  - Do NOT drift into unrelated industries just because they share words like “AI”, “cloud”, “subscription”, “market”, etc.");
        sb.AppendLine("  - Always include 2–4 core topic keywords from the prompt/clarifications (e.g. “cloud photo storage”, “consumer health app”, “GDPR”, “EU AI Act”).");
        sb.AppendLine("- Prefer queries that are likely to surface:");
        sb.AppendLine("  - official or primary sources (e.g. EU, FDA, regulators, standards bodies),");
        sb.AppendLine("  - reputable market/industry reports,");
        sb.AppendLine("  - recent analyses in the 2020–2030 timeframe when relevant (e.g. add 2024, 2025, 2030 in the query if useful).");
        sb.AppendLine("- Use neutral, information-seeking wording, suitable for a search engine.");
        sb.AppendLine("- Keep each query reasonably concise (typically under 140 characters).");
        sb.AppendLine();
        sb.AppendLine("Before you output anything, silently do this:");
        sb.AppendLine("1) Identify 2–5 CORE domain keywords/entities from the main prompt (e.g. product type, industry, region, key regulations).");
        sb.AppendLine("2) Break the research need into 2–5 distinct sub-questions (e.g. regulation, market size, competition, cost structure, technology landscape).");
        sb.AppendLine("3) Design one precise search query per sub-question that:");
        sb.AppendLine("   - explicitly includes at least 2 of the core domain keywords, and");
        sb.AppendLine("   - is specific enough to avoid pulling results from unrelated sectors.");
        sb.AppendLine();
        sb.AppendLine("The user's main research query is:");
        sb.AppendLine($"<prompt>{query}</prompt>");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(clarificationsText))
        {
            sb.AppendLine("Here is additional context from the user (clarifying questions and answers).");
            sb.AppendLine("Use this context to disambiguate the topic and refine which aspects and keywords matter most:");
            sb.AppendLine("<clarifications>");
            sb.AppendLine(clarificationsText.Trim());
            sb.AppendLine("</clarifications>");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("No additional clarifications were provided.");
            sb.AppendLine("Infer reasonable assumptions about target market, timeframe, and constraints, but avoid over-speculation.");
            sb.AppendLine();
        }

        sb.AppendLine($"The target language for SERP queries is {targetLanguage}.");
        sb.AppendLine("Generate ALL search queries in this language unless a proper noun is normally written in English.");
        sb.AppendLine();
        sb.AppendLine("Return your answer as a JSON string with this exact shape:");
        sb.AppendLine("{ \"queries\": [ \"first query here\", \"second query here\", \"third query here\" ] }");
        sb.AppendLine("Do NOT include any other keys, comments, or text outside this JSON object.");

        return new Prompt(GetSystemPrompt(), sb.ToString());
    }

    private static string GetSystemPrompt()
    {
        var dt = DateTime.UtcNow.ToString("s", System.Globalization.CultureInfo.InvariantCulture);
        var sb = new StringBuilder();

        sb.AppendLine($"You are an expert web research planner. Today is {dt} (UTC).");
        sb.AppendLine("Your job is to design highly effective search engine queries (SERP queries) for a separate crawler.");
        sb.AppendLine();
        sb.AppendLine("Core principles:");
        sb.AppendLine("- Treat the user as a highly experienced analyst; do NOT oversimplify.");
        sb.AppendLine("- Your focus is on COVERAGE and RELEVANCE of sources, not on answering the question yourself.");
        sb.AppendLine("- Accuracy means: queries must reliably surface authoritative, up-to-date, domain-relevant sources.");
        sb.AppendLine("- Prefer primary and reputable sources (regulators, official docs, standards bodies, well-known market research firms, major tech/finance/health outlets).");
        sb.AppendLine("- Avoid queries that are so generic they return unrelated industries just because of shared buzzwords (e.g. generic 'AI market 2030' if the topic is 'AI in dermatology').");
        sb.AppendLine();
        sb.AppendLine("When a task prompt specifies an output format (e.g., JSON with a `queries` array), you MUST follow it exactly and output nothing else.");
        sb.AppendLine("Do not include your reasoning or commentary in the output; only produce what the task prompt asks for.");

        return sb.ToString();
    }
}