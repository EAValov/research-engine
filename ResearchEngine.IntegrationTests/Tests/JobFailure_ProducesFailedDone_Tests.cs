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
public sealed class JobFailure_ProducesFailedDone_Tests : IntegrationTestBase
{
    public JobFailure_ProducesFailedDone_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task JobRun_WhenSearchThrows_SseDoneIsFailed()
    {
        // Arrange: override ISearchClient to throw during the job run
        await using var failingFactory = Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBackgroundJobClient>();
                services.AddSingleton<IBackgroundJobClient, InlineBackgroundJobClient>(); // required for Factory overrides
        
                services.RemoveAll<ISearchClient>();
                services.AddSingleton<ISearchClient>(new ThrowingSearchClient(new InvalidOperationException("boom")));
            });
        });

        using var client = failingFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var jobId = await CreateJobAsync(client, "Test query that will fail during searching.");
        Assert.NotEqual(Guid.Empty, jobId);

        var (status, synthesisId, doneId) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));

        Assert.Equal("Failed", status);
        Assert.True(doneId is null or > 0);
    }

    private sealed class ThrowingSearchClient : ISearchClient
    {
        private readonly Exception _ex;
        public ThrowingSearchClient(Exception ex) => _ex = ex;

        public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int limit, string? location = null, CancellationToken ct = default)
            => throw _ex;
    }
}
