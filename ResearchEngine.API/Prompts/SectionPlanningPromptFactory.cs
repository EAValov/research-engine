using System.Text;
using ResearchEngine.Domain;

namespace ResearchEngine.Prompts;

using System.Text;

public static class SectionPlanningPromptFactory
{
    /// <summary>
    /// Builds a prompt for planning report sections.
    /// </summary>
    public static Prompt BuildPlanningPrompt(
        string query,
        string? clarifications,
        string? instructions,
        string? targetLanguage = "en")
    {
        targetLanguage ??= "en";

        var systemSb = new StringBuilder();
        systemSb.AppendLine("You are an expert research planner.");
        systemSb.AppendLine("Your task is to design a clear, logical, high-value section structure for an analytical report.");
        systemSb.AppendLine($"Plan the sections in language: {targetLanguage}.");
        systemSb.AppendLine();
        systemSb.AppendLine("You MUST respond using the structured JSON schema provided by the system.");
        systemSb.AppendLine("Do NOT include any extra keys not allowed by the schema.");
        systemSb.AppendLine();
        systemSb.AppendLine("Your job is not to merely split the topic into broad themes.");
        systemSb.AppendLine("Your job is to identify the real reasoning structure needed to answer the query well.");
        systemSb.AppendLine();
        systemSb.AppendLine("Planning principles:");
        systemSb.AppendLine("- First identify the primary question type implicitly: causal/why, comparative, decision/recommendation, historical, technical, regulatory, market/adoption, or mixed.");
        systemSb.AppendLine("- Identify the core tension, paradox, contrast, or hidden decision inside the query when one exists.");
        systemSb.AppendLine("- For 'why' questions, identify the single strongest explanatory backbone of the answer and make the report structure revolve around it.");
        systemSb.AppendLine("- Prefer sections that directly answer the question’s causal or analytical core.");
        systemSb.AppendLine("- The section order must reflect explanatory importance, not just broad topic coverage.");
        systemSb.AppendLine("- Do NOT default to generic topical buckets such as 'technology', 'regulation', 'challenges', or 'future' unless they are clearly central to answering the query.");
        systemSb.AppendLine("- If a factor is supporting rather than central, place it later in the report.");
        systemSb.AppendLine();
        systemSb.AppendLine("Question-type guidance:");
        systemSb.AppendLine("- For causal or 'why' questions, prioritize causes, mechanisms, incentives, and the most important explanatory factors before secondary details.");
        systemSb.AppendLine("- For adoption or market questions, prioritize behavior, incentives, substitution against alternatives, market structure, and practical motivations before deep technical detail.");
        systemSb.AppendLine("- For adoption questions, distinguish between consumer incentives, provider or merchant incentives, and system-level constraints when these are materially different.");
        systemSb.AppendLine("- For comparative questions, prioritize meaningful comparison dimensions, tradeoffs, and where each side performs better or worse.");
        systemSb.AppendLine("- For decision or recommendation questions, prioritize decision criteria, options, tradeoffs, risks, scenarios, and actionable conclusions.");
        systemSb.AppendLine("- For regulatory questions, prioritize classification, obligations, scope, risk exposure, and practical implications.");
        systemSb.AppendLine("- For technical questions, prioritize mechanism, constraints, architecture, and performance-relevant factors.");
        systemSb.AppendLine();
        systemSb.AppendLine("Ordering rules:");
        systemSb.AppendLine("- If the query contains a tension or paradox, resolve that tension early in the report structure.");
        systemSb.AppendLine("- The first non-conclusion sections should usually address the main explanatory core, not background or secondary factors.");
        systemSb.AppendLine("- Do not place technical architecture or protocol constraints before behavioral, economic, market, or decision sections unless the question is primarily technical.");
        systemSb.AppendLine("- Avoid spending early sections on descriptive background if the question is analytical.");
        systemSb.AppendLine();
        systemSb.AppendLine("Critical rules:");
        systemSb.AppendLine("- Output an ordered list of sections.");
        systemSb.AppendLine("- Each section has: index, title, description, isConclusion.");
        systemSb.AppendLine("- index MUST be 1-based, unique, and strictly increasing by 1.");
        systemSb.AppendLine("- Exactly ONE section must have isConclusion=true, and it MUST be the LAST section.");
        systemSb.AppendLine("- Produce 3–7 sections.");
        systemSb.AppendLine("- Section titles must be short, specific, and descriptive.");
        systemSb.AppendLine("- Descriptions must be 1–2 sentences and explain the analytical purpose of the section.");
        systemSb.AppendLine("- Avoid redundant sections and avoid splitting closely related ideas without a strong reason.");
        systemSb.AppendLine("- Prefer sections that make the final report feel like a direct answer to the query, not a generic overview.");
        systemSb.AppendLine();

        var systemPrompt = systemSb.ToString();

        var userSb = new StringBuilder();
        userSb.AppendLine("Main research query:");
        userSb.AppendLine(query.Trim());
        userSb.AppendLine();

        if (!string.IsNullOrWhiteSpace(clarifications))
        {
            userSb.AppendLine("Additional context / clarifications:");
            userSb.AppendLine(clarifications.Trim());
            userSb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(instructions))
        {
            userSb.AppendLine("Additional user instructions (must be followed):");
            userSb.AppendLine(instructions.Trim());
            userSb.AppendLine();
        }

        userSb.AppendLine("Task:");
        userSb.AppendLine("Propose 3–7 logical sections for a structured analytical report on this topic.");
        userSb.AppendLine("Make the section plan reflect the actual reasoning required by the question.");
        userSb.AppendLine("If the question is fundamentally about causes, adoption, incentives, comparison, or decision-making, the section plan should reflect that directly.");
        userSb.AppendLine("When relevant, separate consumer, provider/merchant, and system-level perspectives instead of blending them together.");
        userSb.AppendLine("The LAST section must be the conclusion and have isConclusion=true.");
        userSb.AppendLine();

        userSb.AppendLine("Formatting rules:");
        userSb.AppendLine("- Output ONLY JSON that matches the schema.");
        userSb.AppendLine("- No Markdown, no prose outside the JSON.");
        userSb.AppendLine("- Do NOT include HTML tags or <references> blocks.");

        var userPrompt = userSb.ToString();

        return new Prompt(systemPrompt, userPrompt);
    }
}