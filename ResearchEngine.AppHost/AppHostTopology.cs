using Aspire.Hosting.ApplicationModel;
using static AppHostCompose;

static class AppHostTopology
{
    public static IResourceBuilder<ContainerResource> AddApiContainer(
        IDistributedApplicationBuilder builder,
        AppHostOptions options,
        IResourceBuilder<IResourceWithConnectionString> researchDb,
        IResourceBuilder<IResourceWithConnectionString> researchRedis,
        IResourceBuilder<ContainerResource> ollama,
        IResourceBuilder<ContainerResource> ollamaInit,
        ReferenceExpression chatEndpoint,
        ReferenceExpression firecrawlEndpoint,
        ReferenceExpression? chatAliasTarget,
        ReferenceExpression? firecrawlAliasTarget)
    {
        var api = builder.AddContainer(options.ApiResourceName, options.ApiImage.Name, options.ApiImage.Tag)
            .WithOptionalImageSHA256(options.ApiImage.Sha256)
            .WithHttpEndpoint(targetPort: options.ApiContainerPort, name: "http")
            .WithEnvironment("ASPNETCORE_URLS", Url(options.AppBindHost, options.ApiContainerPort));

        ConfigureApi(
            api,
            options,
            researchDb,
            researchRedis,
            ollama,
            ollamaInit,
            chatEndpoint,
            firecrawlEndpoint,
            chatAliasTarget,
            firecrawlAliasTarget);

        return api.PublishAsDockerComposeService((_, service) =>
            SetApiComposeEnvironment(service, options));
    }

    public static IResourceBuilder<ProjectResource> AddApiProject(
        IDistributedApplicationBuilder builder,
        AppHostOptions options,
        IResourceBuilder<IResourceWithConnectionString> researchDb,
        IResourceBuilder<IResourceWithConnectionString> researchRedis,
        IResourceBuilder<ContainerResource> ollama,
        IResourceBuilder<ContainerResource> ollamaInit,
        ReferenceExpression chatEndpoint,
        ReferenceExpression firecrawlEndpoint,
        ReferenceExpression? chatAliasTarget,
        ReferenceExpression? firecrawlAliasTarget)
    {
        var api = builder.AddProject<Projects.ResearchEngine_API>(
                options.ApiResourceName,
                static project => project.ExcludeLaunchProfile = true)
            .WithHttpEndpoint(port: options.ApiPort, targetPort: options.ApiPort, name: "http", isProxied: false)
            .WithEnvironment("ASPNETCORE_URLS", Url(options.AppBindHost, options.ApiPort))
            .WithEnvironment("ASPNETCORE_ENVIRONMENT", options.AppEnvironment)
            .WithEnvironment("DOTNET_ENVIRONMENT", options.AppEnvironment);

        ConfigureApi(
            api,
            options,
            researchDb,
            researchRedis,
            ollama,
            ollamaInit,
            chatEndpoint,
            firecrawlEndpoint,
            chatAliasTarget,
            firecrawlAliasTarget);

        return api.PublishAsDockerComposeService((_, service) =>
            SetApiComposeEnvironment(service, options));
    }

    public static IResourceBuilder<ContainerResource> AddWebUiContainer(
        IDistributedApplicationBuilder builder,
        AppHostOptions options)
    {
        return builder.AddContainer(options.WebUiResourceName, options.WebUiImage.Name, options.WebUiImage.Tag)
            .WithOptionalImageSHA256(options.WebUiImage.Sha256)
            .WithHttpEndpoint(targetPort: options.WebUiContainerPort, name: "http")
            .WithEnvironment("WEBUI_PORT", options.WebUiContainerPort.ToString())
            .WithEnvironment("API_BASE_URL", options.WebUiApiBaseUrl)
            .WithEnvironment("AuthenticationOptions__ApiKeys__0", options.ApiKey);
    }

    public static IResourceBuilder<ProjectResource> AddWebUiProject(
        IDistributedApplicationBuilder builder,
        AppHostOptions options)
    {
        return builder.AddProject<Projects.ResearchEngine_WebUI>(
                options.WebUiResourceName,
                static project => project.ExcludeLaunchProfile = true)
            .WithHttpEndpoint(port: options.WebUiPort, targetPort: options.WebUiPort, name: "http", isProxied: false)
            .WithEnvironment("ASPNETCORE_URLS", Url(options.AppBindHost, options.WebUiPort))
            .WithEnvironment("ASPNETCORE_ENVIRONMENT", options.AppEnvironment)
            .WithEnvironment("DOTNET_ENVIRONMENT", options.AppEnvironment);
    }

