using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ResearchEngine.IntegrationTests.Infrastructure;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class Jobs_SoftDelete_Tests : IntegrationTestBase
{
    public Jobs_SoftDelete_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task SoftDeleteJob_HidesJobFromListAndGet()
    {
        using var client = CreateClient();

        var jobId = await CreateJobAsync(client, "Test query: soft delete job.");
        Assert.NotEqual(Guid.Empty, jobId);

        // sanity: job exists
        var getBefore = await client.GetAsync($"/api/research/jobs/{jobId}");
        getBefore.EnsureSuccessStatusCode();

        // delete
        using var delReq = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/research/jobs/{jobId}")
        {
            Content = JsonContent.Create(new { reason = "test delete" })
        };

        var delResp = await client.SendAsync(delReq);
        Assert.Equal(HttpStatusCode.NoContent, delResp.StatusCode);

        // list should not include it
        var listResp = await client.GetAsync("/api/research/jobs");
        listResp.EnsureSuccessStatusCode();
        var listJson = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        var jobs = listJson.GetProperty("jobs").EnumerateArray().ToList();
        Assert.DoesNotContain(jobs, j => j.GetProperty("id").GetGuid() == jobId);

        // get should 404 due to query filter
        var getAfter = await client.GetAsync($"/api/research/jobs/{jobId}");
        Assert.Equal(HttpStatusCode.NotFound, getAfter.StatusCode);
    }
}
