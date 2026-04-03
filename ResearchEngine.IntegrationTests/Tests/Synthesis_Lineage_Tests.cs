using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ResearchEngine.IntegrationTests.Helpers;
using ResearchEngine.IntegrationTests.Infrastructure;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class Synthesis_Lineage_Tests : IntegrationTestBase
{
    public Synthesis_Lineage_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task Regenerate_CreatesNewSynthesis_WithParentSynthesisId_AndLatestPointsToIt()
    {
        using var client = CreateClient();

        // 1) initial job run (this still auto-runs the job workflow)
        var jobId = await CreateJobAsync(client, "Test query: synthesis lineage.");
        var (status1, syn1FromDone, _) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.Equal("Completed", status1);

        // 2) pull latest synthesis id after initial completion
        var latest1 = await GetLatestSynthesisAsync(client, jobId);
        var syn1Id = latest1.GetProperty("id").GetGuid();
        Assert.NotEqual(Guid.Empty, syn1Id);

        // syn1FromDone can be null depending on your done payload; we trust latest endpoint.
        // But if it is present, it should match latest.
        if (syn1FromDone.HasValue)
            Assert.Equal(syn1Id, syn1FromDone.Value);

        // 3) checkpoint so we only observe events from the regeneration run
        var checkpoint = await SseTestHelpers.GetMaxEventIdAsync(client, jobId);

        // 4) create a new synthesis row (NO long-running work here)
        var createReq = new
        {
            parentSynthesisId = (Guid?)null,
            useLatestAsParent = true,
            outline = (string?)null,
            instructions = "Regenerate with lineage test"
        };

        var createResp = await client.PostAsJsonAsync($"/api/jobs/{jobId}/syntheses", createReq);
        createResp.EnsureSuccessStatusCode();

        var createJson = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var syn2Id = createJson.GetProperty("synthesisId").GetGuid();
        Assert.NotEqual(Guid.Empty, syn2Id);

        // 5) verify syn2 exists and has correct parent set BEFORE running
        var syn2BeforeRunResp = await client.GetAsync($"/api/syntheses/{syn2Id}");
        syn2BeforeRunResp.EnsureSuccessStatusCode();

        var syn2BeforeRun = await syn2BeforeRunResp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(syn2Id, syn2BeforeRun.GetProperty("id").GetGuid());

        // parent is set at creation time
        var parentId = syn2BeforeRun.GetProperty("parentSynthesisId").GetGuid();
        Assert.Equal(syn1Id, parentId);

        // 6) start the synthesis run (Hangfire enqueue)
        var runResp = await client.PostAsync($"/api/syntheses/{syn2Id}/run", content: null);
        Assert.True(
            runResp.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.OK,
            $"Expected 202 Accepted (or 200 OK if already terminal), got {(int)runResp.StatusCode} {runResp.StatusCode}");

        // 7) wait for regen done AFTER checkpoint; ensure it matches syn2
        var (status2, doneSynId, _) =
            await SseTestHelpers.WaitForDoneAfterAsync(client, jobId, checkpoint, TimeSpan.FromSeconds(60));

        Assert.Equal("Completed", status2);
        Assert.True(doneSynId.HasValue);
        Assert.Equal(syn2Id, doneSynId.Value);

        // 8) latest should now be syn2
        var latest2 = await GetLatestSynthesisAsync(client, jobId);
        Assert.Equal(syn2Id, latest2.GetProperty("id").GetGuid());
        Assert.Equal("Completed", latest2.GetProperty("status").GetString());
    }
}
