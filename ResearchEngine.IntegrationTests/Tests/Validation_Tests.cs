using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ResearchEngine.IntegrationTests.Infrastructure;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class Validation_Tests : IntegrationTestBase
{
    public Validation_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task CreateJob_WithMissingQuery_Returns400()
    {
        using var client = CreateClient();

        // Query is required
        var payload = new
        {
            // query missing
            clarifications = Array.Empty<object>(),
            breadth = 2,
            depth = 2,
            language = "en",
            region = (string?)null,
            webhook = (object?)null
        };

        var resp = await client.PostAsJsonAsync("/api/research/jobs", payload);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task CreateJob_WithEmptyQuery_Returns400()
    {
        using var client = CreateClient();

        var payload = new
        {
            query = "",
            clarifications = Array.Empty<object>(),
            breadth = 2,
            depth = 2,
            language = "en",
            region = (string?)null,
            webhook = (object?)null
        };

        var resp = await client.PostAsJsonAsync("/api/research/jobs", payload);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task CreateJob_WithTooLongQuery_Returns400()
    {
        using var client = CreateClient();

        var tooLong = new string('x', 4001);

        var payload = new
        {
            query = tooLong,
            clarifications = Array.Empty<object>(),
            breadth = 2,
            depth = 2,
            language = "en",
            region = (string?)null,
            webhook = (object?)null
        };

        var resp = await client.PostAsJsonAsync("/api/research/jobs", payload);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }


    [Theory]
    [InlineData(-1, 10)]
    [InlineData(0, 0)]
    [InlineData(0, 501)]
    public async Task LearningsEndpoint_WithInvalidPaging_Returns400(int skip, int take)
    {
        using var client = CreateClient();

        // Create a valid job so route exists.
        var jobId = await CreateJobAsync(client);

        var url = $"/api/research/jobs/{jobId}/learnings?skip={skip}&take={take}";
        var resp = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    private static async Task<Guid> CreateJobAsync(HttpClient client)
    {
        var createReq = new
        {
            query = "Validation test job",
            clarifications = Array.Empty<object>(),
            breadth = 2,
            depth = 2,
            language = "en",
            region = (string?)null,
            webhook = (object?)null
        };

        var createResp = await client.PostAsJsonAsync("/api/research/jobs", createReq);
        createResp.EnsureSuccessStatusCode();

        var json = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = json.GetProperty("jobId").GetGuid();
        Assert.NotEqual(Guid.Empty, jobId);

        return jobId;
    }
}