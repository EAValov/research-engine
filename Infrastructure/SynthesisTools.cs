namespace ResearchApi.Infrastructure;

public static class SynthesisTools
{
    public static readonly object[] Tools =
    {
        new
        {
            type = "function",
            function = new
            {
                name = "get_similar_learnings",
                description = "Retrieve the most relevant evidence snippets (learnings) from the research knowledge base for a specific sub-question.",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        query = new
                        {
                            type = "string",
                            description = "Concrete sub-question or aspect of the main research query you want evidence for."
                        },
                        top_k = new
                        {
                            type = "integer",
                            description = "Maximum number of learnings to retrieve (1–50).",
                            minimum = 1,
                            maximum = 50
                        },
                        language = new
                        {
                            type = "string",
                            description = "Optional 2-letter language code (e.g. 'en', 'de', 'ru')."
                        },
                        region = new
                        {
                            type = "string",
                            description = "Optional 2-letter region/country code (e.g. 'DE', 'US', 'RU')."
                        }
                    },
                    required = new[] { "query" }
                }
            }
        }
    };
}
public sealed class ToolLearningDto
{
    public Guid Id { get; set; }
    public string Text { get; set; } = null!;
    public string SourceUrl { get; set; } = null!;
    public string Citation { get; set; } = null!;
}
    
public sealed class GetSimilarLearningsToolResult
{
    public List<ToolLearningDto> Learnings { get; set; } = new();
    public int TotalAvailable { get; set; }
}