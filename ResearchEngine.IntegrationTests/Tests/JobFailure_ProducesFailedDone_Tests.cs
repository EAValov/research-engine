using System.Net.Http.Json;
using System.Text.Json;
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
                services.RemoveAll<ISearchClient>();
                services.AddSingleton<ISearchClient>(new ThrowingSearchClient(new InvalidOperationException("boom")));
            });
        });

        using var client = failingFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var createReq = new
        {
            query = "Test query that will fail during searching.",
            clarifications = Array.Empty<object>(),
            breadth = 2,
            depth = 2,
            language = "en",
            region = (string?)null,
            webhook = (object?)null
        };

        var createResp = await client.PostAsJsonAsync("/api/research/jobs", createReq);
        createResp.EnsureSuccessStatusCode();

        var createJson = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = createJson.GetProperty("jobId").GetGuid();
        Assert.NotEqual(Guid.Empty, jobId);

        // Use afterEventId=0 (no checkpoint) - we expect the first done to be this job termination
        var (status, synthesisId, doneId) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));

        Assert.Equal("Failed", status);
        // synthesisId can be null if job fails before synthesis starts
        // doneId should exist if server sets "id:" for done frames
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
