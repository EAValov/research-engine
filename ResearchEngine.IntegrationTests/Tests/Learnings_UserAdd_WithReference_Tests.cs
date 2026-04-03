using System.Net.Http.Json;
using System.Text.Json;
using ResearchEngine.IntegrationTests.Helpers;
using ResearchEngine.IntegrationTests.Infrastructure;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class Learnings_UserAdd_WithReference_Tests : IntegrationTestBase
{
    public Learnings_UserAdd_WithReference_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task AddLearning_WithReference_CreatesOrUsesSource_WithSameReference()
    {
        using var client = CreateClient();

        // 1) Create job + wait completion
        var jobId = await CreateJobAsync(client, "Test query: user add learning with reference.");
        var (status, _, _) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.Equal("Completed", status);

        const string reference = "Smith 2020, pp. 12-15";

        // 2) Add learning with explicit reference
        var addReq = new
        {
            text = "User learning: report key limitations explicitly and cite primary sources.",
            importanceScore = 0.9f,
            reference,
            evidenceText = "Book note.",
            language = "en",
            region = (string?)null
        };

        var addResp = await client.PostAsJsonAsync($"/api/jobs/{jobId}/learnings", addReq);
        addResp.EnsureSuccessStatusCode();

        var addJson = await addResp.Content.ReadFromJsonAsync<JsonElement>();
        var learning = addJson.GetProperty("learning");
        var sourceId = learning.GetProperty("sourceId").GetGuid();

        Assert.NotEqual(Guid.Empty, sourceId);

        // 3) Sources list should contain the reference string and matching sourceId
        var sourcesResp = await client.GetAsync($"/api/jobs/{jobId}/sources");
        sourcesResp.EnsureSuccessStatusCode();

        var sourcesJson = await sourcesResp.Content.ReadFromJsonAsync<JsonElement>();
        var sources = sourcesJson.GetProperty("sources").EnumerateArray().ToList();

        var src = sources.Single(s => s.GetProperty("sourceId").GetGuid() == sourceId);
        Assert.Equal(reference, src.GetProperty("reference").GetString());
    }
}
