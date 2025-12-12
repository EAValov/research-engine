using System.Globalization;
using System.Text;
using ResearchApi.Domain;

namespace ResearchApi.Prompts;

public static class SectionWritingPromptFactory
{
    public static Prompt BuildSectionPrompt(
        string query,
        string? clarifications,
        string targetLanguage,
        SectionPlan section)
    {
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

        userSb.AppendLine("You are now writing ONE section of the full report.");
        userSb.AppendLine();
        userSb.AppendLine("Section specification:");
        userSb.AppendLine($"- Title: {section.Title}");
        userSb.AppendLine($"- Scope: {section.Description}");
        userSb.AppendLine();
        userSb.AppendLine("Constraints for this section:");
        userSb.AppendLine("- Focus ONLY on the scope of this section; do not write other sections.");
        userSb.AppendLine("- DO NOT include the section title as a Markdown heading.");
        userSb.AppendLine("  The caller will add the section heading separately.");
        userSb.AppendLine("- Start directly with the body text.");

        userSb.AppendLine();
        userSb.AppendLine("STRUCTURE AND FORMATTING:");
        userSb.AppendLine("- You MAY use paragraphs, bullet lists, and tables.");
        userSb.AppendLine("- Use tables when they clearly add value, for example when:");
        userSb.AppendLine("  • comparing more than a few items across multiple criteria (features, risks, KPIs, scenarios, etc.),");
        userSb.AppendLine("  • summarizing metrics, benchmarks, or structured frameworks in a compact way.");
        userSb.AppendLine("- For simple points or short lists, plain text and bullet lists are often better than tables.");
        userSb.AppendLine("- When the structure of the information would benefit from a diagram (e.g. flows, stages, pipelines,");
        userSb.AppendLine("  architectures, timelines), you MAY include a Mermaid diagram using a fenced code block of the form:");
        userSb.AppendLine("  ```mermaid");
        userSb.AppendLine("  ...diagram definition...");
        userSb.AppendLine("  ```");
        userSb.AppendLine("- Keep Mermaid diagrams focused and readable; do not overcomplicate them.");
        userSb.AppendLine("- IMPORTANT for Mermaid: in node labels like A[Label], avoid parentheses (), commas, and other special");
        userSb.AppendLine("  punctuation characters. Use short, clean labels such as [Cloud Storage], [AI Indexing], [Optical Archive]");
        userSb.AppendLine("  instead of [Cloud Storage (GDPR-Compliant)] or [Optical Archive (Ceramic/Glass)].");
        userSb.AppendLine();
        userSb.AppendLine("TOOL AND EVIDENCE USE:");
        userSb.AppendLine("- Before or while drafting this section, you MUST call `get_similar_learnings`");
        userSb.AppendLine("  at least once with a focused query relevant to this section.");
        userSb.AppendLine("- You MAY call `get_similar_learnings` multiple times in this section if you need additional evidence");
        userSb.AppendLine("  for different sub-aspects (e.g. regulation vs. market vs. technology details).");
        userSb.AppendLine("- Use the evidence returned by the tool and preserve [n] citation markers.");
        userSb.AppendLine("- Do NOT add your own citation numbers.");
        userSb.AppendLine("- Do NOT write the conclusion for the whole report here; only this section.");

        var userPrompt = userSb.ToString();

        return new Prompt(BuildSystemPrompt(targetLanguage), userPrompt);
    }

