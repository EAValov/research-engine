using System.Net.Http.Json;
using System.Text.Json;
using ResearchEngine.IntegrationTests.Helpers;
using ResearchEngine.IntegrationTests.Infrastructure;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class Overrides_ScoreOverride_AffectsCitationOrder_Tests : IntegrationTestBase
{
    public Overrides_ScoreOverride_AffectsCitationOrder_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task Regenerate_WithScoreOverride_BoostsLearning_CitedBeforeBaseline()
    {
        using var client = CreateClient();

        // 1) create job + wait done
        var createReq = new
        {
            query = "Test query for score override ordering.",
            clarifications = Array.Empty<object>(),
            breadth = 2,
            depth = 2,
            language = "en",
            region = (string?)null,
            webhook = (object?)null
        };

        var createResp = await client.PostAsJsonAsync("/api/research/jobs", createReq);
        createResp.EnsureSuccessStatusCode();

        var createJson = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = createJson.GetProperty("jobId").GetGuid();

        var (status, _, _) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.Equal("Completed", status);

        // 2) pick two learnings
        var learningsResp = await client.GetAsync($"/api/research/jobs/{jobId}/learnings?skip=0&take=200");
        learningsResp.EnsureSuccessStatusCode();

        var learningsJson = await learningsResp.Content.ReadFromJsonAsync<JsonElement>();
        var items = learningsJson.GetProperty("learnings").EnumerateArray().ToList();
        Assert.True(items.Count >= 2);

        var baselineId = items[0].GetProperty("learningId").GetGuid();
        var boostedId  = items[1].GetProperty("learningId").GetGuid();
        Assert.NotEqual(Guid.Empty, baselineId);
        Assert.NotEqual(Guid.Empty, boostedId);

        // 3) checkpoint
        var afterEventId = await SseTestHelpers.GetMaxEventIdAsync(client, jobId);

        // 4) start regeneration with scoreOverride boosting the second learning
        var regenReq = new
        {
            parentSynthesisId = (Guid?)null,
            useLatestAsParent = true,
            outline = (string?)null,
            instructions = "Use retrieved learnings and include citations.",
            sourceOverrides = (object?)null,
            learningOverrides = new[]
            {
                new { learningId = boostedId, scoreOverride = 1.0f, excluded = (bool?)null, pinned = (bool?)null }
            }
        };

        var regenResp = await client.PostAsJsonAsync($"/api/research/jobs/{jobId}/syntheses", regenReq);
        regenResp.EnsureSuccessStatusCode();

        var regenJson = await regenResp.Content.ReadFromJsonAsync<JsonElement>();
        var expectedSynthesisId = regenJson.GetProperty("synthesisId").GetGuid();

        // 5) wait done after checkpoint
        var (synStatus, doneSynId, _) = await SseTestHelpers.WaitForDoneAfterAsync(
            client,
            jobId,
            afterEventId,
            TimeSpan.FromSeconds(60));

        Assert.Equal("Completed", synStatus);
        Assert.True(doneSynId.HasValue);
        Assert.Equal(expectedSynthesisId, doneSynId.Value);

        // 6) pull synthesis and compare citation positions
        var synResp = await client.GetAsync($"/api/research/syntheses/{expectedSynthesisId}");
        synResp.EnsureSuccessStatusCode();

        var synDoc = await synResp.Content.ReadFromJsonAsync<JsonElement>();
  
        var sections = synDoc.GetProperty("sections").EnumerateArray().ToList();
        Assert.True(sections.Count > 0);

        var allMarkdown = string.Join("\n\n", sections.Select(s => s.GetProperty("contentMarkdown").GetString() ?? ""));

        var boostedCitation  = $"[lrn:{boostedId:N}]";
        var baselineCitation = $"[lrn:{baselineId:N}]";

        Assert.Contains(boostedCitation, allMarkdown);

        // We only enforce relative order if baseline is present in the final text.
        // If baseline isn't used, that's acceptable.
        var boostedIdx = allMarkdown.IndexOf(boostedCitation, StringComparison.Ordinal);
        Assert.True(boostedIdx >= 0);

        var baselineIdx = allMarkdown.IndexOf(baselineCitation, StringComparison.Ordinal);
        if (baselineIdx >= 0)
        {
            Assert.True(boostedIdx < baselineIdx, "Boosted learning should be cited before the baseline learning.");
        }
    }
}