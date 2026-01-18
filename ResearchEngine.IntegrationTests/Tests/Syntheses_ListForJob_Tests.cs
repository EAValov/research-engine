using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ResearchEngine.IntegrationTests.Helpers;
using ResearchEngine.IntegrationTests.Infrastructure;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class Syntheses_ListForJob_Tests : IntegrationTestBase
{
    public Syntheses_ListForJob_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task ListSyntheses_ReturnsAllSynthesesForJob_WithParentAndCounts()
    {
        using var client = CreateClient();

        // 1) initial job run -> creates synthesis #1
        var jobId = await CreateJobAsync(client, "Test query: list syntheses endpoint.");
        var (status1, _, _) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.Equal("Completed", status1);

        var latest1 = await GetLatestSynthesisAsync(client, jobId);
        var syn1Id = latest1.GetProperty("id").GetGuid();
        Assert.NotEqual(Guid.Empty, syn1Id);

        // 2) create synthesis #2 (child) and run it
        var createReq = new
        {
            parentSynthesisId = (Guid?)null,
            useLatestAsParent = true,
            outline = (string?)null,
            instructions = "Create second synthesis for list endpoint"
        };

        var createResp = await client.PostAsJsonAsync($"/api/research/jobs/{jobId}/syntheses", createReq);
        createResp.EnsureSuccessStatusCode();
        var createJson = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var syn2Id = createJson.GetProperty("synthesisId").GetGuid();
        Assert.NotEqual(Guid.Empty, syn2Id);

        var checkpoint = await SseTestHelpers.GetMaxEventIdAsync(client, jobId);

        var runResp = await client.PostAsync($"/api/research/syntheses/{syn2Id}/run", content: null);
        Assert.True(runResp.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.OK);

        var (status2, doneSynId, _) =  await SseTestHelpers.WaitForDoneAfterAsync(client, jobId, checkpoint, TimeSpan.FromSeconds(60));
        Assert.Equal("Completed", status2);
        Assert.True(doneSynId.HasValue);
        Assert.Equal(syn2Id, doneSynId.Value);

        // 3) list syntheses
        var listResp = await client.GetAsync($"/api/research/jobs/{jobId}/syntheses?skip=0&take=50");
        listResp.EnsureSuccessStatusCode();

        var listJson = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(jobId, listJson.GetProperty("jobId").GetGuid());

        var syntheses = listJson.GetProperty("syntheses").EnumerateArray().ToList();
        Assert.True(syntheses.Count >= 2);

        // verify both are present
        Assert.Contains(syntheses, s => s.GetProperty("synthesisId").GetGuid() == syn1Id);
        Assert.Contains(syntheses, s => s.GetProperty("synthesisId").GetGuid() == syn2Id);

        // verify syn2 parent = syn1
        var syn2 = syntheses.Single(s => s.GetProperty("synthesisId").GetGuid() == syn2Id);
        Assert.Equal(syn1Id, syn2.GetProperty("parentSynthesisId").GetGuid());

        // verify sectionCount exists and > 0 for completed syntheses
        foreach (var s in syntheses)
        {
            var status = s.GetProperty("status").GetString();
            if (status == "Completed")
            {
                Assert.True(s.TryGetProperty("sectionCount", out var sectionCountEl));
                Assert.True(sectionCountEl.GetInt32() > 0);
            }
        }
        
        var createdAts = syntheses.Select(s => s.GetProperty("createdAt").GetDateTimeOffset()).ToList();
        for (int i = 1; i < createdAts.Count; i++)
            Assert.True(createdAts[i] <= createdAts[i - 1], "Expected syntheses ordered by createdAt desc.");
    }
}
