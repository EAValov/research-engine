using System.Net.Http.Json;
using System.Text.Json;
using ResearchEngine.IntegrationTests.Helpers;
using ResearchEngine.IntegrationTests.Infrastructure;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class Learnings_SourceFilter_And_Promotion_Tests : IntegrationTestBase
{
    public Learnings_SourceFilter_And_Promotion_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task ListLearnings_SourceReferenceFilter_ReturnsOnlyMatchingSource_AndPaginates()
    {
        using var client = CreateClient();

        var jobId = await CreateJobAsync(client, "Test query: learnings source filter pagination.");
        var (status, _, _) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.Equal("Completed", status);

        var sourceRef = $"test:source-filter:{Guid.NewGuid():N}";
        var otherRef = $"test:source-filter-other:{Guid.NewGuid():N}";

        await AddLearningAsync(client, jobId, "learning A1", 0.20f, sourceRef);
        await AddLearningAsync(client, jobId, "learning A2", 0.40f, sourceRef);
        await AddLearningAsync(client, jobId, "learning A3", 0.60f, sourceRef);
        await AddLearningAsync(client, jobId, "learning B1", 0.90f, otherRef);

        var all = await GetLearningsPageAsync(client, jobId, skip: 0, take: 50, sourceReference: sourceRef);
        Assert.Equal(3, all.Total);
        Assert.Equal(3, all.Items.Count);
        Assert.All(all.Items, item => Assert.Equal(sourceRef, item.SourceReference));

        var page1 = await GetLearningsPageAsync(client, jobId, skip: 0, take: 2, sourceReference: sourceRef);
        var page2 = await GetLearningsPageAsync(client, jobId, skip: 2, take: 2, sourceReference: sourceRef);

        Assert.Equal(3, page1.Total);
        Assert.Equal(3, page2.Total);

        var ids1 = page1.Items.Select(x => x.LearningId).ToHashSet();
        var ids2 = page2.Items.Select(x => x.LearningId).ToHashSet();
        Assert.Empty(ids1.Intersect(ids2));
    }

    [Fact]
    public async Task ListLearnings_SourceReferenceFilter_WithPromoteLearningId_PutsPromotedItemFirst()
    {
        using var client = CreateClient();

        var jobId = await CreateJobAsync(client, "Test query: learnings source filter promotion.");
        var (status, _, _) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.Equal("Completed", status);

        var sourceRef = $"test:source-promote:{Guid.NewGuid():N}";

        var low = await AddLearningAsync(client, jobId, "low importance but promoted", 0.01f, sourceRef);
        await AddLearningAsync(client, jobId, "medium importance", 0.50f, sourceRef);
        await AddLearningAsync(client, jobId, "high importance", 0.90f, sourceRef);

        var page = await GetLearningsPageAsync(
            client,
            jobId,
            skip: 0,
            take: 10,
            sourceReference: sourceRef,
            promoteLearningId: low);

        Assert.True(page.Items.Count >= 3);
        Assert.Equal(low, page.Items[0].LearningId);
        Assert.All(page.Items, item => Assert.Equal(sourceRef, item.SourceReference));
    }

    private static async Task<Guid> AddLearningAsync(
        HttpClient client,
        Guid jobId,
        string text,
        float score,
        string reference)
    {
        var addResp = await client.PostAsJsonAsync($"/api/jobs/{jobId}/learnings", new
        {
            text,
            importanceScore = score,
            reference,
            evidenceText = "test-note",
            language = (string?)null,
            region = (string?)null
        });

        addResp.EnsureSuccessStatusCode();
        var addJson = await addResp.Content.ReadFromJsonAsync<JsonElement>();
        return addJson.GetProperty("learning").GetProperty("learningId").GetGuid();
    }

    private sealed record LearningListItem(Guid LearningId, string SourceReference);
    private sealed record LearningsPage(int Total, List<LearningListItem> Items);

    private static async Task<LearningsPage> GetLearningsPageAsync(
        HttpClient client,
        Guid jobId,
        int skip,
        int take,
        string? sourceReference = null,
        Guid? promoteLearningId = null)
    {
        var query = new List<string> { $"skip={skip}", $"take={take}" };
        if (!string.IsNullOrWhiteSpace(sourceReference))
            query.Add($"sourceReference={Uri.EscapeDataString(sourceReference)}");
        if (promoteLearningId is Guid promoted)
            query.Add($"promoteLearningId={promoted}");

        var url = $"/api/jobs/{jobId}/learnings?{string.Join("&", query)}";
        var resp = await client.GetAsync(url);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var total = json.GetProperty("total").GetInt32();
        var items = json.GetProperty("learnings").EnumerateArray()
            .Select(x => new LearningListItem(
                x.GetProperty("learningId").GetGuid(),
                x.GetProperty("sourceReference").GetString() ?? string.Empty))
            .ToList();

        return new LearningsPage(total, items);
    }
}
