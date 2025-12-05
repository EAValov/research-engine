using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;
using ResearchApi.Prompts;

namespace ResearchApi.Infrastructure;

public sealed class LlmServiceConfig
{
    public required string ChatEndpoint { get; init; }
    public required string ChatApiKey { get; init; }
    public required string ChatModelId { get; init; }

    public required string EmbeddingEndpoint { get; init; }
    public required string EmbeddingApiKey { get; init; }
    public required string EmbeddingModelId { get; init; }

    /// <summary>
    /// If true, the internal HttpClient used for /tokenize will accept any certificate
    /// (useful for local Caddy with internal TLS).
    /// </summary>
    public bool IgnoreServerCertificateErrors { get; init; } = true;
}

public interface ILlmService
{
    string ChatModelId { get; }
    string EmbeddingModelId { get; }

    Task<ChatResponse> ChatAsync(Prompt prompt, IEnumerable<AITool>? tools = null, Microsoft.Extensions.AI.ChatResponseFormat? responseFormat = null, float? temperature = null, CancellationToken cancellationToken = default);
    Task<Embedding<float>> GenerateEmbeddingAsync(string input, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Embedding<float>>> GenerateEmbeddingsAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken = default);
    Task<LlmService.TokenizeResult> TokenizeAsync(object payload, CancellationToken cancellationToken = default);
    Task<LlmService.TokenizeResult> TokenizePromptAsync(Prompt prompt, CancellationToken cancellationToken = default);

    string StripThinkBlock(string text);
}

public sealed class LlmService : IDisposable, ILlmService
{
    private readonly ChatClient _rawChatClient;
    private readonly IChatClient _chatClient; 
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly HttpClient _httpForTokenize;
    private readonly Uri _tokenizeRootUri; // root (without /v1)

    private readonly LlmServiceConfig config;

    public string ChatModelId { get; }
    public string EmbeddingModelId { get; }

    public LlmService(IOptions<LlmServiceConfig> options)
    {
        config = options.Value ?? throw new ArgumentNullException(nameof(options));

        ChatModelId      = config.ChatModelId;
        EmbeddingModelId = config.EmbeddingModelId;

        // Helper to create a handler that optionally ignores cert errors
        HttpMessageHandler CreateHandler()
        {
            var handler = new HttpClientHandler();

            if (config.IgnoreServerCertificateErrors)
            {
                // DEV ONLY: accept any certificate
                handler.ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            }

            return handler;
        }

        // ---------- CHAT CLIENT ----------
        var chatHandler = CreateHandler();
        var chatHttp    = new HttpClient(chatHandler);

        var chatOptions = new OpenAIClientOptions
        {
            Endpoint  = new Uri(config.ChatEndpoint),
            Transport = new HttpClientPipelineTransport(chatHttp)
        };

        var chatCredential = new ApiKeyCredential(config.ChatApiKey);

        _rawChatClient = new ChatClient(
            model: config.ChatModelId,
            credential: chatCredential,
            options: chatOptions);

        _chatClient =
            new ChatClientBuilder(_rawChatClient.AsIChatClient())
                .UseFunctionInvocation()
                .Build();

        // ---------- EMBEDDING CLIENT ----------
        var embedHandler = CreateHandler();
        var embedHttp    = new HttpClient(embedHandler);

        var embedOptions = new OpenAIClientOptions
        {
            Endpoint  = new Uri(config.EmbeddingEndpoint),
            Transport = new HttpClientPipelineTransport(embedHttp)
        };

        var embedCredential = new ApiKeyCredential(config.EmbeddingApiKey);

        var embeddingClient = new EmbeddingClient(
            model: config.EmbeddingModelId,
            credential: embedCredential,
            options: embedOptions);

        _embeddingGenerator = embeddingClient.AsIEmbeddingGenerator();

        // ---------- TOKENIZE CLIENT ----------
        var tokenizeHandler = CreateHandler();
        _httpForTokenize = new HttpClient(tokenizeHandler);

        // Strip trailing /v1 if present to get root
        var baseUrl = config.ChatEndpoint;
        var root = baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? baseUrl[..^3]
            : baseUrl;

        _tokenizeRootUri = new Uri(root, UriKind.Absolute);
    }