    public static IResourceBuilder<ContainerResource> AddEdge(
        IDistributedApplicationBuilder builder,
        AppHostOptions options,
        ReferenceExpression apiUpstream,
        ReferenceExpression webUiUpstream)
    {
        var edge = builder.AddContainer(options.EdgeResourceName, options.CaddyImage.Name, options.CaddyImage.Tag)
            .WithOptionalImageSHA256(options.CaddyImage.Sha256)
            .WithVolume(options.CaddyDataVolume, options.CaddyDataPath)
            .WithEnvironment("XDG_DATA_HOME", options.CaddyDataPath)
            .WithEnvironment("API_UPSTREAM", apiUpstream)
            .WithEnvironment("WEBUI_UPSTREAM", webUiUpstream)
            .WithEnvironment("API_AUTH_TOKEN", options.ApiKey)
            .WithEnvironment("UPSTREAM_HOST", options.HostGateway)
            .WithEnvironment("API_PORT", options.AppMode == AppExecutionMode.Images ? options.ApiContainerPort.ToString() : options.ApiPort.ToString())
            .WithEnvironment("WEBUI_PORT", options.AppMode == AppExecutionMode.Images ? options.WebUiContainerPort.ToString() : options.WebUiPort.ToString())
            .WithEnvironment("API_RESOURCE_NAME", options.ApiResourceName)
            .WithEnvironment("WEBUI_RESOURCE_NAME", options.WebUiResourceName)
            .WithEnvironment("EDGE_HTTP_PORT", options.EdgeHttpPort.ToString())
            .WithEnvironment("EDGE_HTTPS_PORT", options.EdgeHttpsPort.ToString())
            .WithEnvironment("WEBUI_HTTPS_HOST", options.WebUiHttpsHost)
            .WithEnvironment("API_HTTPS_HOST", options.ApiHttpsHost)
            .WithHttpEndpoint(port: options.EdgeHttpPort, targetPort: options.EdgeHttpPort, name: "http")
            .WithEndpoint(port: options.EdgeHttpsPort, targetPort: options.EdgeHttpsPort, scheme: "https", name: "https")
            .PublishAsDockerComposeService((_, service) =>
            {
                service.Ports ??= [];
                AddPortMapping(service.Ports, options.EdgeHttpPort, options.EdgeHttpPort);
                AddPortMapping(service.Ports, options.EdgeHttpsPort, options.EdgeHttpsPort);
                RemoveVolumeTarget(service, "/etc/caddy/Caddyfile");
                SetEmbeddedFileEntrypoint(
                    service,
                    "/etc/caddy/Caddyfile",
                    ReadAppHostFile(options.Caddyfile),
                    CaddyComposeStartCommand());
            });

        if (builder.ExecutionContext.IsRunMode)
        {
            edge.WithBindMount(options.Caddyfile, "/etc/caddy/Caddyfile", isReadOnly: true);
        }

        return edge;
    }

    public static void AddOptionalFullProfileWaits<T>(
        IResourceBuilder<T> api,
        IResourceBuilder<ContainerResource>? firecrawl,
        IResourceBuilder<ContainerResource>? vllm)
        where T : IResourceWithWaitSupport
    {
        if (firecrawl is not null)
        {
            api.WaitFor(firecrawl);
        }

        if (vllm is not null)
        {
            api.WaitFor(vllm);
        }
    }

    public static void AddOptionalCertificateInstaller(
        IDistributedApplicationBuilder builder,
        AppHostOptions options,
        IResourceBuilder<ContainerResource> edge)
    {
        if (!options.InstallCaddyCertificate)
        {
            return;
        }

        builder.AddExecutable(
                options.CaddyCertificateInstallerResourceName,
                "powershell",
                "../Deploy",
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                "trust-caddy-local-ca.ps1",
                "-ContainerRuntime",
                options.ContainerRuntime)
            .WaitFor(edge);
    }

