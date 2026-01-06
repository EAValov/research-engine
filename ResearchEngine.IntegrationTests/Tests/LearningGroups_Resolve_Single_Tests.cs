using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ResearchEngine.IntegrationTests.Helpers;
using ResearchEngine.IntegrationTests.Infrastructure;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class LearningGroups_Resolve_Single_Tests : IntegrationTestBase
{
    public LearningGroups_Resolve_Single_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task GetGroupByLearningId_ReturnsCardWithEvidence()
    {
        using var client = CreateClient();

        var jobId = await CreateJobAsync(client, "Test query: group resolver single.");
        var (status, _, _) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.Equal("Completed", status);

        // Add learning
        var addResp = await client.PostAsJsonAsync($"/api/research/jobs/{jobId}/learnings", new
        {
            text = "User learning: CRISPR base editors can reduce DSB-associated toxicity in some contexts.",
            importanceScore = 1.0f,
            reference = "Some book p. 12-15",
            evidenceText = "Manual note.",
            language = (string?)null,
            region = (string?)null
        });
        addResp.EnsureSuccessStatusCode();
        var addJson = await addResp.Content.ReadFromJsonAsync<JsonElement>();

        var learningId = addJson.GetProperty("learning").GetProperty("learningId").GetGuid();
        Assert.NotEqual(Guid.Empty, learningId);

        // Resolve group
        var groupResp = await client.GetAsync($"/api/research/learnings/{learningId}/group");
        groupResp.EnsureSuccessStatusCode();

        var card = await groupResp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.NotEqual(Guid.Empty, card.GetProperty("groupId").GetGuid());
        Assert.False(string.IsNullOrWhiteSpace(card.GetProperty("canonicalText").GetString()));
        Assert.True(card.GetProperty("memberCount").GetInt32() >= 1);

        var evidence = card.GetProperty("evidence").EnumerateArray().ToList();
        Assert.True(evidence.Count >= 1);
        Assert.NotEqual(Guid.Empty, evidence[0].GetProperty("learningId").GetGuid());
    }
}