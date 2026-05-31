static class AppHostCompose
{
    public static string Url(string host, int port)
        => $"http://{host}:{port}";

    public static string HostFromAbsoluteUrl(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return uri.Host;
        }

        throw new InvalidOperationException($"Expected an absolute URL, but got '{value}'.");
    }

    public static void AddPortMapping(ICollection<string> ports, int hostPort, int containerPort)
    {
        var mapping = $"{hostPort}:{containerPort}";
        if (!ports.Contains(mapping))
        {
            ports.Add(mapping);
        }
    }

    public static void SetServiceEnvironment(
        Aspire.Hosting.Docker.Resources.ComposeNodes.Service service,
        string name,
        string value)
    {
        service.Environment ??= [];
        service.Environment[name] = value;
    }

    public static void SetApiComposeEnvironment(
        Aspire.Hosting.Docker.Resources.ComposeNodes.Service service,
        AppHostOptions options)
    {
        var postgresHost = options.ResearchPostgresResourceName;
        var postgresPort = 5432;
        var database = options.ResearchDatabaseName;
        var user = options.ResearchPostgresUser;
        var password = options.ResearchPostgresPassword;
        var connectionString = $"Host={postgresHost};Port={postgresPort};Username={user};Password={password};Database={database}";
        var uri = $"postgresql://{user}:{password}@{postgresHost}:{postgresPort}/{database}";
        var jdbc = $"jdbc:postgresql://{postgresHost}:{postgresPort}/{database}";

        SetServiceEnvironment(service, "ConnectionStrings__ResearchDb", connectionString);
        SetServiceEnvironment(service, "RESEARCHDB_HOST", postgresHost);
        SetServiceEnvironment(service, "RESEARCHDB_PORT", postgresPort.ToString());
        SetServiceEnvironment(service, "RESEARCHDB_USERNAME", user);
        SetServiceEnvironment(service, "RESEARCHDB_PASSWORD", password);
        SetServiceEnvironment(service, "RESEARCHDB_URI", uri);
        SetServiceEnvironment(service, "RESEARCHDB_JDBCCONNECTIONSTRING", jdbc);
        SetServiceEnvironment(service, "RESEARCHDB_DATABASENAME", database);

        SetServiceEnvironment(service, "ConnectionStrings__HangfireDb", connectionString);
        SetServiceEnvironment(service, "HANGFIREDB_HOST", postgresHost);
        SetServiceEnvironment(service, "HANGFIREDB_PORT", postgresPort.ToString());
        SetServiceEnvironment(service, "HANGFIREDB_USERNAME", user);
        SetServiceEnvironment(service, "HANGFIREDB_PASSWORD", password);
        SetServiceEnvironment(service, "HANGFIREDB_URI", uri);
        SetServiceEnvironment(service, "HANGFIREDB_JDBCCONNECTIONSTRING", jdbc);
        SetServiceEnvironment(service, "HANGFIREDB_DATABASENAME", database);

        SetServiceEnvironment(
            service,
            "RedisEventBusOptions__ConnectionString",
            $"{options.ResearchRedisResourceName}:6379,password={options.ResearchRedisPassword}");

        SetServiceEnvironment(service, "OTEL_EXPORTER_OTLP_ENDPOINT", "http://compose-dashboard:18889");
    }

    public static string CaddyComposeStartCommand()
        => "exec caddy run --config /etc/caddy/Caddyfile --adapter caddyfile";

    public static void SetFirecrawlComposeEntrypoint(
        Aspire.Hosting.Docker.Resources.ComposeNodes.Service service,
        string rabbitMqHost,
        int rabbitMqPort)
    {
        service.Entrypoint = ["/bin/sh", "-c"];
        service.Command =
        [
            $"""
            until node -e "const net=require('net'); const s=net.connect({rabbitMqPort}, '{rabbitMqHost}'); s.on('connect', () => process.exit(0)); s.on('error', () => process.exit(1)); setTimeout(() => process.exit(1), 1000);"; do
              echo "Waiting for RabbitMQ at {rabbitMqHost}:{rabbitMqPort}..."
              sleep 2
            done
            exec docker-entrypoint.sh node dist/src/harness.js --start-docker
            """
        ];
    }

    public static void RemoveVolumeTarget(
        Aspire.Hosting.Docker.Resources.ComposeNodes.Service service,
        string target)
    {
        service.Volumes?.RemoveAll(v => string.Equals(v.Target, target, StringComparison.Ordinal));
    }

    public static void SetEmbeddedFileEntrypoint(
        Aspire.Hosting.Docker.Resources.ComposeNodes.Service service,
        string target,
        string content,
        string afterWriteCommand)
    {
        service.Entrypoint = ["/bin/sh", "-c"];
        service.Command =
        [
            EscapeComposeCommand($"""
            mkdir -p "$(dirname '{target}')"
            cat > '{target}' <<'RESEARCH_ENGINE_EMBEDDED_FILE'
            {content}
            RESEARCH_ENGINE_EMBEDDED_FILE
            {afterWriteCommand}
            """)
        ];
    }

    public static string ReadAppHostFile(string path)
    {
        foreach (var basePath in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var candidate = Path.GetFullPath(Path.Combine(basePath, path));
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            candidate = Path.GetFullPath(Path.Combine(basePath, "ResearchEngine.AppHost", path));
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
        }

        throw new FileNotFoundException($"Could not find AppHost file '{path}'.");
    }

    private static string EscapeComposeCommand(string value)
        => value.Replace("$", "$$", StringComparison.Ordinal);
}
