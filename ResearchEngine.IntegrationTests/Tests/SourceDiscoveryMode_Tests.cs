using System.Net.Http.Json;
using System.Text.Json;
using Hangfire;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResearchEngine.Domain;
using ResearchEngine.IntegrationTests.Helpers;
using ResearchEngine.IntegrationTests.Infrastructure;
using ResearchEngine.Infrastructure;

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

    [Fact]
    public async Task JobRun_Balanced_ResearchBlog_IsStoredAsMediumTrustBlog()
    {
        await using var factory = Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBackgroundJobClient>();
                services.AddSingleton<IBackgroundJobClient, InlineBackgroundJobClient>();
                services.RemoveAll<ISearchClient>();
                services.AddSingleton<ISearchClient>(new ResearchBlogOnlySearchClient());
            });
        });

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var jobId = await CreateJobWithModeAsync(client, "Balanced");
        var (status, _, _) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.Equal("Completed", status);

        var sources = await ListSourcesAsync(client, jobId);
        Assert.Single(sources);

        var source = sources[0];
        Assert.Contains("trickle.so/blog/google-a2a-vs-mcp", source.GetProperty("reference").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Blog", source.GetProperty("classification").GetString());
        Assert.Equal("Medium", source.GetProperty("reliabilityTier").GetString());

        var rationale = source.GetProperty("reliabilityRationale").GetString();
        Assert.Contains("Blog", rationale ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Research", rationale ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task JobRun_ReliableOnly_KeepsOfficialProtocolDoc_AndDropsResearchBlog()
    {
        await using var factory = Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBackgroundJobClient>();
                services.AddSingleton<IBackgroundJobClient, InlineBackgroundJobClient>();
                services.RemoveAll<ISearchClient>();
                services.AddSingleton<ISearchClient>(new OfficialVsResearchBlogSearchClient());
            });
        });

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var jobId = await CreateJobWithModeAsync(client, "ReliableOnly");
        var (status, _, _) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.Equal("Completed", status);

        var sources = await ListSourcesAsync(client, jobId);
        Assert.Single(sources);

        var source = sources[0];
        Assert.Contains("a2a-protocol.org/latest/topics/a2a-and-mcp", source.GetProperty("reference").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Official", source.GetProperty("classification").GetString());
        Assert.Equal("High", source.GetProperty("reliabilityTier").GetString());
        Assert.True(source.GetProperty("isPrimarySource").GetBoolean());
    }

    [Fact]
    public async Task CreateJob_AutoDiscoveryMode_ResolvesToConcreteMode()
    {
        using var client = CreateClient();

        var jobId = await CreateJobWithModeAsync(client, "Auto");

        var jobResp = await client.GetAsync($"/api/research/jobs/{jobId}");
        jobResp.EnsureSuccessStatusCode();

        var json = await jobResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Balanced", json.GetProperty("discoveryMode").GetString());
    }

    [Fact]
    public void BuildPolicy_HumanReadableRussianRegion_IncludesGlobalAndRussiaPacks()
    {
        var policy = SourceTrustRuleCatalog.BuildPolicy("Russian market", "en");

        Assert.Contains("Global", policy.ActivePackNames);
        Assert.Contains("Russia", policy.ActivePackNames);
        Assert.DoesNotContain("China", policy.ActivePackNames);
        Assert.Contains(policy.Rules, rule => rule.Name == "global-government-gov");
        Assert.Contains(policy.Rules, rule => rule.Name == "russia-government-gov-ru");
    }

    [Fact]
    public async Task JobRun_ReliableOnly_HumanReadableRussianRegion_UsesRussiaPack()
    {
        await using var factory = Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBackgroundJobClient>();
                services.AddSingleton<IBackgroundJobClient, InlineBackgroundJobClient>();
                services.RemoveAll<ISearchClient>();
                services.AddSingleton<ISearchClient>(new RussianGovernmentSearchClient());
            });
        });

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var jobId = await CreateJobWithModeAsync(client, "ReliableOnly", language: "en", region: "Russian market");
        var (status, _, _) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.Equal("Completed", status);

        var sources = await ListSourcesAsync(client, jobId);
        Assert.Single(sources);

        var source = sources[0];
        Assert.Contains("minfin.gov.ru", source.GetProperty("reference").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Government", source.GetProperty("classification").GetString());
        Assert.Equal("High", source.GetProperty("reliabilityTier").GetString());
        Assert.True(source.GetProperty("isPrimarySource").GetBoolean());
    }

    private static async Task<Guid> CreateJobWithModeAsync(
        HttpClient client,
        string discoveryMode,
        string language = "en",
        string? region = null)
    {
        var createReq = new
        {
            query = $"Test query for {discoveryMode}",
            clarifications = Array.Empty<object>(),
            breadth = 2,
            depth = 2,
            language,
            region,
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

    private sealed class ResearchBlogOnlySearchClient : ISearchClient
    {
        public Task<IReadOnlyList<SearchResult>> SearchAsync(SearchRequest request, CancellationToken ct = default)
        {
            IReadOnlyList<SearchResult> results =
            [
                new(
                    "https://trickle.so/blog/google-a2a-vs-mcp",
                    "Google A2A vs MCP",
                    "A blog comparison post",
                    Domain: "trickle.so",
                    SearchCategory: "research",
                    Position: 1)
            ];

            return Task.FromResult(results);
        }
    }

    private sealed class OfficialVsResearchBlogSearchClient : ISearchClient
    {
        public Task<IReadOnlyList<SearchResult>> SearchAsync(SearchRequest request, CancellationToken ct = default)
        {
            IReadOnlyList<SearchResult> results =
            [
                new(
                    "https://trickle.so/blog/google-a2a-vs-mcp",
                    "Google A2A vs MCP",
                    "A blog comparison post",
                    Domain: "trickle.so",
                    SearchCategory: "research",
                    Position: 1),
                new(
                    "https://a2a-protocol.org/latest/topics/a2a-and-mcp/",
                    "A2A and MCP",
                    "Official protocol documentation",
                    Domain: "a2a-protocol.org",
                    Position: 2)
            ];

            return Task.FromResult(results);
        }
    }

    private sealed class RussianGovernmentSearchClient : ISearchClient
    {
        public Task<IReadOnlyList<SearchResult>> SearchAsync(SearchRequest request, CancellationToken ct = default)
        {
            IReadOnlyList<SearchResult> results =
            [
                new(
                    "https://minfin.gov.ru/ru/press-center/",
                    "Russian Ministry of Finance",
                    "Official government publication",
                    Domain: "minfin.gov.ru",
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