    static string BuildSystemPrompt(string? targetLanguage = "en")
    {
        var dt = DateTime.UtcNow.ToString("s", CultureInfo.InvariantCulture);
        var sb = new StringBuilder();

        sb.AppendLine($"You are an expert research synthesizer. Today is {dt} (UTC).");
        sb.AppendLine("Your task is to write structured, well-supported analytical text.");
        sb.AppendLine();

        sb.AppendLine("USE OF STRUCTURED FORMATS:");
        sb.AppendLine("- Choose the format that best communicates the information:");
        sb.AppendLine("  • Use normal paragraphs for narrative explanations and reasoning.");
        sb.AppendLine("  • Use bullet lists for short, discrete points.");
        sb.AppendLine("  • Use tables when comparing multiple items across several dimensions,");
        sb.AppendLine("    or when summarizing metrics, criteria, or frameworks.");
        sb.AppendLine("- When relationships, flows, or architectures are important, you MAY include Mermaid diagrams,");
        sb.AppendLine("  using fenced code blocks with the exact syntax:");
        sb.AppendLine("  ```mermaid");
        sb.AppendLine("  ...diagram definition...");
        sb.AppendLine("  ```");
        sb.AppendLine("- In Mermaid node labels (e.g. A[Label]), avoid parentheses (), commas, or other complex punctuation.");
        sb.AppendLine("  Prefer short labels like [Cloud Storage], [AI Indexing], [Long-Term Retrieval].");
        sb.AppendLine("- Keep tables and diagrams grounded in the evidence from learnings; do not invent data or numbers just to fill them.");

        sb.AppendLine();
        sb.AppendLine("SOURCE RELIABILITY AND TECHNOLOGY MATURITY:");
        sb.AppendLine("- When weighing evidence, prefer the most authoritative sources:");
        sb.AppendLine("  • official standards bodies, regulators, and government agencies,");
        sb.AppendLine("  • peer-reviewed research and reputable technical documentation,");
        sb.AppendLine("  • major vendors’ official docs and whitepapers.");
        sb.AppendLine("- Treat vendor marketing materials, blogs, Q&A sites, and social media (e.g., LinkedIn, Medium, StackExchange) as secondary sources.");
        sb.AppendLine("  Use them mainly for context or examples, not as the sole basis for strong claims.");
        sb.AppendLine("- For each important technology you describe (e.g. storage media, encryption schemes, AI methods), be explicit about its maturity level, such as:");
        sb.AppendLine("  • widely deployed / production-grade,");
        sb.AppendLine("  • emerging / early adoption,");
        sb.AppendLine("  • experimental / research or vendor claims.");
        sb.AppendLine("- Do NOT present speculative or experimental technologies (e.g. DNA storage at consumer scale, fully homomorphic encryption for general workloads)");
        sb.AppendLine("  as if they were mature, plug-and-play solutions, unless the evidence clearly supports that.");
        sb.AppendLine("- When sources disagree or when the evidence is limited, acknowledge the uncertainty instead of asserting a single, confident conclusion.");

        sb.AppendLine();
        sb.AppendLine("TOOL USAGE (`get_similar_learnings`):");
        sb.AppendLine("- You can call the tool `get_similar_learnings` to retrieve evidence snippets");
        sb.AppendLine("  that already contain citation markers like [3], [7]. These snippets come from");
        sb.AppendLine("  a vetted knowledge base and must be treated as authoritative evidence.");
        sb.AppendLine("- For each substantive section you write, you MUST call `get_similar_learnings`");
        sb.AppendLine("  at least once with a focused query relevant to that section.");
        sb.AppendLine("- You MAY call `get_similar_learnings` multiple times within the same section for different subtopics.");
        sb.AppendLine("- Prefer several small, focused queries over one broad query.");
        sb.AppendLine("- Do not guess facts that could be retrieved through the tool.");
        sb.AppendLine("- If two learnings conflict on a key fact, note the disagreement explicitly instead of silently choosing one.");

        sb.AppendLine();
        sb.AppendLine("CITATIONS:");
        sb.AppendLine("- When using information from learnings, preserve the [n] citation numbers as-is.");
        sb.AppendLine("- Do NOT invent new citation numbers.");
        sb.AppendLine("- Do NOT change existing [n] markers (they are already correctly mapped to sources).");

        sb.AppendLine();
        sb.AppendLine("QUANTITATIVE REASONING:");
        sb.AppendLine("- When appropriate, include clear, simple quantitative reasoning:");
        sb.AppendLine("  costs, ranges, orders of magnitude, throughput, basic estimates.");
        sb.AppendLine("- Distinguish evidence-backed numbers (with [n]) from assumptions.");
        sb.AppendLine("- If a number or time horizon is highly uncertain in the sources, express that uncertainty explicitly.");

        sb.AppendLine();
        sb.AppendLine("HARD CONSTRAINT (section-level):");
        sb.AppendLine("- You must not write a substantive section of the report without first calling");
        sb.AppendLine("  `get_similar_learnings` specifically for that section in this answer.");
        sb.AppendLine("- For EACH non-conclusion section you plan to write, check:");
        sb.AppendLine("    'Have I already called `get_similar_learnings` with a focused query for this section?'");
        sb.AppendLine("  If the answer is no, you MUST call `get_similar_learnings` for that section BEFORE");
        sb.AppendLine("  you start writing its content.");
        sb.AppendLine("- If you produce a section that contains factual claims but you never called the tool");
        sb.AppendLine("  for that section, you are failing the task.");

        sb.AppendLine();
        sb.AppendLine("OUTPUT FORMAT (IMPORTANT):");
        sb.AppendLine("- Your output MUST be valid GitHub-Flavored Markdown text.");
        sb.AppendLine("- Do NOT use any HTML tags (e.g. <references>, <sup>, <br>).");
        sb.AppendLine("- Do NOT use markdown code fences, EXCEPT when creating Mermaid diagrams with:");
        sb.AppendLine("  ```mermaid ... ```");
        sb.AppendLine("- Citations must appear only as simple numeric markers like [1], [2], [3].");
        sb.AppendLine("- When you need multiple citations, use the form: [1], [3], [5].");
        sb.AppendLine("- Never write [1][3][5]; this pattern is forbidden.");
        sb.AppendLine();
        sb.AppendLine($"Write the final text in: {targetLanguage ?? "en"}.");

        return sb.ToString();
    }
}
