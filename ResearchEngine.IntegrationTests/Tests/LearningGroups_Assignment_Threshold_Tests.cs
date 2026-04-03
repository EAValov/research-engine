using System.Net.Http.Json;
using System.Text.Json;
using ResearchEngine.IntegrationTests.Helpers;
using ResearchEngine.IntegrationTests.Infrastructure;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class LearningGroups_Assignment_Threshold_Tests : IntegrationTestBase
{
    public LearningGroups_Assignment_Threshold_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task AddLearning_WithDifferentText_CreatesDifferentLearningGroup()
    {
        using var client = CreateClient();

        // 1) Create job + wait completion
        var jobId = await CreateJobAsync(client, "Test query: grouping threshold different text.");
        var (status, _, _) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.Equal("Completed", status);

        // NOTE:
        // With FakeEmbeddingModel vectors (hash-expanded), different text yields very different vectors.
        // With GroupAssignSimilarityThreshold=0.93, grouping should NOT happen for different text.
        var text1 = "User learning: semantic embeddings support similarity search.";
        var text2 = "User learning: vector representations enable nearest-neighbor retrieval."; // different content

        // 2) Add first learning
        var add1 = new
        {
            text = text1,
            importanceScore = 0.6f,
            reference = (string?)null,
            evidenceText = (string?)null,
            language = (string?)null,
            region = (string?)null
        };

        var r1 = await client.PostAsJsonAsync($"/api/jobs/{jobId}/learnings", add1);
        r1.EnsureSuccessStatusCode();

        var j1 = await r1.Content.ReadFromJsonAsync<JsonElement>();
        var g1 = j1.GetProperty("learning").GetProperty("learningGroupId").GetGuid();
        Assert.NotEqual(Guid.Empty, g1);

        // 3) Add second learning with different text
        var add2 = new
        {
            text = text2,
            importanceScore = 0.7f,
            reference = (string?)null,
            evidenceText = (string?)null,
            language = (string?)null,
            region = (string?)null
        };

        var r2 = await client.PostAsJsonAsync($"/api/jobs/{jobId}/learnings", add2);
        r2.EnsureSuccessStatusCode();

        var j2 = await r2.Content.ReadFromJsonAsync<JsonElement>();
        var g2 = j2.GetProperty("learning").GetProperty("learningGroupId").GetGuid();
        Assert.NotEqual(Guid.Empty, g2);

        Assert.NotEqual(g1, g2);
    }
}