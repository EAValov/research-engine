using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ResearchEngine.IntegrationTests.Helpers;
using ResearchEngine.IntegrationTests.Infrastructure;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class Learnings_SoftDelete_Tests : IntegrationTestBase
{
    public Learnings_SoftDelete_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task DeleteLearning_RemovesFromList()
    {
        using var client = CreateClient();

        var jobId = await CreateJobAsync(client, "Test query: soft delete learning.");
        var (status, _, _) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.Equal("Completed", status);

        // Add learning
        var addResp = await client.PostAsJsonAsync($"/api/jobs/{jobId}/learnings", new
        {
            text = "Learning to delete",
            importanceScore = 1.0f,
            reference = (string?)null,
            evidenceText = "note"
        });
        addResp.EnsureSuccessStatusCode();
        var addJson = await addResp.Content.ReadFromJsonAsync<JsonElement>();
        var learningId = addJson.GetProperty("learning").GetProperty("learningId").GetGuid();

        // Delete
        var delResp = await client.DeleteAsync($"/api/jobs/{jobId}/learnings/{learningId}");
        Assert.Equal(HttpStatusCode.NoContent, delResp.StatusCode);

        // List learnings and ensure it's gone
        var listResp = await client.GetAsync($"/api/jobs/{jobId}/learnings?skip=0&take=500");
        listResp.EnsureSuccessStatusCode();

        var listJson = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        var items = listJson.GetProperty("learnings").EnumerateArray().ToList();

        Assert.DoesNotContain(items, x => x.GetProperty("learningId").GetGuid() == learningId);
    }
}