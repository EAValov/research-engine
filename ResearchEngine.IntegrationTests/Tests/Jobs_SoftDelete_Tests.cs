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

        // recent list should not include it
        var recentJobs = await GetJobsAsync(client, archived: false);
        Assert.DoesNotContain(recentJobs, j => j.GetProperty("id").GetGuid() == jobId);

        // archive list should not include it either
        var archivedJobs = await GetJobsAsync(client, archived: true);
        Assert.DoesNotContain(archivedJobs, j => j.GetProperty("id").GetGuid() == jobId);

        // get should 404 due to query filter
        var getAfter = await client.GetAsync($"/api/research/jobs/{jobId}");
        Assert.Equal(HttpStatusCode.NotFound, getAfter.StatusCode);

        // events should also 404 because the job is soft deleted
        var eventsAfter = await client.GetAsync($"/api/research/jobs/{jobId}/events");
        Assert.Equal(HttpStatusCode.NotFound, eventsAfter.StatusCode);
    }

    [Fact]
    public async Task SoftDeleteArchivedJob_HidesJobFromRecentAndArchiveLists()
    {
        using var client = CreateClient();

        var jobId = await CreateJobAsync(client, "Test query: archive then soft delete job.");
        Assert.NotEqual(Guid.Empty, jobId);

        var archiveResp = await client.PostAsync($"/api/research/jobs/{jobId}/archive", content: null);
        Assert.Equal(HttpStatusCode.NoContent, archiveResp.StatusCode);

        var archivedJobsBeforeDelete = await GetJobsAsync(client, archived: true);
        Assert.Contains(archivedJobsBeforeDelete, j => j.GetProperty("id").GetGuid() == jobId);

        using var delReq = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/research/jobs/{jobId}")
        {
            Content = JsonContent.Create(new { reason = "test delete archived job" })
        };

        var delResp = await client.SendAsync(delReq);
        Assert.Equal(HttpStatusCode.NoContent, delResp.StatusCode);

        var recentJobsAfterDelete = await GetJobsAsync(client, archived: false);
        Assert.DoesNotContain(recentJobsAfterDelete, j => j.GetProperty("id").GetGuid() == jobId);

        var archivedJobsAfterDelete = await GetJobsAsync(client, archived: true);
        Assert.DoesNotContain(archivedJobsAfterDelete, j => j.GetProperty("id").GetGuid() == jobId);

        var getAfterDelete = await client.GetAsync($"/api/research/jobs/{jobId}");
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);

        var eventsAfterDelete = await client.GetAsync($"/api/research/jobs/{jobId}/events");
        Assert.Equal(HttpStatusCode.NotFound, eventsAfterDelete.StatusCode);
    }

    private static async Task<List<JsonElement>> GetJobsAsync(HttpClient client, bool archived)
    {
        var suffix = archived ? "?archived=true" : string.Empty;
        var listResp = await client.GetAsync($"/api/research/jobs{suffix}");
        listResp.EnsureSuccessStatusCode();

        var listJson = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        return listJson.GetProperty("jobs").EnumerateArray().ToList();
    }
}
