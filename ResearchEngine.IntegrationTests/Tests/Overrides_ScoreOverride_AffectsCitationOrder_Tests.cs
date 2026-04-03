using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ResearchEngine.Domain;
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

        var jobId = await CreateJobAsync(client, "Test query for score override ordering.");

        var (status, _, _) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.Equal("Completed", status);

        // 2) pick two learnings
        var learningsResp = await client.GetAsync($"/api/jobs/{jobId}/learnings?skip=0&take=200");
        learningsResp.EnsureSuccessStatusCode();

        var learningsJson = await learningsResp.Content.ReadFromJsonAsync<JsonElement>();
        var items = learningsJson.GetProperty("learnings").EnumerateArray().ToList();
        Assert.True(items.Count >= 2);

        var baselineId = items[0].GetProperty("learningId").GetGuid();
        var boostedId  = items[1].GetProperty("learningId").GetGuid();
        Assert.NotEqual(Guid.Empty, baselineId);
        Assert.NotEqual(Guid.Empty, boostedId);

        // 3) Create a new synthesis row WITHOUT running via the HTTP endpoint (deterministic)
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IResearchJobStore>();
        var synthesisService = scope.ServiceProvider.GetRequiredService<IReportSynthesisService>();

        var parent = await store.GetLatestSynthesisAsync(jobId, CancellationToken.None);
        var parentId = parent?.Id;

        var synthesisId = await synthesisService.CreateSynthesisAsync(
            jobId: jobId,
            parentSynthesisId: parentId,
            outline: null,
            instructions: "Use retrieved learnings and include citations.",
            ct: CancellationToken.None);

        Assert.NotEqual(Guid.Empty, synthesisId);

        // 4) Apply scoreOverride to THIS synthesis BEFORE running it
        var learningOverrides = new object[]
        {
            new { learningId = boostedId, scoreOverride = 1.0f, excluded = (bool?)null, pinned = (bool?)null }
        };

        var ovResp = await client.PutAsJsonAsync(
            $"/api/syntheses/{synthesisId}/overrides/learnings",
            learningOverrides);

        ovResp.EnsureSuccessStatusCode();

        // 5) Now run the synthesis (overrides will be observed during retrieval)
        await synthesisService.RunSynthesisAsync(
            synthesisId: synthesisId,
            progress: null,
            ct: CancellationToken.None);

        // 6) pull synthesis and compare citation positions
        var synResp = await client.GetAsync($"/api/syntheses/{synthesisId}");
        synResp.EnsureSuccessStatusCode();

        var synDoc = await synResp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Completed", synDoc.GetProperty("status").GetString());

        var sections = synDoc.GetProperty("sections").EnumerateArray().ToList();
        Assert.True(sections.Count > 0);

        var allMarkdown = string.Join(
            "\n\n",
            sections.Select(s => s.GetProperty("contentMarkdown").GetString() ?? ""));

        var boostedCitation  = $"[lrn:{boostedId:N}]";
        var baselineCitation = $"[lrn:{baselineId:N}]";

        Assert.Contains(boostedCitation, allMarkdown);

        var boostedIdx = allMarkdown.IndexOf(boostedCitation, StringComparison.Ordinal);
        Assert.True(boostedIdx >= 0);

        var baselineIdx = allMarkdown.IndexOf(baselineCitation, StringComparison.Ordinal);
        if (baselineIdx >= 0)
        {
            Assert.True(boostedIdx < baselineIdx, "Boosted learning should be cited before the baseline learning.");
        }
    }
}