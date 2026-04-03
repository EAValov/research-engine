using System.Net.Http.Json;
using System.Text.Json;
using Hangfire;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResearchEngine.Domain;
using ResearchEngine.IntegrationTests.Helpers;
using ResearchEngine.IntegrationTests.Infrastructure;
using Xunit;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class Sources_DedupeAcrossSerpQueries_Tests : IntegrationTestBase
{
    public Sources_DedupeAcrossSerpQueries_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task JobRun_WhenSameUrlAppearsAcrossQueries_SourcesContainSingleRow_AndLearningsNotDoubled()
    {
        // Arrange: search returns the same URL for any query, so two SERP queries will overlap.
        await using var overlapFactory = Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBackgroundJobClient>();
                services.AddSingleton<IBackgroundJobClient, InlineBackgroundJobClient>(); // required for Factory overrides
                services.RemoveAll<ISearchClient>();
                services.AddSingleton<ISearchClient>(new OverlapSearchClient());
            });
        });

        using var client = overlapFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var jobId = await CreateJobAsync(client, "Test query that will fail during searching.");
        Assert.NotEqual(Guid.Empty, jobId);

        var (status, _, _) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.Equal("Completed", status);

        // Assert sources are unique
        var sourcesResp = await client.GetAsync($"/api/jobs/{jobId}/sources");
        sourcesResp.EnsureSuccessStatusCode();

        var sourcesJson = await sourcesResp.Content.ReadFromJsonAsync<JsonElement>();
        var sources = sourcesJson.GetProperty("sources").EnumerateArray().ToList();
        Assert.True(sources.Count > 0);

        var urls = sources.Select(s => s.GetProperty("reference").GetString() ?? "").ToList();
        Assert.True(urls.All(u => !string.IsNullOrWhiteSpace(u)));

        // ensure no duplicates
        var distinctCount = urls.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        Assert.Equal(distinctCount, urls.Count);

        // Additional sanity: if overlap happened, we expect fewer sources than breadth*limit would suggest.
        // We don't hardcode the exact number, but we can assert the overlapping URL appears only once.
        var overlapUrl = OverlapSearchClient.SharedUrl;
        Assert.Equal(1, urls.Count(u => string.Equals(u, overlapUrl, StringComparison.OrdinalIgnoreCase)));

        // Optional: learnings should be non-zero and should not explode
        var learningsResp = await client.GetAsync($"/api/jobs/{jobId}/learnings?skip=0&take=200");
        learningsResp.EnsureSuccessStatusCode();

        var learningsJson = await learningsResp.Content.ReadFromJsonAsync<JsonElement>();
        var learnings = learningsJson.GetProperty("learnings").EnumerateArray().ToList();
        Assert.True(learnings.Count > 0);

        Assert.InRange(learnings.Count, 1, 20); // 20 learnings configured in fixture
    }

    private sealed class OverlapSearchClient : ISearchClient
    {
        public const string SharedUrl = "https://example.test/overlap";

        public Task<IReadOnlyList<SearchResult>> SearchAsync(SearchRequest request, CancellationToken ct = default)
        {
            // Always return the same URL (plus a second distinct URL so the job can progress).
            var results = new List<SearchResult>
            {
                new(SharedUrl, "Overlap", "Overlap content", Domain: "example.test", Position: 1),
                new("https://example.test/unique", "Unique", "Unique content", Domain: "example.test", Position: 2)
            };

            // Respect limit if provided
            if (request.Limit > 0 && results.Count > request.Limit)
                results = results.Take(request.Limit).ToList();

            return Task.FromResult((IReadOnlyList<SearchResult>)results);
        }
    }
}
