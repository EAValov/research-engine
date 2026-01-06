using System.Net.Http.Json;
using System.Text.Json;
using ResearchEngine.IntegrationTests.Helpers;
using ResearchEngine.IntegrationTests.Infrastructure;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class Learnings_UserAdd_SourceCreation_Tests : IntegrationTestBase
{
    public Learnings_UserAdd_SourceCreation_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task AddLearning_WithoutReference_UsesUserSource_AndReturnsIds()
    {
        using var client = CreateClient();

        // 1) Create job + wait completion
        var jobId = await CreateJobAsync(client, "Test query: user add learning without reference.");
        var (status, _, _) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.Equal("Completed", status);

        // 2) Add user learning without reference
        var addReq = new
        {
            text = "User learning: base editing can reduce indels vs nuclease editing in some contexts.",
            importanceScore = 1.0f,
            reference = (string?)null,
            evidenceText = "Manual note.",
            language = (string?)null,
            region = (string?)null
        };

        var addResp = await client.PostAsJsonAsync($"/api/research/jobs/{jobId}/learnings", addReq);
        addResp.EnsureSuccessStatusCode();

        var addJson = await addResp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(jobId, addJson.GetProperty("jobId").GetGuid());

        var learning = addJson.GetProperty("learning");
        var learningId = learning.GetProperty("learningId").GetGuid();
        var sourceId = learning.GetProperty("sourceId").GetGuid();
        var groupId = learning.GetProperty("learningGroupId").GetGuid();

        Assert.NotEqual(Guid.Empty, learningId);
        Assert.NotEqual(Guid.Empty, sourceId);
        Assert.NotEqual(Guid.Empty, groupId);

        // 3) Verify source exists in sources list and looks like a user source reference
        var sourcesResp = await client.GetAsync($"/api/research/jobs/{jobId}/sources");
        sourcesResp.EnsureSuccessStatusCode();

        var sourcesJson = await sourcesResp.Content.ReadFromJsonAsync<JsonElement>();
        var sources = sourcesJson.GetProperty("sources").EnumerateArray().ToList();
        Assert.True(sources.Count > 0);

        var src = sources.Single(s => s.GetProperty("sourceId").GetGuid() == sourceId);
        var reference = src.GetProperty("reference").GetString() ?? "";

        Assert.False(string.IsNullOrWhiteSpace(reference));

        // TODO: We don't have "Kind" in the list response, so this is the best observable assertion.
        Assert.StartsWith("user:", reference, StringComparison.OrdinalIgnoreCase);
    }
}