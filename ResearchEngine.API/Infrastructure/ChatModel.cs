using System.ClientModel;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using ResearchEngine.Configuration;
using ResearchEngine.Domain;

namespace ResearchEngine.Infrastructure;

public sealed class OpenAiChatModel : IChatModel
{
    private readonly IRuntimeSettingsAccessor _runtimeSettings;
    private readonly object _sync = new();
    private ChatClientState? _state;

    public OpenAiChatModel(IRuntimeSettingsAccessor runtimeSettings)
    {
        _runtimeSettings = runtimeSettings ?? throw new ArgumentNullException(nameof(runtimeSettings));
    }

    public string ModelId => GetOrCreateStateAsync(CancellationToken.None).GetAwaiter().GetResult().Config.ModelId;

    public async Task<ChatResponse> ChatAsync(
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

        var state = await GetOrCreateStateAsync(cancellationToken);
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

        return await state.ChatClient.GetResponseAsync(history, options, cancellationToken);
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

        name ??= function.Method.Name;
        description ??= string.Empty;

        return AIFunctionFactory.Create(
            function,
            name: name,
            description: description);
    }

    private async Task<ChatClientState> GetOrCreateStateAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _runtimeSettings.GetCurrentAsync(cancellationToken);
        var config = snapshot.ChatConfig ?? throw new InvalidOperationException("ChatConfig is not configured.");

        lock (_sync)
        {
            if (_state is not null &&
                string.Equals(_state.Config.Endpoint, config.Endpoint, StringComparison.Ordinal) &&
                string.Equals(_state.Config.ApiKey, config.ApiKey, StringComparison.Ordinal) &&
                string.Equals(_state.Config.ModelId, config.ModelId, StringComparison.Ordinal))
            {
                return _state;
            }

            if (string.IsNullOrWhiteSpace(config.ApiKey))
                throw new InvalidOperationException("Missing required configuration: ChatConfig:ApiKey");

            var clientOptions = new OpenAIClientOptions
            {
                Endpoint = new Uri(config.Endpoint, UriKind.Absolute)
            };

            var rawChatClient = new ChatClient(
                config.ModelId,
                new ApiKeyCredential(config.ApiKey),
                clientOptions);

            _state = new ChatClientState(
                new ChatConfig
                {
                    Endpoint = config.Endpoint,
                    ApiKey = config.ApiKey,
                    ModelId = config.ModelId,
                    MaxContextLength = config.MaxContextLength
                },
                new ChatClientBuilder(rawChatClient.AsIChatClient())
                    .UseFunctionInvocation()
                    .Build());

            return _state;
        }
    }

    private sealed record ChatClientState(ChatConfig Config, IChatClient ChatClient);
}
