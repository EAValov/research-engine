using System.Net.Http.Json;
using System.Text.Json;
using ResearchEngine.IntegrationTests.Helpers;
using ResearchEngine.IntegrationTests.Infrastructure;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class LearningGroups_Resolve_Batch_Tests : IntegrationTestBase
{
    public LearningGroups_Resolve_Batch_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task ResolveBatch_PreservesOrder_AndReturnsNullForUnknownIds()
    {
        using var client = CreateClient();

        var jobId = await CreateJobAsync(client, "Test query: group resolver batch.");
        var (status, _, _) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.Equal("Completed", status);

        async Task<Guid> Add(string text)
        {
            var r = await client.PostAsJsonAsync($"/api/research/jobs/{jobId}/learnings", new
            {
                text,
                importanceScore = 1.0f,
                reference = (string?)null,
                evidenceText = "note"
            });

            r.EnsureSuccessStatusCode();
            var j = await r.Content.ReadFromJsonAsync<JsonElement>();
            return j.GetProperty("learning").GetProperty("learningId").GetGuid();
        }

        var a = await Add("AAAAAAAAAAAAAAAAAAA");
        var b = await Add("BBBBBBBBBBBBBBBBBBB");
        var missing = Guid.NewGuid();

        var batchResp = await client.PostAsJsonAsync("/api/research/learnings/groups/resolve", new
        {
            learningIds = new[] { a, missing, b, a }
        });

        batchResp.EnsureSuccessStatusCode();

        var json = await batchResp.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items").EnumerateArray().ToList();

        Assert.Equal(4, items.Count);

        Assert.Equal(a, items[0].GetProperty("learningId").GetGuid());
        Assert.True(items[0].GetProperty("group").ValueKind != JsonValueKind.Null);

        Assert.Equal(missing, items[1].GetProperty("learningId").GetGuid());
        Assert.True(items[1].GetProperty("group").ValueKind == JsonValueKind.Null);

        Assert.Equal(b, items[2].GetProperty("learningId").GetGuid());
        Assert.True(items[2].GetProperty("group").ValueKind != JsonValueKind.Null);

        Assert.Equal(a, items[3].GetProperty("learningId").GetGuid());
    }
}