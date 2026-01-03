using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ResearchEngine.IntegrationTests.Helpers;
using ResearchEngine.IntegrationTests.Infrastructure;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class Learnings_UserAdd_Validation_Tests : IntegrationTestBase
{
    public Learnings_UserAdd_Validation_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task AddLearning_InvalidText_TooShort_Returns400_WithValidationErrors()
    {
        using var client = CreateClient();

        var jobId = await CreateJobAsync(client, "Test query: validation too short.");
        var (status, _, _) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.Equal("Completed", status);

        // MinLength(3) => "hi" should fail
        var addReq = new
        {
            text = "hi",
            importanceScore = 0.5f
        };

        var resp = await client.PostAsJsonAsync($"/api/research/jobs/{jobId}/learnings", addReq);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();

        // Minimal API validation error payload typically: { "type": "...", "title": "...", "status": 400, "errors": { "Text": ["..."] } }
        Assert.True(body.TryGetProperty("errors", out var errors));
        Assert.True(errors.TryGetProperty("Text", out _));
    }

    [Fact]
    public async Task AddLearning_InvalidScore_OutOfRange_Returns400()
    {
        using var client = CreateClient();

        var jobId = await CreateJobAsync(client, "Test query: validation score range.");
        var (status, _, _) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.Equal("Completed", status);

        // Range(0..1) => 2.0 should fail
        var addReq = new
        {
            text = "Valid enough learning text.",
            importanceScore = 2.0f
        };

        var resp = await client.PostAsJsonAsync($"/api/research/jobs/{jobId}/learnings", addReq);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("errors", out var errors));
        Assert.True(errors.TryGetProperty("ImportanceScore", out _));
    }
}