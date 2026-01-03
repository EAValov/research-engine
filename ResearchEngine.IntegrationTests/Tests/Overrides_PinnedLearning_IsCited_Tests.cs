using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ResearchEngine.Domain;
using ResearchEngine.IntegrationTests.Helpers;
using ResearchEngine.IntegrationTests.Infrastructure;

namespace ResearchEngine.IntegrationTests.Tests;

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

        // 3) Create synthesis row without running via HTTP endpoint (deterministic)
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IResearchJobStore>();
        var synthesisService = scope.ServiceProvider.GetRequiredService<IReportSynthesisService>();

        var parent = await store.GetLatestSynthesisAsync(jobId, CancellationToken.None);
        var parentId = parent?.Id;

        var synthesisId = await synthesisService.StartSynthesisAsync(
            jobId: jobId,
            parentSynthesisId: parentId,
            outline: null,
            instructions: "When writing the report, use retrieved learnings and keep citations.",
            ct: CancellationToken.None);

        Assert.NotEqual(Guid.Empty, synthesisId);

        // 4) Apply pinned override BEFORE running the synthesis
        var learningOverrides = new object[]
        {
            new { learningId = pinnedLearningId, scoreOverride = (float?)null, excluded = (bool?)null, pinned = true }
        };

        var ovResp = await client.PutAsJsonAsync(
            $"/api/research/syntheses/{synthesisId}/overrides/learnings",
            learningOverrides);

        ovResp.EnsureSuccessStatusCode();

        // 5) Run the synthesis (overrides will be observed during retrieval)
        await synthesisService.RunExistingSynthesisAsync(
            synthesisId: synthesisId,
            progress: null,
            ct: CancellationToken.None);

        // 6) pull synthesis and assert pinned citation exists
        var synResp = await client.GetAsync($"/api/research/syntheses/{synthesisId}");
        synResp.EnsureSuccessStatusCode();

        var synDoc = await synResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Completed", synDoc.GetProperty("status").GetString());

        var sections = synDoc.GetProperty("sections").EnumerateArray().ToList();
        Assert.True(sections.Count > 0);

        var allMarkdown = string.Join(
            "\n\n",
            sections.Select(s => s.GetProperty("contentMarkdown").GetString() ?? ""));

        var citation = $"[lrn:{pinnedLearningId:N}]";
        Assert.Contains(citation, allMarkdown);
    }
}