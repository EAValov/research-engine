// ResearchApi.IntegrationTests/Tests/Sources_Dedup_Tests.cs

using System.Net.Http.Json;
using System.Text.Json;
using ResearchApi.IntegrationTests.Helpers;
using ResearchApi.IntegrationTests.Infrastructure;
using Xunit;

namespace ResearchApi.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class Sources_Dedup_Tests : IntegrationTestBase
{
    public Sources_Dedup_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task AfterCompletion_SourcesEndpoint_ReturnsUniqueUrls_PerJob()
    {
        using var client = CreateClient();

        var jobId = await CreateJobAsync(client, "Test query: sources dedup.");
        var (status, _, _) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.Equal("Completed", status);

        var sourcesResp = await client.GetAsync($"/api/research/jobs/{jobId}/sources");
        sourcesResp.EnsureSuccessStatusCode();

        var sourcesJson = await sourcesResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(jobId, sourcesJson.GetProperty("jobId").GetGuid());

        var sources = sourcesJson.GetProperty("sources").EnumerateArray().ToList();
        Assert.True(sources.Count > 0);

        var urls = sources
            .Select(s => s.GetProperty("url").GetString() ?? "")
            .ToList();

        Assert.True(urls.All(u => !string.IsNullOrWhiteSpace(u)));

        var distinct = urls.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        Assert.Equal(distinct, urls.Count);
    }

    private static async Task<Guid> CreateJobAsync(HttpClient client, string query)
    {
        var createReq = new
        {
            query,
            clarifications = Array.Empty<object>(),
            breadth = 2,
            depth = 2,
            language = "en",
            region = (string?)null,
            webhook = (object?)null
        };

        var resp = await client.PostAsJsonAsync("/api/research/jobs", createReq);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("jobId").GetGuid();
    }
}