    public static FirecrawlResources AddFirecrawlStack(IDistributedApplicationBuilder builder, AppHostOptions options)
    {
        var crawlPostgres = builder.AddContainer(options.CrawlPostgresResourceName, options.CrawlPostgresImage.Name, options.CrawlPostgresImage.Tag)
            .WithOptionalImageSHA256(options.CrawlPostgresImage.Sha256)
            .WithEnvironment("POSTGRES_DB", options.CrawlPostgresDatabase)
            .WithEnvironment("POSTGRES_USER", options.CrawlPostgresUser)
            .WithEnvironment("POSTGRES_PASSWORD", options.CrawlPostgresPassword)
            .WithVolume(options.CrawlPostgresDataVolume, options.CrawlPostgresDataPath)
            .WithEndpoint(targetPort: options.CrawlPostgresPort, name: "tcp")
            .PublishAsDockerComposeService((_, service) =>
            {
                RemoveVolumeTarget(service, "/docker-entrypoint-initdb.d/090-nuq-schema-fix.sql");
                SetEmbeddedFileEntrypoint(
                    service,
                    "/docker-entrypoint-initdb.d/090-nuq-schema-fix.sql",
                    ReadAppHostFile(options.CrawlNuqSchemaFixPath),
                    "exec docker-entrypoint.sh postgres");
            });

        if (builder.ExecutionContext.IsRunMode)
        {
            crawlPostgres.WithBindMount(
                options.CrawlNuqSchemaFixPath,
                "/docker-entrypoint-initdb.d/090-nuq-schema-fix.sql",
                isReadOnly: true);
        }

        var crawlRedis = builder.AddContainer(options.CrawlRedisResourceName, options.CrawlRedisImage.Name, options.CrawlRedisImage.Tag)
            .WithOptionalImageSHA256(options.CrawlRedisImage.Sha256)
            .WithArgs("redis-server", "--bind", options.CrawlRedisBindHost, "--port", options.CrawlRedisPort.ToString())
            .WithEndpoint(targetPort: options.CrawlRedisPort, name: "tcp");

        var crawlRabbitUser = builder.AddParameter(
            "crawl-rabbitmq-user",
            options.CrawlRabbitMqUser,
            publishValueAsDefault: true);
        var crawlRabbitPassword = builder.AddParameter(
            "crawl-rabbitmq-password",
            options.CrawlRabbitMqPassword,
            secret: true);
        var crawlRabbit = builder.AddRabbitMQ(
                options.CrawlRabbitMqResourceName,
                userName: crawlRabbitUser,
                password: crawlRabbitPassword,
                port: null)
            .WithManagementPlugin()
            .PublishAsDockerComposeService((_, service) =>
            {
                SetServiceEnvironment(service, "RABBITMQ_DEFAULT_USER", options.CrawlRabbitMqUser);
                SetServiceEnvironment(service, "RABBITMQ_DEFAULT_PASS", options.CrawlRabbitMqPassword);
            });

        var playwright = builder.AddContainer(options.PlaywrightResourceName, options.PlaywrightImage.Name, options.PlaywrightImage.Tag)
            .WithOptionalImageSHA256(options.PlaywrightImage.Sha256)
            .WithEnvironment("PORT", options.PlaywrightPort.ToString())
            .WithVolume(options.PlaywrightDataVolume, options.PlaywrightDataPath)
            .WithHttpEndpoint(targetPort: options.PlaywrightPort, name: "http");

        var searxng = builder.AddContainer(options.SearxngResourceName, options.SearxngImage.Name, options.SearxngImage.Tag)
            .WithOptionalImageSHA256(options.SearxngImage.Sha256)
            .WithEnvironment("SEARXNG_SECRET", options.SearxngSecret)
            .WithEnvironment("GRANIAN_HOST", options.SearxngBindHost)
            .WithEnvironment("GRANIAN_PORT", options.SearxngPort.ToString())
            .WithVolume(options.SearxngCacheVolume, options.SearxngCachePath)
            .WithHttpEndpoint(targetPort: options.SearxngPort, name: "http")
            .PublishAsDockerComposeService((_, service) =>
            {
                RemoveVolumeTarget(service, "/etc/searxng/settings.yml");
                SetEmbeddedFileEntrypoint(
                    service,
                    "/etc/searxng/settings.yml",
                    ReadAppHostFile(options.SearxngSettingsPath),
                    "exec /usr/local/searxng/entrypoint.sh");
            });

        if (builder.ExecutionContext.IsRunMode)
        {
            searxng.WithBindMount(options.SearxngSettingsPath, "/etc/searxng/settings.yml", isReadOnly: true);
        }

        var firecrawl = builder.AddContainer(options.FirecrawlResourceName, options.FirecrawlImage.Name, options.FirecrawlImage.Tag)
            .WithOptionalImageSHA256(options.FirecrawlImage.Sha256)
            .WithEnvironment("PORT", options.FirecrawlPort.ToString())
            .WithEnvironment("HOST", options.FirecrawlBindHost)
            .WithEnvironment("USE_DB_AUTHENTICATION", options.FirecrawlUseDbAuthentication.ToString().ToLowerInvariant())
            .WithEnvironment("POSTGRES_DB", options.CrawlPostgresDatabase)
            .WithEnvironment("POSTGRES_USER", options.CrawlPostgresUser)
            .WithEnvironment("POSTGRES_PASSWORD", options.CrawlPostgresPassword)
            .WithEnvironment("BULL_AUTH_KEY", options.FirecrawlBullAuthKey)
            .WithEnvironment("REDIS_URL", string.Concat("redis://", crawlRedis.Resource.Name, ":", options.CrawlRedisPort))
            .WithEnvironment("REDIS_RATE_LIMIT_URL", string.Concat("redis://", crawlRedis.Resource.Name, ":", options.CrawlRedisPort))
            .WithEnvironment("PLAYWRIGHT_MICROSERVICE_URL", ReferenceExpression.Create($"{playwright.GetEndpoint("http")}/scrape"))
            .WithEnvironment("SEARXNG_ENDPOINT", ReferenceExpression.Create($"{searxng.GetEndpoint("http")}"))
            .WithEnvironment("NUQ_DATABASE_URL", string.Concat("postgresql://", options.CrawlPostgresUser, ":", options.CrawlPostgresPassword, "@", crawlPostgres.Resource.Name, ":", options.CrawlPostgresPort, "/", options.CrawlPostgresDatabase, "?sslmode=disable"))
            .WithEnvironment("NUQ_RABBITMQ_URL", string.Concat("amqp://", options.CrawlRabbitMqUser, ":", options.CrawlRabbitMqPassword, "@", crawlRabbit.Resource.Name, ":", options.CrawlRabbitMqPort))
            .WithEnvironment("DATABASE_URL", string.Concat("postgresql://", options.CrawlPostgresUser, ":", options.CrawlPostgresPassword, "@", crawlPostgres.Resource.Name, ":", options.CrawlPostgresPort, "/", options.CrawlPostgresDatabase, "?sslmode=disable"))
            .WithEnvironment("BLOCK_MEDIA", options.FirecrawlBlockMedia.ToString().ToLowerInvariant())
            .WithEnvironment("MAX_CPU", options.FirecrawlMaxCpu)
            .WithEnvironment("MAX_RAM", options.FirecrawlMaxRam)
            .WithVolume(options.FirecrawlDataVolume, options.FirecrawlDataPath)
            .WithHttpEndpoint(targetPort: options.FirecrawlPort, name: "http")
            .PublishAsDockerComposeService((_, service) =>
                SetFirecrawlComposeEntrypoint(service, crawlRabbit.Resource.Name, options.CrawlRabbitMqPort))
            .WaitFor(crawlPostgres)
            .WaitFor(crawlRedis)
            .WaitFor(crawlRabbit)
            .WaitFor(playwright)
            .WaitFor(searxng);

        return new FirecrawlResources(firecrawl);
    }

