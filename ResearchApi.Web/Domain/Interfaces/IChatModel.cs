using Microsoft.Extensions.AI;
using ResearchApi.Prompts;

namespace ResearchApi.Domain;

public interface IChatModel
{
    string ModelId { get; }

    Task<ChatResponse> ChatAsync(
        Prompt prompt,
        IEnumerable<AITool>? tools = null,
        Microsoft.Extensions.AI.ChatResponseFormat? responseFormat = null,
        float? temperature = null,
        CancellationToken cancellationToken = default);

    string StripThinkBlock(string text);

    AITool CreateTool<TDelegate>(
        TDelegate function,
        string? name = null,
        string? description = null)
        where TDelegate : Delegate;
}

