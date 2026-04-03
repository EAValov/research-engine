using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResearchEngine.IntegrationTests.Infrastructure;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class Jobs_Archive_EventStage_Tests : IntegrationTestBase
{
    public Jobs_Archive_EventStage_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task ArchiveAndUnarchive_CompletedJob_EmitsCompletedStage()
    {
        using var client = CreateClient();

        var jobId = await CreateJobAsync(client, "Test query: archive/unarchive completed stage mapping.");
        Assert.NotEqual(Guid.Empty, jobId);

        await WaitForJobCompletionAsync(client, jobId, timeoutSeconds: 60);

        var archiveResp = await client.PostAsync($"/api/jobs/{jobId}/archive", content: null);
        Assert.Equal(HttpStatusCode.NoContent, archiveResp.StatusCode);

        var mainListAfterArchive = await GetJobsAsync(client, archived: false);
        Assert.DoesNotContain(mainListAfterArchive, j => j.GetProperty("id").GetGuid() == jobId);

        var archiveListAfterArchive = await GetJobsAsync(client, archived: true);
        Assert.Contains(archiveListAfterArchive, j => j.GetProperty("id").GetGuid() == jobId);

        var eventsAfterArchive = await GetEventsAsync(client, jobId);
        var archivedEvent = FindLastByMessage(eventsAfterArchive, "Job archived by user");
        Assert.Equal("Completed", GetStageName(archivedEvent));

        var unarchiveResp = await client.PostAsync($"/api/jobs/{jobId}/unarchive", content: null);
        Assert.Equal(HttpStatusCode.NoContent, unarchiveResp.StatusCode);

        var mainListAfterUnarchive = await GetJobsAsync(client, archived: false);
        Assert.Contains(mainListAfterUnarchive, j => j.GetProperty("id").GetGuid() == jobId);

        var archiveListAfterUnarchive = await GetJobsAsync(client, archived: true);
        Assert.DoesNotContain(archiveListAfterUnarchive, j => j.GetProperty("id").GetGuid() == jobId);

        var eventsAfterUnarchive = await GetEventsAsync(client, jobId);
        var unarchivedEvent = FindLastByMessage(eventsAfterUnarchive, "Job unarchived by user");
        Assert.Equal("Completed", GetStageName(unarchivedEvent));
    }

    [Fact]
    public async Task ArchiveAndUnarchive_PendingJob_EmitsPlanningStage()
    {
        await using var noBackgroundFactory = Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBackgroundJobClient>();
                services.AddSingleton<IBackgroundJobClient, NoopBackgroundJobClient>();
            });
        });

        using var client = noBackgroundFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var jobId = await CreateJobAsync(client, "Test query: archive/unarchive pending stage mapping.");
        Assert.NotEqual(Guid.Empty, jobId);

        var jobResp = await client.GetAsync($"/api/jobs/{jobId}");
        jobResp.EnsureSuccessStatusCode();

        var jobJson = await jobResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Pending", jobJson.GetProperty("status").GetString());

        var archiveResp = await client.PostAsync($"/api/jobs/{jobId}/archive", content: null);
        Assert.Equal(HttpStatusCode.NoContent, archiveResp.StatusCode);

        var mainListAfterArchive = await GetJobsAsync(client, archived: false);
        Assert.DoesNotContain(mainListAfterArchive, j => j.GetProperty("id").GetGuid() == jobId);

        var archiveListAfterArchive = await GetJobsAsync(client, archived: true);
        Assert.Contains(archiveListAfterArchive, j => j.GetProperty("id").GetGuid() == jobId);

        var eventsAfterArchive = await GetEventsAsync(client, jobId);
        var archivedEvent = FindLastByMessage(eventsAfterArchive, "Job archived by user");
        Assert.Equal("Planning", GetStageName(archivedEvent));

        var unarchiveResp = await client.PostAsync($"/api/jobs/{jobId}/unarchive", content: null);
        Assert.Equal(HttpStatusCode.NoContent, unarchiveResp.StatusCode);

        var mainListAfterUnarchive = await GetJobsAsync(client, archived: false);
        Assert.Contains(mainListAfterUnarchive, j => j.GetProperty("id").GetGuid() == jobId);

        var archiveListAfterUnarchive = await GetJobsAsync(client, archived: true);
        Assert.DoesNotContain(archiveListAfterUnarchive, j => j.GetProperty("id").GetGuid() == jobId);

        var eventsAfterUnarchive = await GetEventsAsync(client, jobId);
        var unarchivedEvent = FindLastByMessage(eventsAfterUnarchive, "Job unarchived by user");
        Assert.Equal("Planning", GetStageName(unarchivedEvent));
    }

    private static async Task<List<JsonElement>> GetEventsAsync(HttpClient client, Guid jobId)
    {
        var eventsResp = await client.GetAsync($"/api/jobs/{jobId}/events");
        eventsResp.EnsureSuccessStatusCode();

        var eventsJson = await eventsResp.Content.ReadFromJsonAsync<JsonElement>();
        return eventsJson.EnumerateArray().ToList();
    }

    private static async Task<List<JsonElement>> GetJobsAsync(HttpClient client, bool archived)
    {
        var suffix = archived ? "?archived=true" : string.Empty;
        var listResp = await client.GetAsync($"/api/jobs{suffix}");
        listResp.EnsureSuccessStatusCode();

        var listJson = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        return listJson.GetProperty("jobs").EnumerateArray().ToList();
    }

    private static JsonElement FindLastByMessage(IReadOnlyList<JsonElement> events, string message)
    {
        var found = events.LastOrDefault(e => string.Equals(e.GetProperty("message").GetString(), message, StringComparison.Ordinal));
        Assert.NotEqual(JsonValueKind.Undefined, found.ValueKind);
        return found;
    }

    private sealed class NoopBackgroundJobClient : IBackgroundJobClient
    {
        public string Create(Job job, IState state) => $"noop:{Guid.NewGuid():N}";
        public bool ChangeState(string jobId, IState state, string expectedState) => false;
        public bool Delete(string jobId) => false;
        public bool Requeue(string jobId) => false;
    }
}