    public static IResourceBuilder<ContainerResource> AddVllm(IDistributedApplicationBuilder builder, AppHostOptions options)
    {
        var vllm = builder.AddContainer(options.VllmResourceName, options.VllmImage.Name, options.VllmImage.Tag)
            .WithOptionalImageSHA256(options.VllmImage.Sha256)
            .WithEnvironment("HF_HOME", options.VllmHuggingFaceCachePath)
            .WithEnvironment("HUGGINGFACE_HUB_CACHE", options.VllmHuggingFaceCachePath)
            .WithVolume(options.VllmHuggingFaceCacheVolume, options.VllmHuggingFaceCachePath)
            .WithVolume(options.VllmCompileCacheVolume, options.VllmCompileCachePath)
            .WithArgs(options.VllmArgs)
            .WithHttpEndpoint(targetPort: options.VllmPort, name: "http");

        foreach (var runtimeArg in options.VllmContainerRuntimeArgs)
        {
            vllm.WithContainerRuntimeArgs(runtimeArg);
        }

        return vllm.PublishAsDockerComposeService((_, service) =>
        {
            if (options.VllmContainerRuntimeArgs.Length == 0)
            {
                return;
            }

            service.Devices ??= [];
            foreach (var runtimeArg in options.VllmContainerRuntimeArgs)
            {
                const string devicePrefix = "--device=";
                if (runtimeArg.StartsWith(devicePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    service.Devices.Add(runtimeArg[devicePrefix.Length..]);
                }
            }
        });
    }

    private static IResourceBuilder<T> ConfigureApi<T>(
        IResourceBuilder<T> api,
        AppHostOptions options,
        IResourceBuilder<IResourceWithConnectionString> researchDb,
        IResourceBuilder<IResourceWithConnectionString> researchRedis,
        IResourceBuilder<ContainerResource> ollama,
        IResourceBuilder<ContainerResource> ollamaInit,
        ReferenceExpression chatEndpoint,
        ReferenceExpression firecrawlEndpoint,
        ReferenceExpression? chatAliasTarget,
        ReferenceExpression? firecrawlAliasTarget)
        where T : IResourceWithEnvironment, IResourceWithWaitSupport
    {
        api
            .WithReference(researchDb, "ResearchDb")
            .WithReference(researchDb, "HangfireDb")
            .WithEnvironment("RedisEventBusOptions__ConnectionString", researchRedis)
            .WithEnvironment("ChatConfig__Endpoint", chatEndpoint)
            .WithEnvironment("ChatConfig__ApiKey", options.ChatApiKey)
            .WithEnvironment("ChatConfig__ModelId", options.ChatModelId)
            .WithEnvironment("ChatConfig__MaxContextLength", options.ChatMaxContextLength.ToString())
            .WithEnvironment("ChatConfig__MaxOutputTokens", options.ChatMaxOutputTokens.ToString())
            .WithEnvironment("EmbeddingConfig__Endpoint", ReferenceExpression.Create($"{ollama.GetEndpoint("http")}/v1"))
            .WithEnvironment("EmbeddingConfig__ApiKey", options.EmbeddingApiKey)
            .WithEnvironment("EmbeddingConfig__ModelId", options.EmbeddingModelId)
            .WithEnvironment("EmbeddingConfig__Dimension", options.EmbeddingDimension.ToString())
            .WithEnvironment("FirecrawlOptions__BaseUrl", firecrawlEndpoint)
            .WithEnvironment("FirecrawlOptions__ApiKey", options.FirecrawlApiKey)
            .WithEnvironment("FirecrawlOptions__HttpClientTimeoutSeconds", options.FirecrawlTimeoutSeconds.ToString())
            .WithEnvironment("EndpointAliasOptions__Aliases__0__Host", HostFromAbsoluteUrl(options.ChatEndpoint))
            .WithEnvironment("EndpointAliasOptions__Aliases__0__Target", chatAliasTarget ?? chatEndpoint)
            .WithEnvironment("EndpointAliasOptions__Aliases__1__Host", HostFromAbsoluteUrl(options.FirecrawlBaseUrl))
            .WithEnvironment("EndpointAliasOptions__Aliases__1__Target", firecrawlAliasTarget ?? firecrawlEndpoint)
            .WithEnvironment("OTEL_SERVICE_NAME", options.ApiResourceName)
            .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc")
            .WithEnvironment("OTEL_RESOURCE_ATTRIBUTES", string.Concat("service.namespace=ResearchEngine,deployment.environment.name=", options.AppEnvironment))
            .WithEnvironment("AuthenticationOptions__Enabled", options.AuthenticationEnabled.ToString())
            .WithEnvironment("AuthenticationOptions__ApiKeys__0", options.ApiKey)
            .WithEnvironment("Cors__AllowedOrigins__0", string.Concat("http://localhost:", options.EdgeHttpPort))
            .WithEnvironment("Cors__AllowedOrigins__1", string.Concat("https://", options.WebUiHttpsHost, ":", options.EdgeHttpsPort))
            .WithEnvironment("Cors__AllowedOrigins__2", string.Concat("http://localhost:", options.WebUiPort))
            .WithEnvironment("Hangfire__EnableServer", options.HangfireEnableServer.ToString())
            .WaitFor(researchDb)
            .WaitFor(researchRedis)
            .WaitFor(ollama)
            .WaitForCompletion(ollamaInit);

        if (options.AppMode == AppExecutionMode.Source &&
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL")))
        {
            api.WithEnvironment(
                "OTEL_EXPORTER_OTLP_ENDPOINT",
                Environment.GetEnvironmentVariable("ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL")!);
        }

        return api;
    }
}

sealed record FirecrawlResources(IResourceBuilder<ContainerResource> Firecrawl);
