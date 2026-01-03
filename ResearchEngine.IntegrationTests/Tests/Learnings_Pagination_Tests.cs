using System.Net.Http.Json;
using System.Text.Json;
using ResearchEngine.IntegrationTests.Helpers;
using ResearchEngine.IntegrationTests.Infrastructure;
using Xunit;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class Learnings_Pagination_Tests : IntegrationTestBase
{
    public Learnings_Pagination_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task ListLearnings_Pagination_Works_AndDoesNotOverlap()
    {
        using var client = CreateClient();

        var jobId = await CreateJobAsync(client, "Test query: pagination.");
        await WaitForJobCompletionAsync(client, jobId, timeoutSeconds: 60);

        var page1 = await GetLearningsPageAsync(client, jobId, skip: 0, take: 3);
        var page2 = await GetLearningsPageAsync(client, jobId, skip: 3, take: 3);

        Assert.Equal(jobId, page1.JobId);
        Assert.Equal(jobId, page2.JobId);

        Assert.True(page1.Total > 0);
        Assert.Equal(page1.Total, page2.Total);

        Assert.InRange(page1.Take, 1, 500);
        Assert.InRange(page2.Take, 1, 500);

        // Pages can be smaller than requested if not enough learnings exist,
        // but if total >= 4 we should have no overlap between first 2 pages.
        var ids1 = page1.Items.Select(x => x.LearningId).ToHashSet();
        var ids2 = page2.Items.Select(x => x.LearningId).ToHashSet();

        var overlap = ids1.Intersect(ids2).ToList();
        Assert.True(overlap.Count == 0, "Pagination pages should not overlap by learningId.");

        // sanity: each item has required fields
        foreach (var it in page1.Items.Concat(page2.Items))
        {
            Assert.NotEqual(Guid.Empty, it.LearningId);
            Assert.NotEqual(Guid.Empty, it.SourceId);
            Assert.False(string.IsNullOrWhiteSpace(it.SourceUrl));
            Assert.False(string.IsNullOrWhiteSpace(it.Text));
        }
    }

    private sealed record LearningsPage(
        Guid JobId,
        int Skip,
        int Take,
        int Total,
        List<(Guid LearningId, Guid SourceId, string SourceUrl, string Text)> Items);

    private static async Task<LearningsPage> GetLearningsPageAsync(HttpClient client, Guid jobId, int skip, int take)
    {
        var resp = await client.GetAsync($"/api/research/jobs/{jobId}/learnings?skip={skip}&take={take}");
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(jobId, json.GetProperty("jobId").GetGuid());

        var jSkip = json.GetProperty("skip").GetInt32();
        var jTake = json.GetProperty("take").GetInt32();
        var total = json.GetProperty("total").GetInt32();

        var items = json.GetProperty("learnings").EnumerateArray()
            .Select(l => (
                LearningId: l.GetProperty("learningId").GetGuid(),
                SourceId: l.GetProperty("sourceId").GetGuid(),
                SourceUrl: l.GetProperty("sourceReference").GetString() ?? "",
                Text: l.GetProperty("text").GetString() ?? ""))
            .ToList();

        return new LearningsPage(jobId, jSkip, jTake, total, items);
    }
}