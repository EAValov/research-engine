using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ResearchEngine.Domain;
using ResearchEngine.IntegrationTests.Helpers;
using ResearchEngine.IntegrationTests.Infrastructure;
using Xunit;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class VectorSearch_RespectsOverrides_Tests : IntegrationTestBase
{
    public VectorSearch_RespectsOverrides_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task Regenerate_WithExcludedLearning_DoesNotCiteIt_InSynthesisSections()
    {
        using var client = CreateClient();

        // 1) initial job run
        var jobId = await CreateJobAsync(client, "Test query: overrides affect retrieval.");
        var (status1, _, _) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.Equal("Completed", status1);

        // 2) pick a learning to exclude
        var learnings = await ListLearningsAsync(client, jobId);
        Assert.True(learnings.Count > 0);

        var excludedLearningId = learnings[0].GetProperty("learningId").GetGuid();
        Assert.NotEqual(Guid.Empty, excludedLearningId);

        // 3) Create synthesis row WITHOUT starting it via HTTP endpoint (deterministic)
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IResearchJobStore>();
        var synthesisService = scope.ServiceProvider.GetRequiredService<IReportSynthesisService>();

        var parent = await store.GetLatestSynthesisAsync(jobId, CancellationToken.None);
        var parentId = parent?.Id;

        var synthesisId = await synthesisService.CreateSynthesisAsync(
            jobId: jobId,
            parentSynthesisId: parentId,
            outline: null,
            instructions: "Ensure you cite evidence learnings using [lrn:...] markers.",
            ct: CancellationToken.None);

        Assert.NotEqual(Guid.Empty, synthesisId);

        // 4) Apply excluded override BEFORE running the synthesis
        var learningOverrides = new object[]
        {
            new { learningId = excludedLearningId, scoreOverride = (float?)null, excluded = true, pinned = (bool?)null }
        };

        var ovResp = await client.PutAsJsonAsync(
            $"/api/research/syntheses/{synthesisId}/overrides/learnings",
            learningOverrides);

        ovResp.EnsureSuccessStatusCode();

        // 5) Run synthesis so overrides take effect
        await synthesisService.RunSynthesisAsync(
            synthesisId: synthesisId,
            progress: null,
            ct: CancellationToken.None);

        // 6) fetch synthesis and ensure none of the sections contain the excluded citation
        var synResp = await client.GetAsync($"/api/research/syntheses/{synthesisId}");
        synResp.EnsureSuccessStatusCode();

        var synJson = await synResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Completed", synJson.GetProperty("status").GetString());

        var sections = synJson.GetProperty("sections").EnumerateArray().ToList();
        Assert.True(sections.Count > 0);

        var needle = $"[lrn:{excludedLearningId:N}]";
        foreach (var s in sections)
        {
            var body = s.GetProperty("contentMarkdown").GetString() ?? "";
            Assert.DoesNotContain(needle, body);
        }
    }
}