using System.Net.Http.Json;
using System.Text.Json;
using ResearchApi.IntegrationTests.Helpers;
using ResearchApi.IntegrationTests.Infrastructure;
using Xunit;

namespace ResearchApi.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class JobRun_EndToEnd_Tests : IntegrationTestBase
{
    public JobRun_EndToEnd_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task JobRun_SseTerminates_WithCompleted_AndLatestSynthesisIsCompleted()
    {
        using var client = CreateClient();

        var createReq = new
        {
            query = "Test query: explain the topic and list key takeaways.",
            clarifications = Array.Empty<object>(),
            breadth = 2,
            depth = 2,
            language = "en",
            region = (string?)null,
            webhook = (object?)null
        };

        var createResp = await client.PostAsJsonAsync("/api/research/jobs", createReq);
        createResp.EnsureSuccessStatusCode();

        var createJson = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(createJson.TryGetProperty("jobId", out var jobIdEl));
        var jobId = jobIdEl.GetGuid();
        Assert.NotEqual(Guid.Empty, jobId);

        using var sseReq = new HttpRequestMessage(HttpMethod.Get, $"/api/research/jobs/{jobId}/events/stream");
        sseReq.Headers.Add("Accept", "text/event-stream");

        using var sseResp = await client.SendAsync(sseReq, HttpCompletionOption.ResponseHeadersRead);
        sseResp.EnsureSuccessStatusCode();

        var stages = new List<string>();
        string? terminalStage = null;

        using var sseStream = await sseResp.Content.ReadAsStreamAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        await foreach (var frame in SseReader.ReadAsync(sseStream, cts.Token))
        {
            Assert.False(string.IsNullOrWhiteSpace(frame.Event));

            if (frame.Event == "event")
            {
                using var doc = SseReader.ParseJson(frame);
                var stage = GenericHelpers.GetStageName(doc.RootElement.GetProperty("stage"));
                Assert.False(string.IsNullOrWhiteSpace(stage));
                stages.Add(stage!);
            }

            if (frame.Event == "done")
            {
                using var doc = SseReader.ParseJson(frame);
                var status = doc.RootElement.GetProperty("status").GetString();
                Assert.True(status is "Completed" or "Failed");
                terminalStage ??= status;
                break;
            }
        }

        Assert.Equal("Completed", terminalStage);
        Assert.Contains("Planning", stages);
        Assert.Contains("Searching", stages);
        Assert.Contains("LearningExtraction", stages);

        var synResp = await client.GetAsync($"/api/research/jobs/{jobId}/syntheses/latest");
        synResp.EnsureSuccessStatusCode();

        var synJson = await synResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(jobId, synJson.GetProperty("jobId").GetGuid());

        var synthesis = synJson.GetProperty("synthesis");
        Assert.Equal(JsonValueKind.Object, synthesis.ValueKind);

        Assert.Equal("Completed", synthesis.GetProperty("status").GetString());

        Assert.True(synthesis.TryGetProperty("completedAt", out var completedAt));
        Assert.NotEqual(JsonValueKind.Null, completedAt.ValueKind);

        var sections = synthesis.GetProperty("sections").EnumerateArray().ToList();
        Assert.True(sections.Count > 0);

        var indices = sections.Select(s => s.GetProperty("index").GetInt32()).ToList();
        for (int i = 1; i < indices.Count; i++)
            Assert.True(indices[i] >= indices[i - 1], "Sections must be returned in ascending index order.");

        var conclusionFlags = sections.Select(s => s.GetProperty("isConclusion").GetBoolean()).ToList();
        Assert.Equal(1, conclusionFlags.Count(b => b));
        Assert.True(conclusionFlags[^1], "Conclusion section must be last.");
    }
}