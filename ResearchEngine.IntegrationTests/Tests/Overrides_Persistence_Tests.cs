using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ResearchEngine.Domain;
using ResearchEngine.IntegrationTests.Helpers;
using ResearchEngine.IntegrationTests.Infrastructure;
using Xunit;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class Overrides_Persistence_Tests : IntegrationTestBase
{
    public Overrides_Persistence_Tests(ContainersFixture containers) : base(containers) { }
    
    [Fact]
    public async Task StartSynthesis_PersistsSourceAndLearningOverrides_AndSnapshotReturnsThem()
    {
        using var client = CreateClient();

        // 1) Create job and wait initial run
        var jobId = await CreateJobAsync(client, "Test query: overrides persistence.");
        var (jobStatus, _, _) =
            await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));

        Assert.Equal("Completed", jobStatus);

        // 2) Collect ids for overrides
        var sources = await ListSourcesAsync(client, jobId);
        Assert.True(sources.Count > 0);
        var sourceId = sources[0].GetProperty("sourceId").GetGuid();

        var learnings = await ListLearningsAsync(client, jobId);
        Assert.True(learnings.Count > 1);

        var learningExcluded = learnings[0].GetProperty("learningId").GetGuid();
        var learningPinned   = learnings[1].GetProperty("learningId").GetGuid();

        var sourceOverrides = new object[]
        {
            new { sourceId, excluded = true, pinned = (bool?)null }
        };

        var learningOverrides = new object[]
        {
            new { learningId = learningExcluded, scoreOverride = (float?)null, excluded = true, pinned = (bool?)null },
            new { learningId = learningPinned,   scoreOverride = (float?)0.95f, excluded = (bool?)null, pinned = true }
        };

        // 3) checkpoint so we get the NEXT done for this synthesis run
        var checkpoint = await SseTestHelpers.GetMaxEventIdAsync(client, jobId);

        // 4) Create synthesis row with overrides (NO run yet)
        var createReq = new
        {
            parentSynthesisId = (Guid?)null,
            useLatestAsParent = true,
            outline = (string?)null,
            instructions = "Test overrides",
            sourceOverrides,
            learningOverrides
        };

        var createResp = await client.PostAsJsonAsync($"/api/jobs/{jobId}/syntheses", createReq);
        createResp.EnsureSuccessStatusCode();

        var createJson = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var synthesisId = createJson.GetProperty("synthesisId").GetGuid();
        Assert.NotEqual(Guid.Empty, synthesisId);

        // 5) Run synthesis (Hangfire enqueue) AFTER overrides are saved
        var runResp = await client.PostAsync($"/api/syntheses/{synthesisId}/run", content: null);
        runResp.EnsureSuccessStatusCode();

        // 6) Wait for NEXT done and ensure it matches this synthesis
        var (synStatus, doneSynthesisId, _) =
            await SseTestHelpers.WaitForDoneAfterAsync(client, jobId, checkpoint, TimeSpan.FromSeconds(60));

        Assert.Equal("Completed", synStatus);
        Assert.True(doneSynthesisId.HasValue);
        Assert.Equal(synthesisId, doneSynthesisId.Value);

        // 7) Query overrides snapshot directly via store (DI)
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IResearchJobStore>();

        var snapshot = await store.GetSynthesisOverridesAsync(synthesisId, CancellationToken.None);

        Assert.Equal(synthesisId, snapshot.SynthesisId);
        Assert.Equal(jobId, snapshot.JobId);

        Assert.True(snapshot.SourceOverridesBySourceId.TryGetValue(sourceId, out var so));
        Assert.True(so.Excluded == true);

        Assert.True(snapshot.LearningOverridesByLearningId.TryGetValue(learningExcluded, out var lo1));
        Assert.True(lo1.Excluded == true);

        Assert.True(snapshot.LearningOverridesByLearningId.TryGetValue(learningPinned, out var lo2));
        Assert.True(lo2.Pinned == true);
        Assert.True(lo2.ScoreOverride.HasValue);
    }

}
