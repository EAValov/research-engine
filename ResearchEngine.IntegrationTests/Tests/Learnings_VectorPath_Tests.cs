using System.Net.Http.Json;
using System.Text.Json;
using ResearchEngine.IntegrationTests.Helpers;
using ResearchEngine.IntegrationTests.Infrastructure;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class Learnings_VectorPath_Tests : IntegrationTestBase
{
    public Learnings_VectorPath_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task AfterCompletion_LearningsEndpoint_ReturnsItems_WithSourceUrls()
    {
        using var client = CreateClient();

        var createReq = new
        {
            query = "Test query for learnings extraction.",
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

        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        while (true)
        {
            var evResp = await client.GetAsync($"/api/research/jobs/{jobId}/events");
            evResp.EnsureSuccessStatusCode();

            var events = await evResp.Content.ReadFromJsonAsync<JsonElement>();
            var stages = events.EnumerateArray().Select(GenericHelpers.GetStageName).ToList();

            if (stages.Contains("Completed"))
                break;

            if (stages.Contains("Failed"))
                throw new Xunit.Sdk.XunitException("Job failed unexpectedly during learnings test.");

            if (DateTimeOffset.UtcNow > deadline)
                throw new Xunit.Sdk.XunitException("Timed out waiting for job completion.");

            await Task.Delay(300);
        }

        var learningsResp = await client.GetAsync($"/api/research/jobs/{jobId}/learnings?skip=0&take=200");
        learningsResp.EnsureSuccessStatusCode();

        var learningsJson = await learningsResp.Content.ReadFromJsonAsync<JsonElement>();

        var deb = learningsJson.ToString();

        Assert.Equal(jobId, learningsJson.GetProperty("jobId").GetGuid());

        var take = learningsJson.GetProperty("take").GetInt32();
        Assert.InRange(take, 1, 500);

        var items = learningsJson.GetProperty("learnings").EnumerateArray().ToList();
        Assert.True(items.Count > 0);

        foreach (var l in items)
        {
            Assert.NotEqual(Guid.Empty, l.GetProperty("learningId").GetGuid());
            Assert.NotEqual(Guid.Empty, l.GetProperty("sourceId").GetGuid());
            Assert.StartsWith("https://", l.GetProperty("sourceReference").GetString() ?? "");
            Assert.True(l.GetProperty("importanceScore").GetSingle() > 0);
            Assert.False(string.IsNullOrWhiteSpace(l.GetProperty("text").GetString()));
        }

        var sourcesResp = await client.GetAsync($"/api/research/jobs/{jobId}/sources");
        sourcesResp.EnsureSuccessStatusCode();

        var sourcesJson = await sourcesResp.Content.ReadFromJsonAsync<JsonElement>();
        var sources = sourcesJson.GetProperty("sources").EnumerateArray().ToList();

        var sourceIds = sources.Select(s => s.GetProperty("sourceId").GetGuid()).ToHashSet();
        Assert.True(sourceIds.Count > 0);

        foreach (var l in items)
        {
            var sid = l.GetProperty("sourceId").GetGuid();
            Assert.True(sourceIds.Contains(sid), "Each learning.sourceId should exist in sources list.");
        }
    }
}