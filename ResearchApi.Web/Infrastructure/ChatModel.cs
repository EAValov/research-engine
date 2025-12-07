using System.ClientModel;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using ResearchApi.Configuration;
using ResearchApi.Domain;
using ResearchApi.Prompts;

namespace ResearchApi.Infrastructure;

public sealed class OpenAiChatModel : IChatModel
{
    private readonly ChatClient _rawChatClient;
    private readonly IChatClient _chatClient;

    public string ModelId { get; }

    public OpenAiChatModel(IOptions<ChatConfig> options)
    {
        var cfg = options.Value ?? throw new ArgumentNullException(nameof(options));

        ModelId = cfg.ModelId;

        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(cfg.Endpoint)
        };

        var credential = new ApiKeyCredential(cfg.ApiKey);

        _rawChatClient = new ChatClient(
            model: cfg.ModelId,
            credential: credential,
            options: clientOptions);

        _chatClient = new ChatClientBuilder(_rawChatClient.AsIChatClient())
            .UseFunctionInvocation()
            .Build();
    }

    public Task<ChatResponse> ChatAsync(
        Prompt prompt,
        IEnumerable<AITool>? tools = null,
        Microsoft.Extensions.AI.ChatResponseFormat? responseFormat = null,
        float? temperature = null,
        CancellationToken cancellationToken = default)
    {
        if (prompt.systemPrompt is null)
            throw new ArgumentNullException(nameof(prompt.systemPrompt));
        if (prompt.userPrompt is null)
            throw new ArgumentNullException(nameof(prompt.userPrompt));

        var options = new ChatOptions();

        if (tools != null)
        {
            options.Tools = tools is IList<AITool> list ? list : [.. tools];

            // Current behavior: require the first tool if tools are provided.
            options.ToolMode = ChatToolMode.RequireSpecific(options.Tools[0].Name);
        }

        if (responseFormat is not null)
        {
            options.ResponseFormat = responseFormat;
        }

        if (temperature is not null)
        {
            options.Temperature = temperature;
        }

        var history = new List<Microsoft.Extensions.AI.ChatMessage>
        {
            new(ChatRole.System, prompt.systemPrompt),
            new(ChatRole.User, prompt.userPrompt)
        };

        return _chatClient.GetResponseAsync(history, options, cancellationToken);
    }

    public string StripThinkBlock(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var withoutThink = Regex.Replace(
            text,
            @"<think>.*?</think>",
            string.Empty,
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        return withoutThink.Trim();
    }

    public AITool CreateTool<TDelegate>(
        TDelegate function,
        string? name = null,
        string? description = null)
        where TDelegate : Delegate
    {
        if (function is null) throw new ArgumentNullException(nameof(function));

        // If name is not provided, use method name as default
        name ??= function.Method.Name;
        description ??= string.Empty;

        return AIFunctionFactory.Create(
            function,
            name: name,
            description: description);
    }
}

