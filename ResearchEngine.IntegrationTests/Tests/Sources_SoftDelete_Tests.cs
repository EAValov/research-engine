using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ResearchEngine.IntegrationTests.Helpers;
using ResearchEngine.IntegrationTests.Infrastructure;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class Sources_SoftDelete_Tests : IntegrationTestBase
{
    public Sources_SoftDelete_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task DeleteSource_RemovesSourceAndItsLearningsFromLists()
    {
        using var client = CreateClient();

        var jobId = await CreateJobAsync(client, "Test query: soft delete source.");
        var (status, _, _) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.Equal("Completed", status);

        // Add learning WITH reference so it's observable in sources list (not user:manual)
        var addResp = await client.PostAsJsonAsync($"/api/research/jobs/{jobId}/learnings", new
        {
            text = "Learning under deletable source",
            importanceScore = 1.0f,
            reference = "https://example.com/paper#p12-15",
            evidenceText = "note",
            language = (string?)null,
            region = (string?)null
        });
        addResp.EnsureSuccessStatusCode();
        var addJson = await addResp.Content.ReadFromJsonAsync<JsonElement>();
        var learningId = addJson.GetProperty("learning").GetProperty("learningId").GetGuid();
        var sourceId = addJson.GetProperty("learning").GetProperty("sourceId").GetGuid();

        // Delete source
        var delResp = await client.DeleteAsync($"/api/research/jobs/{jobId}/sources/{sourceId}");
        Assert.Equal(HttpStatusCode.NoContent, delResp.StatusCode);

        // Sources list: sourceId should be absent
        var sourcesResp = await client.GetAsync($"/api/research/jobs/{jobId}/sources");
        sourcesResp.EnsureSuccessStatusCode();
        var sourcesJson = await sourcesResp.Content.ReadFromJsonAsync<JsonElement>();
        var sources = sourcesJson.GetProperty("sources").EnumerateArray().ToList();

        Assert.DoesNotContain(sources, s => s.GetProperty("sourceId").GetGuid() == sourceId);

        // Learnings list: learningId should be absent
        var learnResp = await client.GetAsync($"/api/research/jobs/{jobId}/learnings?skip=0&take=500");
        learnResp.EnsureSuccessStatusCode();
        var learnJson = await learnResp.Content.ReadFromJsonAsync<JsonElement>();
        var learnings = learnJson.GetProperty("learnings").EnumerateArray().ToList();

        Assert.DoesNotContain(learnings, l => l.GetProperty("learningId").GetGuid() == learningId);
    }
}