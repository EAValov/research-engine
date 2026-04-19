using System;
using System.Text;
using ResearchEngine.Domain;

namespace ResearchEngine.Prompts;

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
        string? targetLanguage = "en",
        DateTime? utcNow = null)
    {
        var effectiveBreadth = breadth is > 0 ? breadth.Value : 3;
        var effectiveDepth   = depth   is > 0 ? depth.Value   : 2;
        var effectiveUtcNow = utcNow ?? DateTime.UtcNow;
        var currentDate = effectiveUtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var currentYear = effectiveUtcNow.Year;
        var priorYear = currentYear - 1;

        var sb = new StringBuilder();

        sb.AppendLine("You are a research planning agent for web search.");
        sb.AppendLine("Your task is to design high-value, Google-style SERP queries for a separate crawler.");
        sb.AppendLine("Those queries will be used to gather sources for deep analysis of the topic.");
        sb.AppendLine();
        sb.AppendLine("You MUST follow these rules:");
        sb.AppendLine($"- Generate up to {effectiveBreadth} UNIQUE search queries (you may use fewer if the topic is very narrow).");
        sb.AppendLine("- Think through the planning process, but only output the final JSON (no explanations, no commentary).");
        sb.AppendLine();
        sb.AppendLine("Query design rules:");
        sb.AppendLine($"- Think in terms of research DEPTH = {effectiveDepth}:");
        sb.AppendLine("  - Depth 1: broad, high-level overview queries about the main topic.");
        sb.AppendLine("  - Higher depth: more specific, technical, or niche queries that drill into particular aspects.");
        sb.AppendLine("- Avoid near-duplicates. Each query must target a DISTINCT aspect of the topic.");
        sb.AppendLine("  - Examples of aspects (depending on the topic): core concepts, theory/background, methods/technology,");
        sb.AppendLine("    implementation/practice, regulation/standards, stakeholders, risks/limitations, benchmarks/datasets,");
        sb.AppendLine("    market/competition, or case studies.");
        sb.AppendLine("- Queries MUST stay tightly focused on the user’s domain and use that domain’s key terms.");
        sb.AppendLine("  - Do NOT drift into unrelated domains just because they share words like “AI”, “cloud”, “subscription”, “market”, etc.");
        sb.AppendLine("  - Always include 2–4 core topic keywords from the prompt/clarifications when possible.");
        sb.AppendLine("- Prefer queries that are likely to surface:");
        sb.AppendLine("  - official or primary sources (regulators, standards bodies, official docs, primary research),");
        sb.AppendLine("  - reputable reports or reviews (e.g. industry reports, systematic reviews, whitepapers),");
        sb.AppendLine($"  - recent analyses when recency is important; anchor recency to today's actual date ({currentDate} UTC), not to a model knowledge cutoff.");
        sb.AppendLine($"  - if adding years improves retrieval, prefer {currentYear} and {priorYear}; only go further back when the user explicitly asks for history, baselines, or multi-year comparisons.");
        sb.AppendLine($"  - for prompts about current conditions, latest updates, recent developments, or trends, avoid stale year anchors such as {priorYear - 1} or earlier unless they are genuinely necessary.");
        sb.AppendLine("- Use neutral, information-seeking wording, suitable for a search engine.");
        sb.AppendLine("- Keep each query reasonably concise (typically under 140 characters).");
        sb.AppendLine();
        sb.AppendLine("Before you output anything, silently do this:");
        sb.AppendLine("1) Identify 2–5 CORE domain keywords/entities from the main prompt (e.g. product type, technology, disease,");
        sb.AppendLine("   legal framework, market/sector, region).");
        sb.AppendLine("2) Break the research need into 2–5 distinct sub-questions that together give good coverage of the topic.");
        sb.AppendLine("   - Choose aspects that are genuinely relevant for THIS query (e.g. theory, methods, regulation,");
        sb.AppendLine("     applications, risks, stakeholders, economics, benchmarks, etc.).");
        sb.AppendLine("3) Design one precise search query per sub-question that:");
        sb.AppendLine("   - explicitly includes at least 2 of the core domain keywords when possible, and");
        sb.AppendLine("   - is specific enough to avoid pulling results from unrelated sectors or topics.");
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
            sb.AppendLine("Infer reasonable assumptions about target context and constraints, but avoid over-speculation.");
            sb.AppendLine();
        }

        sb.AppendLine($"The target language for SERP queries is {targetLanguage}.");
        sb.AppendLine("Generate ALL search queries in this language unless a proper noun or acronym is normally written in English.");
        sb.AppendLine();
        sb.AppendLine("You will respond in a structured JSON format provided by the system.");
        sb.AppendLine("- Include up to the configured maximum number of search queries.");
        sb.AppendLine("- Each item must be a single search query string.");
        sb.AppendLine("- Output only the JSON payload required by the system, with no extra commentary or formatting.");
        
        return new Prompt(GetSystemPrompt(effectiveUtcNow), sb.ToString());
    }

    private static string GetSystemPrompt(DateTime utcNow)
    {
        var dt = utcNow.ToString("s", System.Globalization.CultureInfo.InvariantCulture);
        var sb = new StringBuilder();

        sb.AppendLine($"You are an expert web research planner. Today is {dt} (UTC).");
        sb.AppendLine("Your job is to design highly effective search engine queries (SERP queries) for a separate crawler.");
        sb.AppendLine();
        sb.AppendLine("Core principles:");
        sb.AppendLine("- Treat the user as a highly experienced analyst; do NOT oversimplify.");
        sb.AppendLine("- Your focus is on COVERAGE and RELEVANCE of sources, not on answering the question yourself.");
        sb.AppendLine("- Accuracy means: queries must reliably surface authoritative, up-to-date, domain-relevant sources.");
        sb.AppendLine("- When the user asks about current, recent, latest, today, or trends, use today's date above as the source of truth for recency.");
        sb.AppendLine("- Do not anchor on an internal training cutoff or arbitrary past years when selecting year terms.");
        sb.AppendLine("- Prefer primary and reputable sources (regulators, official docs, standards bodies,");
        sb.AppendLine("  well-known market research firms, major tech/finance/health/science outlets, etc.).");
        sb.AppendLine("- Avoid queries that are so generic they return unrelated domains just because of shared buzzwords.");
        sb.AppendLine();
        sb.AppendLine("When a task prompt specifies an output format (e.g., JSON with specific fields), you MUST follow it exactly.");
        sb.AppendLine("The system defines the exact JSON schema for your response.");
        sb.AppendLine("Do not include your reasoning or commentary in the output; only produce the JSON structure required by the system.");

        return sb.ToString();
    }
}
