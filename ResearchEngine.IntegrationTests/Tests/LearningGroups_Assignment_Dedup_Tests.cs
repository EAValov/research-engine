using System.Net.Http.Json;
using System.Text.Json;
using ResearchEngine.IntegrationTests.Helpers;
using ResearchEngine.IntegrationTests.Infrastructure;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class LearningGroups_Assignment_Dedup_Tests : IntegrationTestBase
{
    public LearningGroups_Assignment_Dedup_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task AddLearning_Twice_WithSameText_AssignsSameLearningGroup()
    {
        using var client = CreateClient();

        // 1) Create job + wait completion
        var jobId = await CreateJobAsync(client, "Test query: grouping identical user learnings.");
        var (status, _, _) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.Equal("Completed", status);

        const string text = "User learning: embeddings enable semantic retrieval across paraphrases.";

        // 2) Add first learning
        var add1 = new { text, importanceScore = 0.6f, reference = (string?)null, evidenceText = (string?)null, language = (string?)null, region = (string?)null };
        var r1 = await client.PostAsJsonAsync($"/api/jobs/{jobId}/learnings", add1);
        r1.EnsureSuccessStatusCode();

        var j1 = await r1.Content.ReadFromJsonAsync<JsonElement>();
        var g1 = j1.GetProperty("learning").GetProperty("learningGroupId").GetGuid();
        Assert.NotEqual(Guid.Empty, g1);

        // 3) Add second learning with identical text (deterministic embeddings => should re-use nearest group)
        var add2 = new { text, importanceScore = 0.7f, reference = (string?)null, evidenceText = (string?)null, language = (string?)null, region = (string?)null };
        var r2 = await client.PostAsJsonAsync($"/api/jobs/{jobId}/learnings", add2);
        r2.EnsureSuccessStatusCode();

        var j2 = await r2.Content.ReadFromJsonAsync<JsonElement>();
        var g2 = j2.GetProperty("learning").GetProperty("learningGroupId").GetGuid();
        Assert.NotEqual(Guid.Empty, g2);

        Assert.Equal(g1, g2);
    }
}