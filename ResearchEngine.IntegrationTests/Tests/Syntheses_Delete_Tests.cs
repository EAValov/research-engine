using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ResearchEngine.IntegrationTests.Infrastructure;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class Syntheses_Delete_Tests : IntegrationTestBase
{
    public Syntheses_Delete_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task DeleteSynthesis_RemovesIt_FromGetAndList()
    {
        using var client = CreateClient();

        var jobId = await CreateJobAsync(client, "Test query: delete synthesis endpoint.");

        var createResp = await client.PostAsJsonAsync($"/api/jobs/{jobId}/syntheses", new
        {
            parentSynthesisId = (Guid?)null,
            useLatestAsParent = false,
            outline = (string?)null,
            instructions = "Create synthesis for delete test"
        });
        createResp.EnsureSuccessStatusCode();

        var createJson = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var synthesisId = createJson.GetProperty("synthesisId").GetGuid();
        Assert.NotEqual(Guid.Empty, synthesisId);

        // sanity: synthesis exists
        var getBefore = await client.GetAsync($"/api/syntheses/{synthesisId}");
        getBefore.EnsureSuccessStatusCode();

        // delete synthesis
        var delResp = await client.DeleteAsync($"/api/syntheses/{synthesisId}");
        Assert.Equal(HttpStatusCode.NoContent, delResp.StatusCode);

        // get by id should return 404
        var getAfter = await client.GetAsync($"/api/syntheses/{synthesisId}");
        Assert.Equal(HttpStatusCode.NotFound, getAfter.StatusCode);

        // list for job should not include deleted synthesis
        var listResp = await client.GetAsync($"/api/jobs/{jobId}/syntheses?skip=0&take=50");
        listResp.EnsureSuccessStatusCode();
        var listJson = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        var syntheses = listJson.GetProperty("syntheses").EnumerateArray().ToList();

        Assert.DoesNotContain(syntheses, s => s.GetProperty("synthesisId").GetGuid() == synthesisId);
    }

    [Fact]
    public async Task DeleteSynthesis_UnknownId_Returns404()
    {
        using var client = CreateClient();

        var resp = await client.DeleteAsync($"/api/syntheses/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
