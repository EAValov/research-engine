// ResearchApi.IntegrationTests/Tests/Outline_Contract_Tests.cs

using System.Net.Http.Json;
using System.Text.Json;
using ResearchApi.IntegrationTests.Helpers;
using ResearchApi.IntegrationTests.Infrastructure;
using Xunit;

namespace ResearchApi.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class Outline_Contract_Tests : IntegrationTestBase
{
    public Outline_Contract_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task Regenerate_WithOutline_EnforcesSectionOrder_AndConclusionLast()
    {
        using var client = CreateClient();

        // 1) Create job and wait until initial run finishes (first "done")
        var jobId = await CreateJobAsync(client, "Test query: outline contract.");
        var (jobStatus, initialSynId, _) =
            await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));

        Assert.Equal("Completed", jobStatus);
        Assert.True(initialSynId is null || initialSynId != Guid.Empty);

        // 2) Take checkpoint so we don't accidentally read the OLD done again
        var checkpoint = await SseTestHelpers.GetMaxEventIdAsync(client, jobId);

        // 3) Provide strict outline JSON where conclusion is NOT last (server should normalize)
        var outlineJson = """
        {
          "sections": [
            { "sectionKey": null, "index": 1, "title": "Intro", "description": "Brief intro.", "isConclusion": false },
            { "sectionKey": null, "index": 2, "title": "Conclusion", "description": "Wrap up.", "isConclusion": true },
            { "sectionKey": null, "index": 3, "title": "Details", "description": "Core details.", "isConclusion": false }
          ]
        }
        """;

        // 4) Start regeneration synthesis using latest as parent + outline
        var startReq = new
        {
            parentSynthesisId = (Guid?)null,
            useLatestAsParent = true,
            outline = outlineJson,
            instructions = "Use the outline strictly.",
            sourceOverrides = (object[]?)null,
            learningOverrides = (object[]?)null
        };

        var startResp = await client.PostAsJsonAsync($"/api/research/jobs/{jobId}/syntheses", startReq);
        startResp.EnsureSuccessStatusCode();

        var startJson = await startResp.Content.ReadFromJsonAsync<JsonElement>();
        var expectedSynthesisId = startJson.GetProperty("synthesisId").GetGuid();
        Assert.NotEqual(Guid.Empty, expectedSynthesisId);

        // 5) Wait for NEXT "done" after checkpoint and ensure it's the synthesis we started
        var (synStatus, doneSynthesisId, _) =
            await SseTestHelpers.WaitForDoneAfterAsync(client, jobId, checkpoint, TimeSpan.FromSeconds(60));

        Assert.Equal("Completed", synStatus);
        Assert.True(doneSynthesisId.HasValue);
        Assert.Equal(expectedSynthesisId, doneSynthesisId.Value);

        // 6) Pull synthesis directly by id
        var synResp = await client.GetAsync($"/api/research/syntheses/{doneSynthesisId.Value}");
        synResp.EnsureSuccessStatusCode();

        var synJson = await synResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(doneSynthesisId.Value, synJson.GetProperty("id").GetGuid());
        Assert.Equal(jobId, synJson.GetProperty("jobId").GetGuid());
        Assert.Equal("Completed", synJson.GetProperty("status").GetString());

        var sections = synJson.GetProperty("sections").EnumerateArray().ToList();
        Assert.Equal(3, sections.Count);

        var sections_text = sections.Select(s => $"{s.GetProperty("title").GetString()}|{s.GetProperty("isConclusion").GetBoolean()}");

        var indices = sections.Select(s => s.GetProperty("index").GetInt32()).ToList();
        for (int i = 1; i < indices.Count; i++)
            Assert.True(indices[i] >= indices[i - 1], "Sections must be returned in ascending index order.");

        var isConclusion = sections.Select(s => s.GetProperty("isConclusion").GetBoolean()).ToList();
        Assert.Equal(1, isConclusion.Count(x => x));
        Assert.True(isConclusion[^1], "Conclusion must be the last section.");

        var titles = sections.Select(s => s.GetProperty("title").GetString() ?? "").ToList();
        Assert.Equal("Conclusion", titles[^1]);
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