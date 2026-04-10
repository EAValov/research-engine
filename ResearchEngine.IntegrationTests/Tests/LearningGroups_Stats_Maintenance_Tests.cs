using System.Net.Http.Json;
using System.Text.Json;
using ResearchEngine.IntegrationTests.Helpers;
using ResearchEngine.IntegrationTests.Infrastructure;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class LearningGroups_Stats_Maintenance_Tests : IntegrationTestBase
{
    public LearningGroups_Stats_Maintenance_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task DeleteLearning_RecomputesGroupMemberCount()
    {
        using var client = CreateClient();

        var jobId = await CreateJobAsync(client, "Test query: group stats maintenance after learning delete.");
        var (status, _, _) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.Equal("Completed", status);

        const string text = "User learning: identical statements should share one group for stats maintenance.";

        async Task<JsonElement> Add(float importance)
        {
            var resp = await client.PostAsJsonAsync($"/api/jobs/{jobId}/learnings", new
            {
                text,
                importanceScore = importance,
                reference = (string?)null,
                evidenceText = "note"
            });
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadFromJsonAsync<JsonElement>();
        }

        var first = await Add(0.4f);
        var second = await Add(0.9f);

        var firstLearningId = first.GetProperty("learning").GetProperty("learningId").GetGuid();
        var secondLearningId = second.GetProperty("learning").GetProperty("learningId").GetGuid();

        var beforeResp = await client.GetAsync($"/api/learnings/{secondLearningId}/group");
        beforeResp.EnsureSuccessStatusCode();
        var before = await beforeResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(before.GetProperty("memberCount").GetInt32() >= 2);

        var deleteResp = await client.DeleteAsync($"/api/jobs/{jobId}/learnings/{firstLearningId}");
        deleteResp.EnsureSuccessStatusCode();

        var afterResp = await client.GetAsync($"/api/learnings/{secondLearningId}/group");
        afterResp.EnsureSuccessStatusCode();
        var after = await afterResp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(1, after.GetProperty("memberCount").GetInt32());
        Assert.Equal(1, after.GetProperty("distinctSourceCount").GetInt32());
    }

    [Fact]
    public async Task DeleteSource_RecomputesDistinctSourceCount()
    {
        using var client = CreateClient();

        var jobId = await CreateJobAsync(client, "Test query: group stats maintenance after source delete.");
        var (status, _, _) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.Equal("Completed", status);

        const string text = "User learning: same claim from two references should keep distinct source counts accurate.";

        async Task<JsonElement> Add(string reference, float importance)
        {
            var resp = await client.PostAsJsonAsync($"/api/jobs/{jobId}/learnings", new
            {
                text,
                importanceScore = importance,
                reference,
                evidenceText = "note"
            });
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadFromJsonAsync<JsonElement>();
        }

        var first = await Add("https://example.com/source-a", 0.6f);
        var second = await Add("https://example.com/source-b", 0.9f);

        var firstSourceId = first.GetProperty("learning").GetProperty("sourceId").GetGuid();
        var secondLearningId = second.GetProperty("learning").GetProperty("learningId").GetGuid();

        var beforeResp = await client.GetAsync($"/api/learnings/{secondLearningId}/group");
        beforeResp.EnsureSuccessStatusCode();
        var before = await beforeResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(before.GetProperty("memberCount").GetInt32() >= 2);
        Assert.True(before.GetProperty("distinctSourceCount").GetInt32() >= 2);

        var deleteResp = await client.DeleteAsync($"/api/jobs/{jobId}/sources/{firstSourceId}");
        deleteResp.EnsureSuccessStatusCode();

        var afterResp = await client.GetAsync($"/api/learnings/{secondLearningId}/group");
        afterResp.EnsureSuccessStatusCode();
        var after = await afterResp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(1, after.GetProperty("memberCount").GetInt32());
        Assert.Equal(1, after.GetProperty("distinctSourceCount").GetInt32());
    }
}
