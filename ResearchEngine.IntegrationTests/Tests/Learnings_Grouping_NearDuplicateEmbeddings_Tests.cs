using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResearchEngine.Domain;
using ResearchEngine.IntegrationTests.Helpers;
using ResearchEngine.IntegrationTests.Infrastructure;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class Learnings_Grouping_NearDuplicateEmbeddings_Tests : IntegrationTestBase
{
    public Learnings_Grouping_NearDuplicateEmbeddings_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task AddLearning_NearDuplicateText_AssignsToSameGroup_WithNearDuplicateEmbeddings()
    {
        // We override only the embedder: everything else stays identical to IntegrationTestBase.
        // This makes the grouping deterministic for near-duplicate cases while keeping production threshold unchanged.
        await using var factory = Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEmbeddingModel>();
                services.AddSingleton<IEmbeddingModel, NearDuplicateFakeEmbeddingModel>();
            });
        });

        using var client = factory.CreateClient();

        var createResp = await client.PostAsJsonAsync("/api/research/jobs", new
        {
            query = "Grouping test: near-duplicate texts should merge at 0.93 threshold.",
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

        // Near-duplicate pair: same tokens, different order / minor phrasing.
        // Our NearDuplicateFakeEmbeddingModel normalizes aggressively so cosine similarity should exceed 0.93.
        var textA = "Base editing reduces double-strand breaks compared to classical CRISPR editing.";
        var textB = "Compared to classical CRISPR editing, base editing reduces double-strand breaks.";

        var r1 = await client.PostAsJsonAsync($"/api/research/jobs/{jobId}/learnings", new
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

        var r2 = await client.PostAsJsonAsync($"/api/research/jobs/{jobId}/learnings", new
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

        // Expected product behavior (current): near-duplicate => same group.
        Assert.Equal(g1, g2);
    }
}