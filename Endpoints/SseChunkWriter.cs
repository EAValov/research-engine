using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

public sealed class SseChunkWriter
{
    private readonly HttpContext _httpContext;
    private readonly JsonSerializerOptions _serializerOptions;

    public SseChunkWriter(HttpContext httpContext)
    {
        _httpContext = httpContext;
        _serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task WriteChunkAsync(object chunk, CancellationToken token)
    {
        var json = JsonSerializer.Serialize(chunk, _serializerOptions);
        var line = $"data: {json}\n\n";
        var bytes = Encoding.UTF8.GetBytes(line);
        await _httpContext.Response.Body.WriteAsync(bytes, token);
        await _httpContext.Response.Body.FlushAsync(token);
    }

    public async Task WriteDoneAsync(CancellationToken token)
    {
        var bytes = Encoding.UTF8.GetBytes("data: [DONE]\n\n");
        await _httpContext.Response.Body.WriteAsync(bytes, token);
        await _httpContext.Response.Body.FlushAsync(token);
    }

    public async Task WriteErrorChunkAsync(string message, string requestId, long created, string modelName, CancellationToken token)
    {
        var payload = new
        {
            id      = requestId,
            @object = "chat.completion.chunk",
            created,
            model   = modelName,
            choices = new[]
            {
                new
                {
                    index = 0,
                    delta = new
                    {
                        role    = "assistant",
                        content = $"<think>\nError: {message}\n</think>\n"
                    },
                    finish_reason = "error"
                }
            }
        };

        await WriteChunkAsync(payload, token);
        await WriteDoneAsync(token);
    }
}
