using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ResearchEngine.IntegrationTests.Helpers;
using ResearchEngine.IntegrationTests.Infrastructure;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class Synthesis_Run_Idempotency_Tests : IntegrationTestBase
{
    public Synthesis_Run_Idempotency_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task RunSynthesis_Twice_IsIdempotent()
    {
        using var client = CreateClient();

        var jobId = await CreateJobAsync(client, "Test query: synthesis run idempotency.");
        var (status1, _, _) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.Equal("Completed", status1);

        // create new synthesis
        var createResp = await client.PostAsJsonAsync($"/api/jobs/{jobId}/syntheses", new
        {
            useLatestAsParent = true,
            instructions = "Idempotent run test"
        });
        createResp.EnsureSuccessStatusCode();
        var createJson = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var synId = createJson.GetProperty("synthesisId").GetGuid();

        // run #1
        var run1 = await client.PostAsync($"/api/syntheses/{synId}/run", null);
        Assert.True(run1.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.OK);

        // run #2 immediately (should still be Accepted/OK, but not 500)
        var run2 = await client.PostAsync($"/api/syntheses/{synId}/run", null);
        Assert.True(run2.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.OK);
    }
}
