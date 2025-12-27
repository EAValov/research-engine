using System.Net.Http.Json;
using System.Text.Json;
using ResearchApi.IntegrationTests.Helpers;
using ResearchApi.IntegrationTests.Infrastructure;

namespace ResearchApi.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class Overrides_PinnedLearning_IsCited_Tests : IntegrationTestBase
{
    public Overrides_PinnedLearning_IsCited_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task Regenerate_WithPinnedLearning_CitesIt_InSynthesisSections()
    {
        using var client = CreateClient();

        // 1) create job + wait done
        var createReq = new
        {
            query = "Test query for pinned learning citation.",
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

        var (status, _, doneId) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.Equal("Completed", status);
        Assert.True(doneId is null or > 0);

        // 2) fetch learnings, pick one to pin
        var learningsResp = await client.GetAsync($"/api/research/jobs/{jobId}/learnings?skip=0&take=200");
        learningsResp.EnsureSuccessStatusCode();

        var learningsJson = await learningsResp.Content.ReadFromJsonAsync<JsonElement>();
        var items = learningsJson.GetProperty("learnings").EnumerateArray().ToList();
        Assert.True(items.Count > 0);

        var pinnedLearningId = items[0].GetProperty("learningId").GetGuid();
        Assert.NotEqual(Guid.Empty, pinnedLearningId);

        // 3) capture current max event id for a clean SSE checkpoint for the regeneration run
        var afterEventId = await SseTestHelpers.GetMaxEventIdAsync(client, jobId);

        // 4) start regeneration with learning override pinned=true
        var regenReq = new
        {
            parentSynthesisId = (Guid?)null,
            useLatestAsParent = true,
            outline = (string?)null,
            instructions = "When writing the report, use retrieved learnings and keep citations.",
            sourceOverrides = (object?)null,
            learningOverrides = new[]
            {
                new { learningId = pinnedLearningId, scoreOverride = (float?)null, excluded = (bool?)null, pinned = true }
            }
        };

        var regenResp = await client.PostAsJsonAsync($"/api/research/jobs/{jobId}/syntheses", regenReq);
        regenResp.EnsureSuccessStatusCode();

        var regenJson = await regenResp.Content.ReadFromJsonAsync<JsonElement>();
        var expectedSynthesisId = regenJson.GetProperty("synthesisId").GetGuid();
        Assert.NotEqual(Guid.Empty, expectedSynthesisId);

        // 5) wait for done AFTER checkpoint; ensure we got the regeneration completion
        var (synStatus, doneSynId, _) = await SseTestHelpers.WaitForDoneAfterAsync(
            client,
            jobId,
            afterEventId: afterEventId,
            timeout: TimeSpan.FromSeconds(60));

        Assert.Equal("Completed", synStatus);
        Assert.True(doneSynId.HasValue);
        Assert.Equal(expectedSynthesisId, doneSynId.Value);

        // 6) pull synthesis and assert pinned citation exists
        var synResp = await client.GetAsync($"/api/research/syntheses/{expectedSynthesisId}");
        synResp.EnsureSuccessStatusCode();

        var synDoc = await synResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Completed", synDoc.GetProperty("status").GetString());

        var sections = synDoc.GetProperty("sections").EnumerateArray().ToList();
        Assert.True(sections.Count > 0);

        var allMarkdown = string.Join("\n\n", sections.Select(s => s.GetProperty("contentMarkdown").GetString() ?? ""));
        var citation = $"[lrn:{pinnedLearningId:N}]";
        Assert.Contains(citation, allMarkdown);
    }
}