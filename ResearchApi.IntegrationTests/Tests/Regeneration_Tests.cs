using System.Net.Http.Json;
using System.Text.Json;
using ResearchApi.IntegrationTests.Helpers;
using ResearchApi.IntegrationTests.Infrastructure;
using Xunit;

namespace ResearchApi.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class Regeneration_Tests : IntegrationTestBase
{
    public Regeneration_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task Regenerate_CreatesNewSynthesis_WithParentLink_AndLatestPointsToNew()
    {
        using var client = CreateClient();

        // 1) Create job
        var createReq = new
        {
            query = "Test query: create baseline synthesis, then regenerate.",
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
        var jobId = createJson.GetProperty("jobId").GetGuid();
        Assert.NotEqual(Guid.Empty, jobId);

        // 2) Wait for job completion
        await WaitForJobCompletionAsync(client, jobId, timeoutSeconds: 60);

        // 3) Get latest synthesis (baseline)
        var syn1 = await GetLatestSynthesisAsync(client, jobId);
        Assert.Equal("Completed", syn1.GetProperty("status").GetString());
        var syn1Id = syn1.GetProperty("id").GetGuid();
        Assert.NotEqual(Guid.Empty, syn1Id);

        // 4) Start a new synthesis using latest as parent (regenerate)
        var regenReq = new
        {
            parentSynthesisId = (Guid?)null,
            useLatestAsParent = true,
            outline = (string?)null,
            instructions = "Regenerate for test purposes.",
            sourceOverrides = (object?)null,
            learningOverrides = (object?)null
        };

        var startResp = await client.PostAsJsonAsync($"/api/research/jobs/{jobId}/syntheses", regenReq);
        startResp.EnsureSuccessStatusCode();

        var startJson = await startResp.Content.ReadFromJsonAsync<JsonElement>();
        var syn2Id = startJson.GetProperty("synthesisId").GetGuid();
        Assert.NotEqual(Guid.Empty, syn2Id);
        Assert.NotEqual(syn1Id, syn2Id);

        // 5) Poll until synthesis2 completed
        var syn2 = await WaitForSynthesisCompletedAsync(client, syn2Id, timeoutSeconds: 60);
        Assert.Equal("Completed", syn2.GetProperty("status").GetString());

        // 6) Verify lineage
        Assert.True(syn2.TryGetProperty("parentSynthesisId", out var parentEl));
        Assert.Equal(syn1Id, parentEl.GetGuid());

        // 7) Verify latest points to new synthesis
        var latest = await GetLatestSynthesisAsync(client, jobId);
        Assert.Equal(syn2Id, latest.GetProperty("id").GetGuid());
        Assert.Equal("Completed", latest.GetProperty("status").GetString());

        var sections = latest.GetProperty("sections").EnumerateArray().ToList();
        Assert.True(sections.Count > 0);
    }

    private static async Task WaitForJobCompletionAsync(HttpClient client, Guid jobId, int timeoutSeconds)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);

        while (true)
        {
            var evResp = await client.GetAsync($"/api/research/jobs/{jobId}/events");
            evResp.EnsureSuccessStatusCode();

            var events = await evResp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(JsonValueKind.Array, events.ValueKind);

            var stages = events.EnumerateArray().Select(GenericHelpers.GetStageName).ToList();

            if (stages.Contains("Completed"))
                return;

            if (stages.Contains("Failed"))
                throw new Xunit.Sdk.XunitException("Job failed unexpectedly.");

            if (DateTimeOffset.UtcNow > deadline)
                throw new Xunit.Sdk.XunitException("Timed out waiting for job completion.");

            await Task.Delay(300);
        }
    }

    private static async Task<JsonElement> GetLatestSynthesisAsync(HttpClient client, Guid jobId)
    {
        var resp = await client.GetAsync($"/api/research/jobs/{jobId}/syntheses/latest");
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(jobId, json.GetProperty("jobId").GetGuid());

        var syn = json.GetProperty("synthesis");
        Assert.Equal(JsonValueKind.Object, syn.ValueKind);

        return syn;
    }

    private static async Task<JsonElement> WaitForSynthesisCompletedAsync(HttpClient client, Guid synthesisId, int timeoutSeconds)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);

        while (true)
        {
            var resp = await client.GetAsync($"/api/research/syntheses/{synthesisId}");
            resp.EnsureSuccessStatusCode();

            var syn = await resp.Content.ReadFromJsonAsync<JsonElement>();
            var status = syn.GetProperty("status").GetString();

            if (status is "Completed" or "Failed")
                return syn;

            if (DateTimeOffset.UtcNow > deadline)
                throw new Xunit.Sdk.XunitException("Timed out waiting for synthesis completion.");

            await Task.Delay(300);
        }
    }
}