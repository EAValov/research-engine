using System.Net.Http.Json;
using System.Text.Json;
using Hangfire;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResearchEngine.Domain;
using ResearchEngine.IntegrationTests.Helpers;
using ResearchEngine.IntegrationTests.Infrastructure;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class SourceDiscoveryMode_Tests : IntegrationTestBase
{
    public SourceDiscoveryMode_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task JobRun_ReliableOnly_FiltersLowTrustSources()
    {
        await using var factory = Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBackgroundJobClient>();
                services.AddSingleton<IBackgroundJobClient, InlineBackgroundJobClient>();
                services.RemoveAll<ISearchClient>();
                services.AddSingleton<ISearchClient>(new ReliableVsForumSearchClient());
            });
        });

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var jobId = await CreateJobWithModeAsync(client, "ReliableOnly");
        var (status, _, _) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.Equal("Completed", status);

        var sources = await ListSourcesAsync(client, jobId);
        Assert.Single(sources);

        var source = sources[0];
        Assert.Contains("sec.gov", source.GetProperty("reference").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Government", source.GetProperty("classification").GetString());
        Assert.Equal("High", source.GetProperty("reliabilityTier").GetString());
        Assert.True(source.GetProperty("isPrimarySource").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(source.GetProperty("reliabilityRationale").GetString()));
    }

    [Fact]
    public async Task JobRun_AcademicOnly_FiltersNonAcademicSources()
    {
        await using var factory = Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBackgroundJobClient>();
                services.AddSingleton<IBackgroundJobClient, InlineBackgroundJobClient>();
                services.RemoveAll<ISearchClient>();
                services.AddSingleton<ISearchClient>(new AcademicVsNewsSearchClient());
            });
        });

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var jobId = await CreateJobWithModeAsync(client, "AcademicOnly");
        var (status, _, _) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.Equal("Completed", status);

        var sources = await ListSourcesAsync(client, jobId);
        Assert.Single(sources);

        var source = sources[0];
        Assert.Contains("arxiv.org", source.GetProperty("reference").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Preprint", source.GetProperty("classification").GetString());
        Assert.Equal("Medium", source.GetProperty("reliabilityTier").GetString());
    }

    private static async Task<Guid> CreateJobWithModeAsync(HttpClient client, string discoveryMode)
    {
        var createReq = new
        {
            query = $"Test query for {discoveryMode}",
            clarifications = Array.Empty<object>(),
            breadth = 2,
            depth = 2,
            language = "en",
            region = (string?)null,
            discoveryMode
        };

        var createResp = await client.PostAsJsonAsync("/api/research/jobs", createReq);
        createResp.EnsureSuccessStatusCode();

        var json = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("jobId").GetGuid();
    }

    private sealed class ReliableVsForumSearchClient : ISearchClient
    {
        public Task<IReadOnlyList<SearchResult>> SearchAsync(SearchRequest request, CancellationToken ct = default)
        {
            IReadOnlyList<SearchResult> results =
            [
                new(
                    "https://www.sec.gov/news/press-release/2025-01",
                    "SEC press release",
                    "Official statement",
                    Domain: "sec.gov",
                    Position: 1),
                new(
                    "https://www.reddit.com/r/investing/comments/example",
                    "Reddit discussion",
                    "User opinions",
                    Domain: "reddit.com",
                    Position: 2)
            ];

            return Task.FromResult(results);
        }
    }

    private sealed class AcademicVsNewsSearchClient : ISearchClient
    {
        public Task<IReadOnlyList<SearchResult>> SearchAsync(SearchRequest request, CancellationToken ct = default)
        {
            IReadOnlyList<SearchResult> results =
            [
                new(
                    "https://arxiv.org/abs/2401.12345",
                    "Interesting paper",
                    "Research paper",
                    Domain: "arxiv.org",
                    SearchCategory: "research",
                    Position: 1),
                new(
                    "https://www.reuters.com/world/europe/example-story/",
                    "Reuters story",
                    "News report",
                    Domain: "reuters.com",
                    Position: 2)
            ];

            return Task.FromResult(results);
        }
    }
}