    /// <summary>
    /// Simple chat entry point: sends a system prompt and a user prompt, with optional tools and optional structured output.
    /// Uses the Microsoft.Extensions.AI pipeline with function invocation enabled.
    /// </summary>
    public Task<ChatResponse> ChatAsync(
        Prompt prompt, 
        IEnumerable<AITool>? tools = null,
        Microsoft.Extensions.AI.ChatResponseFormat? responseFormat = null,
        float? temperature = null,
        CancellationToken cancellationToken = default)
    {
        if (prompt.systemPrompt is null) throw new ArgumentNullException(nameof(prompt.systemPrompt));
        if (prompt.userPrompt is null) throw new ArgumentNullException(nameof(prompt.userPrompt));

        var options = new ChatOptions();

        if (tools != null)
        {
            options.Tools = tools is IList<AITool> list
                ? list
                : [.. tools];
            options.ToolMode = ChatToolMode.RequireSpecific(tools.First().Name);
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

    /// <summary>
    /// Low-level chat entry point: caller controls full history and ChatOptions.
    /// </summary>
    public Task<ChatResponse> ChatAsync(
        IReadOnlyList<Microsoft.Extensions.AI.ChatMessage> history,
        ChatOptions options,
        CancellationToken cancellationToken = default)
    {
        if (history is null) throw new ArgumentNullException(nameof(history));
        if (options is null) throw new ArgumentNullException(nameof(options));

        return _chatClient.GetResponseAsync(history, options, cancellationToken);
    }

    /// <summary>
    /// Generate embeddings for multiple inputs.
    /// </summary>
    public async Task<IReadOnlyList<Embedding<float>>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default)
    {
        if (inputs is null) throw new ArgumentNullException(nameof(inputs));

        var result = await _embeddingGenerator.GenerateAsync(inputs, cancellationToken: cancellationToken)
                                            .ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// Generate embedding for a single string.
    /// </summary>
    public async Task<Embedding<float>> GenerateEmbeddingAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));

        var result = await _embeddingGenerator.GenerateAsync(new[] { input }, cancellationToken: cancellationToken)
                                            .ConfigureAwait(false);
        return result[0];
    }

    // ----- TOKENIZATION -----

    public sealed class TokenizeResult
    {
        [JsonPropertyName("count")]
        public int Count { get; set; }

        [JsonPropertyName("max_model_len")]
        public int MaxModelLen { get; set; }

        [JsonPropertyName("tokens")]
        public int[]? Tokens { get; set; }
    }

    /// <summary>
    /// Generic /tokenize call. The payload object should match what vLLM expects.
    /// For example: new { model = ChatModelId, messages = ... }.
    /// </summary>
    public async Task<TokenizeResult> TokenizeAsync(
        object messages,
        CancellationToken cancellationToken = default)
    {
        if (messages is null) throw new ArgumentNullException(nameof(messages));

        var uri = new Uri(_tokenizeRootUri, "tokenize");

        var resp = await _httpForTokenize.PostAsJsonAsync(uri, messages, cancellationToken);
        var rawJson = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Tokenize failed: HTTP {(int)resp.StatusCode} {resp.StatusCode}, body: {rawJson}");
        }

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var parsed = JsonSerializer.Deserialize<TokenizeResult>(rawJson, options)
                    ?? throw new InvalidOperationException("Failed to deserialize /tokenize response.");

        return parsed;
    }

    /// <summary>
    /// Convenience wrapper for tokenizing chat-style messages.
    /// </summary>
    public Task<TokenizeResult> TokenizePromptAsync (
        Prompt prompt,
        CancellationToken cancellationToken = default)
    {
        if (prompt is null) throw new ArgumentNullException(nameof(prompt));

        var history = new List<Microsoft.Extensions.AI.ChatMessage>
        {
            new(ChatRole.System, prompt.systemPrompt),
            new(ChatRole.User, prompt.userPrompt)
        };

        var messages = new List<object>
        {
            new { role = "system", content = prompt.systemPrompt },
            new { role = "user",   content = prompt.userPrompt }
        };

        var payload = new
        {
            model = ChatModelId,
            messages
        };

        return TokenizeAsync(payload, cancellationToken);
    }

     public string StripThinkBlock(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Remove <think>...</think> blocks (non-greedy, multiline)
        var withoutThink = Regex.Replace(
            text,
            @"<think>.*?</think>",
            string.Empty,
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        return withoutThink.Trim();
    }

    public static AITool CreateTool<TDelegate>(
        TDelegate function,
        string? name = null,
        string? description = null) where TDelegate : Delegate
    {
        if (function is null) throw new ArgumentNullException(nameof(function));

        // If name is not provided, use method name as default
        name ??= function.Method.Name;
        description ??= "";

        return AIFunctionFactory.Create(
            function,
            name: name,
            description: description);
    }

    public void Dispose()
    {
        _httpForTokenize.Dispose();
    }
}