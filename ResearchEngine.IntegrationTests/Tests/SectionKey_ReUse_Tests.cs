using System.Net.Http.Json;
using System.Text.Json;
using ResearchEngine.IntegrationTests.Helpers;
using ResearchEngine.IntegrationTests.Infrastructure;
using Xunit;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class SectionKey_ReUse_Tests : IntegrationTestBase
{
    public SectionKey_ReUse_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task Regenerate_WithOutlineReusingSectionKeys_PreservesKeys_AndAssignsNewOnNull()
    {
        using var client = CreateClient();

        // Create job and wait completion
        var jobId = await CreateJobAsync(client, "Test query: section key reuse.");
        await WaitForJobCompletionAsync(client, jobId, timeoutSeconds: 60);

        // Baseline synthesis
        var s1 = await GetLatestSynthesisAsync(client, jobId);
        Assert.Equal("Completed", s1.GetProperty("status").GetString());

        var s1Id = s1.GetProperty("id").GetGuid();
        var s1Sections = s1.GetProperty("sections").EnumerateArray().ToList();
        Assert.True(s1Sections.Count >= 2);

        // Pick 2 existing SectionKeys to reuse
        var reuseA = s1Sections[0].GetProperty("sectionKey").GetGuid();
        var reuseB = s1Sections.Count > 1 ? s1Sections[1].GetProperty("sectionKey").GetGuid() : reuseA;

        Assert.NotEqual(Guid.Empty, reuseA);
        Assert.NotEqual(Guid.Empty, reuseB);

        // Provide outline with:
        // - 2 reused section keys
        // - 1 new section (sectionKey null)
        // Note: outline uses 1-based indices, your storage uses 0..N-1, that's fine.
        var outlineJson = $$"""
        {
          "sections": [
            { "sectionKey": "{{reuseA}}", "index": 1, "title": "Reused A", "description": "Should preserve key.", "isConclusion": false },
            { "sectionKey": null,        "index": 2, "title": "New Section", "description": "Should get a new key.", "isConclusion": false },
            { "sectionKey": "{{reuseB}}", "index": 3, "title": "Reused B", "description": "Should preserve key.", "isConclusion": true }
          ]
        }
        """;

        // Start regeneration using s1 as parent
        var req = new
        {
            parentSynthesisId = s1Id,
            useLatestAsParent = (bool?)null,
            outline = outlineJson,
            instructions = "Test SectionKey matching.",
            sourceOverrides = (object?)null,
            learningOverrides = (object?)null
        };

        var startResp = await client.PostAsJsonAsync($"/api/research/jobs/{jobId}/syntheses", req);
        startResp.EnsureSuccessStatusCode();

        var startJson = await startResp.Content.ReadFromJsonAsync<JsonElement>();
        var s2Id = startJson.GetProperty("synthesisId").GetGuid();
        Assert.NotEqual(Guid.Empty, s2Id);
        Assert.NotEqual(s1Id, s2Id);

        var s2 = await WaitForSynthesisCompletedAsync(client, s2Id, timeoutSeconds: 60);
        Assert.Equal("Completed", s2.GetProperty("status").GetString());
        Assert.Equal(s1Id, s2.GetProperty("parentSynthesisId").GetGuid());

        var s2Sections = s2.GetProperty("sections").EnumerateArray().ToList();
        Assert.True(s2Sections.Count >= 3);

        var s2Keys = s2Sections.Select(s => s.GetProperty("sectionKey").GetGuid()).ToList();

        Assert.Contains(reuseA, s2Keys);
        Assert.Contains(reuseB, s2Keys);

        var newKeys = s2Keys.Where(k => k != reuseA && k != reuseB).ToList();
        Assert.True(newKeys.Count >= 1, "Expected at least one newly assigned SectionKey.");
        Assert.All(newKeys, k => Assert.NotEqual(Guid.Empty, k));

        // Ensure conclusion is last (sanity)
        var conclusionFlags = s2Sections.Select(s => s.GetProperty("isConclusion").GetBoolean()).ToList();
        Assert.Equal(1, conclusionFlags.Count(b => b));
        Assert.True(conclusionFlags[^1]);
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

    private static async Task WaitForJobCompletionAsync(HttpClient client, Guid jobId, int timeoutSeconds)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (true)
        {
            var evResp = await client.GetAsync($"/api/research/jobs/{jobId}/events");
            evResp.EnsureSuccessStatusCode();

            var events = await evResp.Content.ReadFromJsonAsync<JsonElement>();
            var stages = events.EnumerateArray().Select(GenericHelpers.GetStageName).ToList();

            if (stages.Contains("Completed")) return;
            if (stages.Contains("Failed")) throw new Xunit.Sdk.XunitException("Job failed unexpectedly.");
            if (DateTimeOffset.UtcNow > deadline) throw new Xunit.Sdk.XunitException("Timed out waiting for job completion.");

            await Task.Delay(300);
        }
    }

    private static async Task<JsonElement> GetLatestSynthesisAsync(HttpClient client, Guid jobId)
    {
        var resp = await client.GetAsync($"/api/research/jobs/{jobId}/syntheses/latest");
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("synthesis");
    }

    private static async Task<JsonElement> WaitForSynthesisCompletedAsync(HttpClient client, Guid synthesisId, int timeoutSeconds)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (true)
        {
            var resp = await client.GetAsync($"/api/research/syntheses/{synthesisId}");
            resp.EnsureSuccessStatusCode();

            var syn = await resp.Content.ReadFromJsonAsync<JsonElement>();
            var status = syn.GetProperty("status").GetString();

            if (status is "Completed" or "Failed") return syn;
            if (DateTimeOffset.UtcNow > deadline) throw new Xunit.Sdk.XunitException("Timed out waiting for synthesis completion.");

            await Task.Delay(300);
        }
    }
}