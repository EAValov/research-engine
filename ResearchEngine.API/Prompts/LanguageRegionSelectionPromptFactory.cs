using System.Text;
using ResearchEngine.Domain;

namespace ResearchEngine.Prompts;

public static class LanguageRegionSelectionPromptFactory
{
    public static Prompt Build(string query, IReadOnlyList<Clarification> clarifications)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You must select the best target LANGUAGE and REGION/LOCATION for performing web research.");
        sb.AppendLine();
        sb.AppendLine("Where:");
        sb.AppendLine("- \"language\" is a 2-letter ISO 639-1 code in LOWERCASE, e.g.:");
        sb.AppendLine("    - \"de\" for German");
        sb.AppendLine("    - \"en\" for English");
        sb.AppendLine("    - \"ja\" for Japanese");
        sb.AppendLine("- \"region\" is a HUMAN-READABLE LOCATION STRING that will be passed directly as a `location` parameter");
        sb.AppendLine("  to the Firecrawl search API, e.g.:");
        sb.AppendLine("    - \"Germany\"");
        sb.AppendLine("    - \"Japan\"");
        sb.AppendLine("    - \"Bavaria,Germany\"");
        sb.AppendLine("    - \"San Francisco,California,United States\"");
        sb.AppendLine("  or null if no specific region is appropriate.");
        sb.AppendLine();
        sb.AppendLine("These values will be used as follows:");
        sb.AppendLine("- The \"language\" value will be used as the target output language and may be passed to the search engine as a language hint.");
        sb.AppendLine("- The \"region\" value will be passed AS-IS as the `location` string to the search engine to bias results geographically.");
        sb.AppendLine();
        sb.AppendLine("Selection rules:");
        sb.AppendLine("- If the query is written clearly in a specific language, choose that language.");
        sb.AppendLine("- If the user explicitly requests a language (e.g. \"answer in German\", \"please research in English\"),");
        sb.AppendLine("  you MUST use that language code if possible.");
        sb.AppendLine("- If the user explicitly mentions a country or region focus (e.g. \"in Germany\", \"for the US market\",");
        sb.AppendLine("  \"in the EU\"), choose a matching region string.");
        sb.AppendLine("- Prefer region strings that explicitly include the country name whenever there is one");
        sb.AppendLine("  (for example \"Moscow,Russia\" instead of just \"Moscow\") so downstream regional source rules can activate correctly.");
        sb.AppendLine("- If the topic is clearly about Germany (e.g., \"Bayern\", \"Deutschland\", \"Bundesland\"), strongly prefer:");
        sb.AppendLine("    language = \"de\", region = \"Germany\".");
        sb.AppendLine("- If the topic is clearly about a specific German state (e.g., Bayern), you MAY use a more specific region like:");
        sb.AppendLine("    language = \"de\", region = \"Bavaria,Germany\".");
        sb.AppendLine("- If the topic is clearly about another country or city, choose a sensible string like:");
        sb.AppendLine("    \"France\", \"United Kingdom\", \"California,United States\", \"Toronto,Ontario,Canada\".");
        sb.AppendLine("- If the topic is global and there is no clear regional focus, treat it as:");
        sb.AppendLine("    language = \"en\", region = null.");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT:");
        sb.AppendLine("- Do NOT return language names like \"German\" or \"English\" – only 2-letter codes in the language field.");
        sb.AppendLine("- Do NOT return formats like \"de-DE\" or \"en-GB\" in the region field – it must be a human-readable location string");
        sb.AppendLine("  (e.g., \"Germany\", \"United Kingdom\", \"Berlin,Germany\").");
        sb.AppendLine("- If no specific region makes sense, conceptually set region to null.");
        sb.AppendLine();
        sb.AppendLine("You will respond in a structured JSON format provided by the system.");
        sb.AppendLine("- Fill in the language code and, if applicable, a region/location string according to the rules above.");
        sb.AppendLine("- Output only the JSON payload required by the system, with no extra commentary or formatting.");
        sb.AppendLine();
        sb.AppendLine("User query:");
        sb.AppendLine(query);
        sb.AppendLine();

        if (clarifications.Count > 0)
        {
            sb.AppendLine("Clarifications (these indicate what matters most to the user):");
            foreach (var c in clarifications)
            {
                sb.AppendLine($"- Q: {c.Question}");
                sb.AppendLine($"  A: {c.Answer}");
            }
            sb.AppendLine();
        }

        var system =
            "You are a router that selects the best target language and location string for web research. " +
            "The system defines the exact JSON schema for your response. " +
            "You MUST always respond with that JSON structure only, with fields for language and region, and no extra text.";

        return new Prompt(system, sb.ToString());
    }
}


