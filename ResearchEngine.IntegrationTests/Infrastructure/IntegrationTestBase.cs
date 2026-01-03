using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResearchEngine.Infrastructure;
using ResearchEngine.IntegrationTests.Helpers;
using StackExchange.Redis;

namespace ResearchEngine.IntegrationTests.Infrastructure;

[Collection(ContainersCollection.Name)]
public abstract class IntegrationTestBase : IAsyncDisposable
{
    protected readonly ContainersFixture Containers;
    protected readonly CustomWebApplicationFactory Factory;

    private readonly PostgresTestDatabase _db;
    private readonly int _redisDb;

    // ---- Redis DB leasing (process-wide) ----
    private static readonly ConcurrentBag<int> RedisDbPool = new();
    private static volatile bool _poolInitialized;
    private static readonly object PoolLock = new();

    protected IntegrationTestBase(ContainersFixture containers)
    {
        Containers = containers;

        _db = PostgresTestDatabase.CreateAsync(
                adminConnectionString: containers.GetAdminConnectionString(),
                buildDbConnectionString: containers.BuildDbConnectionString)
            .GetAwaiter().GetResult();

        _redisDb = LeaseRedisDb(containers);

        // Clean only this logical DB (isolation)
        FlushRedisDb(containers, _redisDb);

        Factory = new CustomWebApplicationFactory(Containers, _db.ConnectionString, _redisDb);

        using var scope = Factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ResearchDbContext>>();
        using var db = dbFactory.CreateDbContext();
        db.Database.Migrate();
    }

    protected HttpClient CreateClient()
        => Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public async ValueTask DisposeAsync()
    {
        Factory.Dispose();
        await _db.DisposeAsync();

        ReturnRedisDb(_redisDb);
    }

    private static int LeaseRedisDb(ContainersFixture containers)
    {
        EnsureRedisPoolInitialized(containers);

        if (!RedisDbPool.TryTake(out var db))
            throw new Xunit.Sdk.XunitException(
                $"Redis DB pool exhausted. Increase ContainersFixture.RedisDbCount (currently {ContainersFixture.RedisDbCount}) " +
                $"or reduce parallelism.");

        return db;
    }

    private static void ReturnRedisDb(int db)
        => RedisDbPool.Add(db);

    private static void EnsureRedisPoolInitialized(ContainersFixture containers)
    {
        if (_poolInitialized) return;

        lock (PoolLock)
        {
            if (_poolInitialized) return;

            // Reserve DB 0 optionally (sometimes used by readiness probes / misc)
            // Feel free to include 0 if you want:
            for (int i = 1; i < ContainersFixture.RedisDbCount; i++)
                RedisDbPool.Add(i);

            _poolInitialized = true;
        }
    }

    private static void FlushRedisDb(ContainersFixture containers, int dbIndex)
    {
        var host = containers.Redis.Hostname;
        var port = containers.Redis.GetMappedPublicPort(6379);

        using var mux = ConnectionMultiplexer.Connect($"{host}:{port},abortConnect=false");
        var db = mux.GetDatabase(dbIndex);

        // FLUSHDB affects only selected logical DB
        db.Execute("FLUSHDB");
    }

    protected static async Task<Guid> CreateJobAsync(HttpClient client, string query)
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

    protected static async Task WaitForJobCompletionAsync(HttpClient client, Guid jobId, int timeoutSeconds)
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
}
