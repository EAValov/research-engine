namespace ResearchApi.Domain;

public interface ITokenizer
{
    /// <summary>
    /// Convenience wrapper that builds a vLLM-style chat payload from Prompt.
    /// </summary>
    Task<TokenizeResult> TokenizePromptAsync(
        Prompt prompt,
        CancellationToken cancellationToken = default);
}
