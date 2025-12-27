using System.Text;
using System.Text.Json;

namespace ResearchApi.IntegrationTests.Helpers;

public sealed record SseFrame(string Id, string Event, string DataJson);

public static class SseReader
{
    public static async IAsyncEnumerable<SseFrame> ReadAsync(
        Stream stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);

        string? id = null;
        string? ev = null;
        var data = new StringBuilder();

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null)
            {
                // flush partial frame if server closed without trailing blank line
                if (id is not null || ev is not null || data.Length > 0)
                {
                    yield return new SseFrame(id ?? "", ev ?? "message", data.ToString());
                }
                yield break;
            }

            if (line.Length == 0)
            {
                if (id is not null || ev is not null || data.Length > 0)
                {
                    yield return new SseFrame(id ?? "", ev ?? "message", data.ToString());
                    id = null;
                    ev = null;
                    data.Clear();
                }
                continue;
            }

            if (line.StartsWith("id: ", StringComparison.Ordinal))
            {
                id = line.AsSpan(4).Trim().ToString();
                continue;
            }

            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                ev = line.AsSpan(7).Trim().ToString();
                continue;
            }

            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                if (data.Length > 0) data.Append('\n');
                data.Append(line.AsSpan(6));
                continue;
            }

            // ignore other SSE fields (retry:, etc.)
        }
    }

    public static JsonDocument ParseJson(SseFrame frame) => JsonDocument.Parse(frame.DataJson);
}