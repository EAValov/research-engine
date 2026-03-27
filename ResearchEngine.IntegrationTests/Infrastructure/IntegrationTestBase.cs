using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ResearchEngine.Domain;
using ResearchEngine.IntegrationTests.Helpers;

namespace ResearchEngine.IntegrationTests.Infrastructure;

[Collection(ContainersCollection.Name)]
public abstract class IntegrationTestBase
{
    protected readonly ContainersFixture Containers;
    protected WebApplicationFactory<Program> Factory => Containers.Factory;

    protected IntegrationTestBase(ContainersFixture containers)
        => Containers = containers;

    protected HttpClient CreateClient()
        => Containers.CreateClient();

    protected static async Task RunJobInlineAsync(WebApplicationFactory<Program> factory, Guid jobId, CancellationToken ct = default)
    {
        using var scope = factory.Services.CreateScope();

        var orchestrator = scope.ServiceProvider.GetRequiredService<IResearchOrchestrator>();

        try
        {
            await orchestrator.RunJobBackgroundAsync(jobId);
        }
        catch
        {
            // The orchestrator should mark job/synthesis as failed and persist the error.
            // We swallow so the test can assert via API.
        }
    }

    protected static async Task<Guid> CreateJobAsync(HttpClient client, string query, string? discoveryMode = null)
    {
        var createReq = new
        {
            query,
            clarifications = Array.Empty<object>(),
            breadth = 2,
            depth = 2,
            language = "en",
            region = (string?)null,
            discoveryMode
        };

        var resp = await client.PostAsJsonAsync("/api/research/jobs", createReq);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("jobId").GetGuid();
    }

    protected static async Task WaitForJobCompletionAsync(HttpClient client, Guid jobId, int timeoutSeconds)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (true)
        {
            var evResp = await client.GetAsync($"/api/research/jobs/{jobId}/events");
            evResp.EnsureSuccessStatusCode();

            var events = await evResp.Content.ReadFromJsonAsync<JsonElement>();
            var stages = events.EnumerateArray().Select(GetStageName).ToList();

            if (stages.Contains("Completed")) return;
            if (stages.Contains("Failed")) throw new Xunit.Sdk.XunitException("Job failed unexpectedly.");
            if (DateTimeOffset.UtcNow > deadline) throw new Xunit.Sdk.XunitException("Timed out waiting for job completion.");

            await Task.Delay(300);
        }
    }

    protected static async Task<List<JsonElement>> ListSourcesAsync(HttpClient client, Guid jobId)
    {
        var resp = await client.GetAsync($"/api/research/jobs/{jobId}/sources");
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("sources").EnumerateArray().ToList();
    }

    protected static async Task<List<JsonElement>> ListLearningsAsync(HttpClient client, Guid jobId)
    {
        var resp = await client.GetAsync($"/api/research/jobs/{jobId}/learnings?skip=0&take=200");
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("learnings").EnumerateArray().ToList();
    }

    protected static async Task<JsonElement> GetLatestSynthesisAsync(HttpClient client, Guid jobId)
    {
        var resp = await client.GetAsync($"/api/research/jobs/{jobId}/syntheses/latest");
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var synthesis = json.GetProperty("synthesis");
        Assert.Equal(JsonValueKind.Object, synthesis.ValueKind);
        return synthesis;
    }

    protected static string GetStageName(JsonElement el)
    {
        // If el is already the stage value (number/string), interpret it directly.
        if (el.ValueKind == JsonValueKind.Number)
            return ((ResearchEventStage)el.GetInt32()).ToString();

        if (el.ValueKind == JsonValueKind.String)
            return el.GetString()!;

        // Otherwise expect an event object with a "stage" property.
        if (el.ValueKind == JsonValueKind.Object)
        {
            var stageEl = el.GetProperty("stage");
            return stageEl.ValueKind switch
            {
                JsonValueKind.String => stageEl.GetString()!,
                JsonValueKind.Number => ((ResearchEventStage)stageEl.GetInt32()).ToString(),
                _ => throw new InvalidOperationException($"Unexpected JSON kind for stage: {stageEl.ValueKind}")
            };
        }

        throw new InvalidOperationException($"Unexpected JSON kind for event/stage: {el.ValueKind}");
    }
}
