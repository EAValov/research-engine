// ResearchApi.IntegrationTests/Tests/VectorSearch_RespectsOverrides_Tests.cs

using System.Net.Http.Json;
using System.Text.Json;
using ResearchApi.IntegrationTests.Helpers;
using ResearchApi.IntegrationTests.Infrastructure;
using Xunit;

namespace ResearchApi.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class VectorSearch_RespectsOverrides_Tests : IntegrationTestBase
{
    public VectorSearch_RespectsOverrides_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task Regenerate_WithExcludedLearning_DoesNotCiteIt_InSynthesisSections()
    {
        using var client = CreateClient();

        // initial job run
        var jobId = await CreateJobAsync(client, "Test query: overrides affect retrieval.");
        var (status1, _, _) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.Equal("Completed", status1);

        // pick a learning to exclude
        var learnings = await ListLearningsAsync(client, jobId);
        Assert.True(learnings.Count > 0);
        var excludedLearningId = learnings[0].GetProperty("learningId").GetGuid();
        Assert.NotEqual(Guid.Empty, excludedLearningId);

        // checkpoint
        var checkpoint = await SseTestHelpers.GetMaxEventIdAsync(client, jobId);

        // regen with learning override excluded=true
        var startReq = new
        {
            parentSynthesisId = (Guid?)null,
            useLatestAsParent = true,
            outline = (string?)null,
            instructions = "Ensure you cite evidence learnings using [lrn:...] markers.",
            sourceOverrides = (object[]?)null,
            learningOverrides = new object[]
            {
                new { learningId = excludedLearningId, scoreOverride = (float?)null, excluded = true, pinned = (bool?)null }
            }
        };

        var startResp = await client.PostAsJsonAsync($"/api/research/jobs/{jobId}/syntheses", startReq);
        startResp.EnsureSuccessStatusCode();

        var startJson = await startResp.Content.ReadFromJsonAsync<JsonElement>();
        var expectedSynId = startJson.GetProperty("synthesisId").GetGuid();

        var (status2, doneSynId, _) =
            await SseTestHelpers.WaitForDoneAfterAsync(client, jobId, checkpoint, TimeSpan.FromSeconds(60));

        Assert.Equal("Completed", status2);
        Assert.True(doneSynId.HasValue);
        Assert.Equal(expectedSynId, doneSynId.Value);

        // fetch synthesis and ensure none of the sections contain the excluded [lrn:...]
        var synResp = await client.GetAsync($"/api/research/syntheses/{doneSynId.Value}");
        synResp.EnsureSuccessStatusCode();

        var synJson = await synResp.Content.ReadFromJsonAsync<JsonElement>();
        var sections = synJson.GetProperty("sections").EnumerateArray().ToList();
        Assert.True(sections.Count > 0);

        var needle = $"[lrn:{excludedLearningId:N}]";
        foreach (var s in sections)
        {
            var body = s.GetProperty("contentMarkdown").GetString() ?? "";
            Assert.DoesNotContain(needle, body);
        }
    }

    private static async Task<List<JsonElement>> ListLearningsAsync(HttpClient client, Guid jobId)
    {
        var resp = await client.GetAsync($"/api/research/jobs/{jobId}/learnings?skip=0&take=200");
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("learnings").EnumerateArray().ToList();
    }

    private static async Task<Guid> CreateJobAsync(HttpClient client, string query)
    {
        var createReq = new
        {
            query,
            clarifications = Array.Empty<object>(),
            breadth = 2,
            depth = 2,
            language = "en",
            region = (string?)null,
            webhook = (object?)null
        };

        var resp = await client.PostAsJsonAsync("/api/research/jobs", createReq);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("jobId").GetGuid();
    }
}