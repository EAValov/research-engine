using System.Net.Http.Json;
using System.Text.Json;
using ResearchEngine.IntegrationTests.Helpers;
using ResearchEngine.IntegrationTests.Infrastructure;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class Learnings_Grouping_SemanticThreshold_Tests : IntegrationTestBase
{
    public Learnings_Grouping_SemanticThreshold_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task AddLearning_ParaphraseText_DoesNotMeet093Threshold_CreatesNewGroup()
    {
        using var client = CreateClient();

        // Create job + wait completion (so DB is initialized and stable)
        var createResp = await client.PostAsJsonAsync("/api/jobs", new
        {
            query = "Grouping test: semantic paraphrase should NOT auto-merge at 0.93 threshold.",
            clarifications = Array.Empty<object>(),
            breadth = 2,
            depth = 2,
            language = "en",
            region = (string?)null,
            webhook = (object?)null
        });
        createResp.EnsureSuccessStatusCode();

        var createJson = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = createJson.GetProperty("jobId").GetGuid();

        var (status, _, _) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.Equal("Completed", status);

        // These are semantically close and plausibly paraphrases, but with the strict threshold (0.93)
        // and a general-purpose fake embedder, we expect them NOT to merge.
        var textA = "Vector embeddings enable nearest neighbor search for retrieval in RAG systems.";
        var textB = "Nearest neighbor search for retrieval in RAG systems is enabled by vector embeddings.";

        var r1 = await client.PostAsJsonAsync($"/api/jobs/{jobId}/learnings", new
        {
            text = textA,
            importanceScore = 0.6f,
            reference = (string?)null,
            evidenceText = "note A",
            language = (string?)null,
            region = (string?)null
        });
        r1.EnsureSuccessStatusCode();

        var j1 = await r1.Content.ReadFromJsonAsync<JsonElement>();
        var g1 = j1.GetProperty("learning").GetProperty("learningGroupId").GetGuid();
        Assert.NotEqual(Guid.Empty, g1);

        var r2 = await client.PostAsJsonAsync($"/api/jobs/{jobId}/learnings", new
        {
            text = textB,
            importanceScore = 0.7f,
            reference = (string?)null,
            evidenceText = "note B",
            language = (string?)null,
            region = (string?)null
        });
        r2.EnsureSuccessStatusCode();

        var j2 = await r2.Content.ReadFromJsonAsync<JsonElement>();
        var g2 = j2.GetProperty("learning").GetProperty("learningGroupId").GetGuid();
        Assert.NotEqual(Guid.Empty, g2);

        // Expected product behavior (current): semantically close != near-duplicate => different groups.
        Assert.NotEqual(g1, g2);
    }
}
