using System.Net.Http.Json;
using System.Text.Json;

namespace ResearchEngine.IntegrationTests.Helpers;

public static class SseTestHelpers
{
    public static async Task<(string Status, Guid? SynthesisId, int? DoneEventId)> WaitForDoneAsync(
        HttpClient client,
        Guid jobId,
        TimeSpan timeout)
    {
        // No checkpoint: will return the first done in replay+live stream.
        return await WaitForDoneAfterAsync(client, jobId, afterEventId: 0, timeout);
    }

    public static async Task<(string Status, Guid? SynthesisId, int? DoneEventId)> WaitForDoneAfterAsync(
        HttpClient client,
        Guid jobId,
        int afterEventId,
        TimeSpan timeout)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/research/jobs/{jobId}/events/stream");
        req.Headers.Add("Accept", "text/event-stream");
        req.Headers.Add("Last-Event-ID", afterEventId.ToString());

        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();

        using var stream = await resp.Content.ReadAsStreamAsync();
        using var cts = new CancellationTokenSource(timeout);

        await foreach (var frame in SseReader.ReadAsync(stream, cts.Token))
        {
            if (frame.Event != "done")
                continue;

            int? doneEventId = null;
            if (int.TryParse(frame.Id, out var parsedId))
                doneEventId = parsedId;

            using var doc = SseReader.ParseJson(frame);

            var status = doc.RootElement.GetProperty("status").GetString();
            Assert.True(status is "Completed" or "Failed");

            Guid? synthesisId = null;
            if (doc.RootElement.TryGetProperty("synthesisId", out var synEl) &&
                synEl.ValueKind == JsonValueKind.String &&
                Guid.TryParse(synEl.GetString(), out var syn))
            {
                synthesisId = syn;
            }

            return (status!, synthesisId, doneEventId);
        }

        throw new Xunit.Sdk.XunitException("Timed out waiting for done SSE event.");
    }

    public static async Task<int> GetMaxEventIdAsync(HttpClient client, Guid jobId)
    {
        var resp = await client.GetAsync($"/api/research/jobs/{jobId}/events");
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        if (json.ValueKind != JsonValueKind.Array)
            return 0;

        var max = 0;
        foreach (var e in json.EnumerateArray())
        {
            if (e.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number)
                max = Math.Max(max, idEl.GetInt32());
        }

        return max;
    }
}