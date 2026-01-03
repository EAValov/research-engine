using Npgsql;

namespace ResearchEngine.IntegrationTests.Infrastructure;

/// <summary>
/// Owns a per-test PostgreSQL database inside a long-lived Postgres container.
/// EF Core can migrate inside the database, but creating/dropping the database
/// itself is a server-level operation that requires admin SQL.
/// </summary>
public sealed class PostgresTestDatabase : IAsyncDisposable
{
    public string DatabaseName { get; }
    public string ConnectionString { get; }

    private readonly string _adminConnectionString;

    private PostgresTestDatabase(string adminConnectionString, string databaseName, string dbConnectionString)
    {
        _adminConnectionString = adminConnectionString;
        DatabaseName = databaseName;
        ConnectionString = dbConnectionString;
    }

    public static async Task<PostgresTestDatabase> CreateAsync(
        string adminConnectionString,
        Func<string, string> buildDbConnectionString,
        CancellationToken ct = default)
    {
        var dbName = "research_test_" + Guid.NewGuid().ToString("N");
        var dbConn = buildDbConnectionString(dbName);

        await using var conn = new NpgsqlConnection(adminConnectionString);
        await conn.OpenAsync(ct);

        // TEMPLATE template0 avoids copying extensions/settings from a template DB.
        await using (var cmd = new NpgsqlCommand(
            $@"CREATE DATABASE ""{dbName}"" TEMPLATE template0 ENCODING 'UTF8';", conn))
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }

        return new PostgresTestDatabase(adminConnectionString, dbName, dbConn);
    }

    public async ValueTask DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_adminConnectionString);
        await conn.OpenAsync();

        // Terminate open connections so DROP succeeds even if a test died mid-request.
        const string killSql = """
        SELECT pg_terminate_backend(pid)
        FROM pg_stat_activity
        WHERE datname = @db AND pid <> pg_backend_pid();
        """;

        await using (var kill = new NpgsqlCommand(killSql, conn))
        {
            kill.Parameters.AddWithValue("db", DatabaseName);
            await kill.ExecuteNonQueryAsync();
        }

        await using (var drop = new NpgsqlCommand($@"DROP DATABASE IF EXISTS ""{DatabaseName}"";", conn))
        {
            await drop.ExecuteNonQueryAsync();
        }
    }
}