using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.PostgreSql.Factories;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ResearchEngine.Infrastructure;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace ResearchEngine.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class ContainersCollection : ICollectionFixture<ContainersFixture>
{
    public const string Name = "Containers";
}

public sealed class ContainersFixture : IAsyncLifetime
{
    public const int ResearchDbMaxPoolSize = 20;
    public const int HangfireDbMaxPoolSize = 20;

    private const string ResearchDatabaseName = "research_tests";
    private const string HangfireDatabaseName = "hangfire_tests";
    
    // Single shared factory/host for ALL tests in this collection
    public CustomWebApplicationFactory Factory { get; private set; } = null!;

    private BackgroundJobServer? HangfireServer;

    public PostgreSqlContainer Postgres { get; }
    public RedisContainer Redis { get; }

    public ContainersFixture()
    {
        Postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17")
            .WithDatabase("research")   // container default, we create our own DBs inside
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        Redis = new RedisBuilder("redis:7-alpine")
            .Build();
    }

    public async Task InitializeAsync()
    {
        await Postgres.StartAsync();
        await Redis.StartAsync();

        // Create both DBs
        await PostgresTestDatabase.EnsureDatabaseExistsAsync(GetAdminConnectionString(), ResearchDatabaseName);
        await PostgresTestDatabase.EnsureDatabaseExistsAsync(GetAdminConnectionString(), HangfireDatabaseName);

        var researchConn = BuildDbConnectionString(ResearchDatabaseName);
        var hangfireConn = GetHangfireConnectionString();

        // IMPORTANT: reset/install schema BEFORE starting server
        await PostgresTestDatabase.ResetHangfireSchemaAsync(hangfireConn, schema: "hangfire");
        await EnsureHangfireSchemaAsync(hangfireConn, "hangfire");

        // Set env vars ONCE before building the host
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("ConnectionStrings__ResearchDb", researchConn);
        Environment.SetEnvironmentVariable("ConnectionStrings__HangfireDb", hangfireConn);

        // Redis (single DB ok for now)
        var redisHost = Redis.Hostname;
        var redisPort = Redis.GetMappedPublicPort(6379);
        
        Environment.SetEnvironmentVariable("RedisEventBusOptions__ConnectionString",
            $"{redisHost}:{redisPort},abortConnect=false,defaultDatabase=0");

        Environment.SetEnvironmentVariable("EmbeddingConfig__Endpoint", "http://fake");
        Environment.SetEnvironmentVariable("EmbeddingConfig__ApiKey", "fake");
        Environment.SetEnvironmentVariable("EmbeddingConfig__ModelId", "fake");
        Environment.SetEnvironmentVariable("EmbeddingConfig__Dimension", "1024");

        Environment.SetEnvironmentVariable("Hangfire__QueuePollMs", "200");

        Environment.SetEnvironmentVariable("IpRateLimiting__Enabled", "false");

        // Build single shared factory (ONE host for all tests)
        Factory = new CustomWebApplicationFactory(this);

        // Apply migrations once
        using (var scope = Factory.Services.CreateScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ResearchDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            await db.Database.MigrateAsync();
        }

        // Start ONE hangfire server for whole run
        var storageOptions = new PostgreSqlStorageOptions
        {
            SchemaName = "hangfire",
            PrepareSchemaIfNecessary = false,
            QueuePollInterval = TimeSpan.FromMilliseconds(200),
        };

        var storage = new PostgreSqlStorage(
            new NpgsqlConnectionFactory(hangfireConn, storageOptions),
            storageOptions);

        HangfireServer = new BackgroundJobServer(new BackgroundJobServerOptions {
            WorkerCount = 2,
            Queues = ["jobs", "synthesis"],
            ServerName = $"tests-{Environment.MachineName}"
        }, storage);

        // Redis readiness probe (optional)
        await using (var mux = await ConnectionMultiplexer.ConnectAsync($"{redisHost}:{redisPort},abortConnect=false"))
        {
            await mux.GetDatabase(0).PingAsync();
        }
    }

    public async Task DisposeAsync()
    {
        HangfireServer?.Dispose();
        await Redis.DisposeAsync();
        await Postgres.DisposeAsync();

        // optional but nice after full run
        Npgsql.NpgsqlConnection.ClearAllPools();
        await Task.CompletedTask;
    }

    public HttpClient CreateClient()
        => Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public string GetAdminConnectionString()
    {
        var csb = new NpgsqlConnectionStringBuilder(Postgres.GetConnectionString())
        {
            Database = "postgres",
            Pooling = true,
            MaxPoolSize = 5
        };
        return csb.ConnectionString;
    }

    public string BuildDbConnectionString(string databaseName)
    {
        var csb = new NpgsqlConnectionStringBuilder(Postgres.GetConnectionString())
        {
            Database = databaseName,
            Pooling = true,
            MaxPoolSize = ResearchDbMaxPoolSize
        };
        return csb.ConnectionString;
    }

    public string GetHangfireConnectionString()
    {
        var csb = new NpgsqlConnectionStringBuilder(Postgres.GetConnectionString())
        {
            Database = HangfireDatabaseName,
            Pooling = true,
            MaxPoolSize = HangfireDbMaxPoolSize
        };
        return csb.ConnectionString;
    }

    private static async Task EnsureHangfireSchemaAsync(string hangfireConnString, string schemaName)
    {
        await using var conn = new NpgsqlConnection(hangfireConnString);
        await conn.OpenAsync();
        PostgreSqlObjectsInstaller.Install(conn, schemaName);
    }
}