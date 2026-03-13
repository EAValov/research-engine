namespace ResearchEngine.API.OpenAI;

public sealed class OpenAiChatStreamWriter
{
    private readonly SseFrameWriter _sse;

    private bool _thinkOpen;

    public OpenAiChatStreamWriter(SseFrameWriter sse) => _sse = sse;

    private sealed class Delta
    {
        public string? Role { get; init; }
        public string? Content { get; init; }
    }

    public Task WriteChunkAsync(object chunk, CancellationToken ct) => _sse.WriteJsonDataAsync(chunk, ct);

    public Task WriteDoneAsync(CancellationToken ct) => _sse.WriteDataAsync("[DONE]", ct);

    public Task WriteTextDeltaAsync(
        string requestId, long created, string modelName,
        string? role, string content, string? finishReason,
        CancellationToken ct)
    {
        var payload = new
        {
            id = requestId,
            @object = "chat.completion.chunk",
            created,
            model = modelName,
            choices = new[]
            {
                new
                {
                    index = 0,
                    delta = new Delta { Role = role, Content = content },
                    finish_reason = finishReason
                }
            }
        };

        return WriteChunkAsync(payload, ct);
    }

    public async Task WriteErrorAsync(string requestId, long created, string modelName, string message, CancellationToken ct)
    {
        await WriteTextDeltaAsync(
            requestId, created, modelName,
            role: "assistant",
            content: $"Error: {message}\n",
            finishReason: "error",
            ct);

        await WriteDoneAsync(ct);
    }

      public async Task BeginThinkAsync(string requestId, long created, string modelName, CancellationToken ct)
    {
        if (_thinkOpen) return;
        _thinkOpen = true;

        // role only once at the start of the stream
        await WriteTextDeltaAsync(requestId, created, modelName, role: "assistant", content: "<think>\n", finishReason: null, ct);
    }

    public async Task WriteThinkAsync(string requestId, long created, string modelName, string content, CancellationToken ct)
    {
        if (!_thinkOpen)
            await BeginThinkAsync(requestId, created, modelName, ct);

        await WriteTextDeltaAsync(requestId, created, modelName, role: null, content: content, finishReason: null, ct);
    }

    public async Task EndThinkAsync(string requestId, long created, string modelName, CancellationToken ct)
    {
        if (!_thinkOpen) return;
        _thinkOpen = false;

        await WriteTextDeltaAsync(requestId, created, modelName, role: null, content: "\n</think>\n", finishReason: null, ct);
    }

    // Optional: send a proper “stop” terminator chunk (some UIs behave better)
    public Task WriteStopAsync(string requestId, long created, string modelName, CancellationToken ct)
        => WriteTextDeltaAsync(requestId, created, modelName, role: null, content: "", finishReason: "stop", ct: ct);
}