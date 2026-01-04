using Npgsql;

namespace ResearchEngine.IntegrationTests.Infrastructure;

/// <summary>
/// Owns a per-test PostgreSQL database inside a long-lived Postgres container.
/// EF Core can migrate inside the database, but creating/dropping the database
/// itself is a server-level operation that requires admin SQL.
/// Also used to create a persistent hangfire DB.
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
        await EnsureDatabaseExistsAsync(adminConnectionString, dbName, ct);

        var dbConn = buildDbConnectionString(dbName);
        return new PostgresTestDatabase(adminConnectionString, dbName, dbConn);
    }

    /// <summary>
    /// Postgres-specific: creates DB if missing.
    /// </summary>
    public static async Task EnsureDatabaseExistsAsync(
        string adminConnectionString,
        string databaseName,
        CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(adminConnectionString);
        await conn.OpenAsync(ct);

        const string existsSql = """
        SELECT 1 FROM pg_database WHERE datname = @db;
        """;

        await using (var existsCmd = new NpgsqlCommand(existsSql, conn))
        {
            existsCmd.Parameters.AddWithValue("db", databaseName);

            var exists = await existsCmd.ExecuteScalarAsync(ct);
            if (exists is not null)
                return;
        }

        // TEMPLATE template0 avoids copying extensions/settings from a template DB.
        var createSql = $@"CREATE DATABASE ""{databaseName}"" TEMPLATE template0 ENCODING 'UTF8';";
        await using (var createCmd = new NpgsqlCommand(createSql, conn))
        {
            await createCmd.ExecuteNonQueryAsync(ct);
        }
    }

    public static async Task ResetHangfireSchemaAsync(
        string hangfireConnectionString,
        string schema,
        CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(hangfireConnectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
        DO $$
        DECLARE r RECORD;
        BEGIN
        FOR r IN
            SELECT tablename
            FROM pg_tables
            WHERE schemaname = '{schema}'
        LOOP
            EXECUTE format('TRUNCATE TABLE {schema}.%I RESTART IDENTITY CASCADE;', r.tablename);
        END LOOP;
        END $$;
        """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }


    public async ValueTask DisposeAsync()
    {
        try
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
        finally
        {
            NpgsqlConnection.ClearAllPools();
        }
    }
}
