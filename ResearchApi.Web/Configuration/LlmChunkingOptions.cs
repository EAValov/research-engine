namespace ResearchApi.Configuration;

public class LlmChunkingOptions
{
    public int MaxPromptTokens { get; set; } = 32768;
}
