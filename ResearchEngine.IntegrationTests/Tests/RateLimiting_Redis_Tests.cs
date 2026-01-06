using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Net.Http.Json;
using ResearchEngine.IntegrationTests.Helpers;
using ResearchEngine.IntegrationTests.Infrastructure;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class RateLimiting_Redis_Tests : IntegrationTestBase
{
    public RateLimiting_Redis_Tests(ContainersFixture containers) : base(containers) { }

   [Fact]
    public async Task AddLearning_TooManyRequests_Returns429()
    {
        using var _ = UseTempEnv(new Dictionary<string, string?>
        {
            // Enable rate limiting for this test
            ["IpRateLimiting__Enabled"] = "true",
            ["IpRateLimiting__EnableEndpointRateLimiting"] = "true",
            ["IpRateLimiting__StackBlockedRequests"] = "false",
            ["IpRateLimiting__RealIpHeader"] = "X-Forwarded-For",
            ["IpRateLimiting__HttpStatusCode"] = "429",

            // Rules
            ["IpRateLimiting__GeneralRules__0__Endpoint"] = "POST:/api/research/jobs/*/learnings",
            ["IpRateLimiting__GeneralRules__0__Period"] = "1m",
            ["IpRateLimiting__GeneralRules__0__Limit"] = "3",
        });

        // IMPORTANT: new factory/host after env vars are set
        await using var limitedFactory = new CustomWebApplicationFactory(Containers);

        await using var failingFactory = limitedFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBackgroundJobClient>();
                services.AddSingleton<IBackgroundJobClient, InlineBackgroundJobClient>();
            });
        });

        using var client = failingFactory.CreateClient();

        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.10");

        var jobId = await CreateJobAsync(client, "Test query: rate limiting.");
        var (status, _, _) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.Equal("Completed", status);

        HttpStatusCode last = HttpStatusCode.OK;

        for (int i = 0; i < 20; i++)
        {
            var resp = await client.PostAsJsonAsync($"/api/research/jobs/{jobId}/learnings", new
            {
                text = $"rl-{i}",
                importanceScore = 1.0f,
                reference = (string?)null,
                evidenceText = "note"
            });

            last = resp.StatusCode;
            if (last == HttpStatusCode.TooManyRequests)
                break;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }

    static IDisposable UseTempEnv(IDictionary<string, string?> values)
    {
        var old = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in values)
        {
            old[kv.Key] = Environment.GetEnvironmentVariable(kv.Key);
            Environment.SetEnvironmentVariable(kv.Key, kv.Value);
        }

        return new RestoreEnv(old);
    }

    sealed class RestoreEnv(Dictionary<string, string?> old) : IDisposable
    {
        public void Dispose()
        {
            foreach (var kv in old)
                Environment.SetEnvironmentVariable(kv.Key, kv.Value);
        }
    }
}
