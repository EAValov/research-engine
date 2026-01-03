using System.Net.Http.Json;
using System.Text.Json;
using ResearchEngine.IntegrationTests.Helpers;
using ResearchEngine.IntegrationTests.Infrastructure;
using Xunit;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class Synthesis_Lineage_Tests : IntegrationTestBase
{
    public Synthesis_Lineage_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task Regenerate_CreatesNewSynthesis_WithParentSynthesisId_AndLatestPointsToIt()
    {
        using var client = CreateClient();

        // initial job run
        var jobId = await CreateJobAsync(client, "Test query: synthesis lineage.");
        var (status1, syn1, _) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.Equal("Completed", status1);

        // pull latest synthesis id after initial completion
        var latest1 = await GetLatestSynthesisAsync(client, jobId);
        var syn1Id = latest1.GetProperty("id").GetGuid();
        Assert.NotEqual(Guid.Empty, syn1Id);

        // checkpoint
        var checkpoint = await SseTestHelpers.GetMaxEventIdAsync(client, jobId);

        // start regen using latest as parent
        var startReq = new
        {
            parentSynthesisId = (Guid?)null,
            useLatestAsParent = true,
            outline = (string?)null,
            instructions = "Regenerate with lineage test",
            sourceOverrides = (object[]?)null,
            learningOverrides = (object[]?)null
        };

        var startResp = await client.PostAsJsonAsync($"/api/research/jobs/{jobId}/syntheses", startReq);
        startResp.EnsureSuccessStatusCode();

        var startJson = await startResp.Content.ReadFromJsonAsync<JsonElement>();
        var expectedSyn2Id = startJson.GetProperty("synthesisId").GetGuid();

        // wait for regen done AFTER checkpoint
        var (status2, doneSyn2Id, _) =
            await SseTestHelpers.WaitForDoneAfterAsync(client, jobId, checkpoint, TimeSpan.FromSeconds(60));

        Assert.Equal("Completed", status2);
        Assert.True(doneSyn2Id.HasValue);
        Assert.Equal(expectedSyn2Id, doneSyn2Id.Value);

        // fetch synthesis 2 and check parent linkage
        var syn2Resp = await client.GetAsync($"/api/research/syntheses/{doneSyn2Id.Value}");
        syn2Resp.EnsureSuccessStatusCode();

        var syn2Json = await syn2Resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(doneSyn2Id.Value, syn2Json.GetProperty("id").GetGuid());

        var parentId = syn2Json.GetProperty("parentSynthesisId").GetGuid();
        Assert.Equal(syn1Id, parentId);

        // latest should now be syn2
        var latest2 = await GetLatestSynthesisAsync(client, jobId);
        Assert.Equal(doneSyn2Id.Value, latest2.GetProperty("id").GetGuid());
        Assert.Equal("Completed", latest2.GetProperty("status").GetString());
    }
}
