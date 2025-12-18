using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class SseFrameWriter
{
    private readonly HttpContext _http;
    private readonly JsonSerializerOptions _json;

    public SseFrameWriter(HttpContext httpContext, JsonSerializerOptions? jsonOptions = null)
    {
        _http = httpContext;
        _json = jsonOptions ?? new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull // <-- important
        };
    }

    public Task FlushAsync(CancellationToken ct) => _http.Response.Body.FlushAsync(ct);

    public async Task WriteCommentAsync(string comment, CancellationToken ct)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes($": {comment}\n\n");
        await _http.Response.Body.WriteAsync(bytes, ct);
        await _http.Response.Body.FlushAsync(ct);
    }

    public async Task WriteDataAsync(string data, CancellationToken ct)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes($"data: {data}\n\n");
        await _http.Response.Body.WriteAsync(bytes, ct);
        await _http.Response.Body.FlushAsync(ct);
    }

    public Task WriteJsonDataAsync(object payload, CancellationToken ct)
        => WriteDataAsync(JsonSerializer.Serialize(payload, _json), ct);
}