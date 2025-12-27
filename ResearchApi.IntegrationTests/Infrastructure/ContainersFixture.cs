using System.Threading.Tasks;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace ResearchApi.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class ContainersCollection : ICollectionFixture<ContainersFixture>
{
    public const string Name = "Containers";
}

public sealed class ContainersFixture : IAsyncLifetime
{
    public PostgreSqlContainer Postgres { get; }
    public RedisContainer Redis { get; }

    public ContainersFixture()
    {
        // Postgres 17 with pgvector
        Postgres = new PostgreSqlBuilder()
            .WithImage("pgvector/pgvector:pg17")
            .WithDatabase("research")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        Redis = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();
    }

    public async Task InitializeAsync()
    {
        await Postgres.StartAsync();
        await Redis.StartAsync();

        // Redis readiness probe (prevents race on first Connect() in app DI)
        var host = Redis.Hostname;
        var port = Redis.GetMappedPublicPort(6379);
        var conn = await StackExchange.Redis.ConnectionMultiplexer.ConnectAsync($"{host}:{port},abortConnect=false");

        await conn.GetDatabase().PingAsync();
        await conn.CloseAsync();
    }

    public async Task DisposeAsync()
    {
        await Redis.DisposeAsync();
        await Postgres.DisposeAsync();
    }
}