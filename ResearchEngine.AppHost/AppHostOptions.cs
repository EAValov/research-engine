sealed class AppHostOptions
{
    public required AppHostProfile Profile { get; init; }
    public required AppExecutionMode AppMode { get; init; }
    public required bool InstallCaddyCertificate { get; init; }
    public required ContainerImageReference ApiImage { get; init; }
    public required ContainerImageReference WebUiImage { get; init; }
    public required string ContainerRuntime { get; init; }
    public required string ComposeProjectName { get; init; }
    public required string HostGateway { get; init; }
    public required string AppEnvironment { get; init; }
    public required string AppBindHost { get; init; }
    public required int ApiPort { get; init; }
    public required int WebUiPort { get; init; }
    public required int EdgeHttpPort { get; init; }
    public required int EdgeHttpsPort { get; init; }
    public required int ApiContainerPort { get; init; }
    public required int WebUiContainerPort { get; init; }
    public required string WebUiHttpsHost { get; init; }
    public required string ApiHttpsHost { get; init; }
    public required bool AuthenticationEnabled { get; init; }
    public required string ApiKey { get; init; }
    public required bool HangfireEnableServer { get; init; }
    public required string WebUiApiBaseUrl { get; init; }
    public required string ApiResourceName { get; init; }
    public required string WebUiResourceName { get; init; }
    public required string EdgeResourceName { get; init; }
    public required string ResearchPostgresResourceName { get; init; }
    public required string ResearchDatabaseResourceName { get; init; }
    public required string ResearchDatabaseName { get; init; }
    public required string ResearchPostgresUser { get; init; }
    public required string ResearchPostgresPassword { get; init; }
    public required ContainerImageReference ResearchPostgresImage { get; init; }
    public required string ResearchPostgresDataVolume { get; init; }
    public required string ResearchRedisResourceName { get; init; }
    public required string ResearchRedisPassword { get; init; }
    public required ContainerImageReference ResearchRedisImage { get; init; }
    public required string OllamaResourceName { get; init; }
    public required string OllamaInitResourceName { get; init; }
    public required ContainerImageReference OllamaImage { get; init; }
    public required string OllamaBindHost { get; init; }
    public required int OllamaPort { get; init; }
    public required string OllamaDataVolume { get; init; }
    public required string OllamaDataPath { get; init; }
    public required ContainerImageReference CaddyImage { get; init; }
    public required string CaddyContainerName { get; init; }
    public required string CaddyDataVolume { get; init; }
    public required string CaddyDataPath { get; init; }
    public required string Caddyfile { get; init; }
    public required string CaddyCertificateInstallerResourceName { get; init; }
    public required string ChatEndpoint { get; init; }
    public required string ChatApiKey { get; init; }
    public required string ChatModelId { get; init; }
    public required int ChatMaxContextLength { get; init; }
    public required int ChatMaxOutputTokens { get; init; }
    public required string EmbeddingApiKey { get; init; }
    public required string EmbeddingModelId { get; init; }
    public required int EmbeddingDimension { get; init; }
    public required string FirecrawlBaseUrl { get; init; }
    public required string FirecrawlApiKey { get; init; }
    public required int FirecrawlTimeoutSeconds { get; init; }
    public required string CrawlPostgresResourceName { get; init; }
    public required ContainerImageReference CrawlPostgresImage { get; init; }
    public required string CrawlPostgresDatabase { get; init; }
    public required string CrawlPostgresUser { get; init; }
    public required string CrawlPostgresPassword { get; init; }
    public required int CrawlPostgresPort { get; init; }
    public required string CrawlPostgresDataVolume { get; init; }
    public required string CrawlPostgresDataPath { get; init; }
    public required string CrawlNuqSchemaFixPath { get; init; }
    public required string CrawlRedisResourceName { get; init; }
    public required ContainerImageReference CrawlRedisImage { get; init; }
    public required string CrawlRedisBindHost { get; init; }
    public required int CrawlRedisPort { get; init; }
    public required string CrawlRabbitMqResourceName { get; init; }
    public required string CrawlRabbitMqUser { get; init; }
    public required string CrawlRabbitMqPassword { get; init; }
    public required int CrawlRabbitMqPort { get; init; }
    public required string PlaywrightResourceName { get; init; }
    public required ContainerImageReference PlaywrightImage { get; init; }
    public required int PlaywrightPort { get; init; }
    public required string PlaywrightDataVolume { get; init; }
    public required string PlaywrightDataPath { get; init; }
    public required string SearxngResourceName { get; init; }
    public required ContainerImageReference SearxngImage { get; init; }
    public required string SearxngBindHost { get; init; }
    public required int SearxngPort { get; init; }
    public required string SearxngSecret { get; init; }
    public required string SearxngSettingsPath { get; init; }
    public required string SearxngCacheVolume { get; init; }
    public required string SearxngCachePath { get; init; }
    public required string FirecrawlResourceName { get; init; }
    public required ContainerImageReference FirecrawlImage { get; init; }
    public required string FirecrawlBindHost { get; init; }
    public required int FirecrawlPort { get; init; }
    public required bool FirecrawlUseDbAuthentication { get; init; }
    public required string FirecrawlBullAuthKey { get; init; }
    public required bool FirecrawlBlockMedia { get; init; }
    public required string FirecrawlMaxCpu { get; init; }
    public required string FirecrawlMaxRam { get; init; }
    public required string FirecrawlDataVolume { get; init; }
    public required string FirecrawlDataPath { get; init; }
    public required string VllmResourceName { get; init; }
    public required ContainerImageReference VllmImage { get; init; }
    public required int VllmPort { get; init; }
    public required string VllmHuggingFaceCacheVolume { get; init; }
    public required string VllmHuggingFaceCachePath { get; init; }
    public required string VllmCompileCacheVolume { get; init; }
    public required string VllmCompileCachePath { get; init; }
    public required string[] VllmArgs { get; init; }
    public required string[] VllmContainerRuntimeArgs { get; init; }

    public static AppHostOptions Parse(string[] args)
    {
        var values = CliArguments.Parse(args);
        var settings = AppHostSettings.Load(values);

        var appBindHost = settings.Get("APP_BIND_HOST", "app-bind-host");
        var chatModelId = settings.Get("CHAT_MODEL_ID", "chat-model-id");
        var vllmPort = settings.Int("VLLM_PORT", "vllm-port");
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["APP_BIND_HOST"] = appBindHost,
            ["CHAT_MODEL_ID"] = chatModelId,
            ["VLLM_PORT"] = vllmPort.ToString()
        };

        return new AppHostOptions
        {
            Profile = settings.Enum<AppHostProfile>("RESEARCH_ASPIRE_PROFILE", "profile"),
            AppMode = settings.Enum<AppExecutionMode>("RESEARCH_ASPIRE_APP_MODE", "app-mode"),
            InstallCaddyCertificate = settings.Bool("RESEARCH_INSTALL_CADDY_CA", "install-caddy-ca"),
            ApiImage = settings.Image("RESEARCH_API_IMAGE", "api-image"),
            WebUiImage = settings.Image("RESEARCH_WEBUI_IMAGE", "webui-image"),
            ContainerRuntime = settings.Get("RESEARCH_CONTAINER_RUNTIME", "container-runtime"),
            ComposeProjectName = settings.Get("RESEARCH_COMPOSE_PROJECT_NAME", "compose-project-name"),
            HostGateway = settings.Get("RESEARCH_ASPIRE_HOST_GATEWAY", "host-gateway"),
            AppEnvironment = settings.Get("APP_ENVIRONMENT", "app-environment"),
            AppBindHost = appBindHost,
            ApiPort = settings.Int("RESEARCH_API_PORT", "api-port"),
            WebUiPort = settings.Int("WEBUI_HOST_PORT", "webui-port"),
            EdgeHttpPort = settings.Int("EDGE_HTTP_PORT", "edge-http-port"),
            EdgeHttpsPort = settings.Int("EDGE_HTTPS_PORT", "edge-https-port"),
            ApiContainerPort = settings.Int("API_CONTAINER_PORT", "api-container-port"),
            WebUiContainerPort = settings.Int("WEBUI_CONTAINER_PORT", "webui-container-port"),
            WebUiHttpsHost = settings.Get("WEBUI_HTTPS_HOST", "webui-https-host"),
            ApiHttpsHost = settings.Get("API_HTTPS_HOST", "api-https-host"),
            AuthenticationEnabled = settings.Bool("AUTHENTICATION_ENABLED", "authentication-enabled"),
            ApiKey = settings.Get("RESEARCH_API_KEY", "api-key"),
            HangfireEnableServer = settings.Bool("HANGFIRE_ENABLE_SERVER", "hangfire-enable-server"),
            WebUiApiBaseUrl = settings.Get("WEBUI_API_BASE_URL", "webui-api-base-url"),
            ApiResourceName = settings.Get("API_RESOURCE_NAME", "api-resource-name"),
            WebUiResourceName = settings.Get("WEBUI_RESOURCE_NAME", "webui-resource-name"),
            EdgeResourceName = settings.Get("EDGE_RESOURCE_NAME", "edge-resource-name"),
            ResearchPostgresResourceName = settings.Get("RESEARCH_POSTGRES_RESOURCE_NAME", "research-postgres-resource-name"),
            ResearchDatabaseResourceName = settings.Get("RESEARCH_DATABASE_RESOURCE_NAME", "research-database-resource-name"),
            ResearchDatabaseName = settings.Get("RESEARCH_DATABASE_NAME", "research-database-name"),
            ResearchPostgresUser = settings.Get("RESEARCH_POSTGRES_USER", "research-postgres-user"),
            ResearchPostgresPassword = settings.Get("RESEARCH_POSTGRES_PASSWORD", "research-postgres-password"),
            ResearchPostgresImage = settings.Image("RESEARCH_POSTGRES_IMAGE", "research-postgres-image"),
            ResearchPostgresDataVolume = settings.Get("RESEARCH_POSTGRES_DATA_VOLUME", "research-postgres-data-volume"),
            ResearchRedisResourceName = settings.Get("RESEARCH_REDIS_RESOURCE_NAME", "research-redis-resource-name"),
            ResearchRedisPassword = settings.Get("RESEARCH_REDIS_PASSWORD", "research-redis-password"),
            ResearchRedisImage = settings.Image("RESEARCH_REDIS_IMAGE", "research-redis-image"),
            OllamaResourceName = settings.Get("OLLAMA_RESOURCE_NAME", "ollama-resource-name"),
            OllamaInitResourceName = settings.Get("OLLAMA_INIT_RESOURCE_NAME", "ollama-init-resource-name"),
            OllamaImage = settings.Image("OLLAMA_IMAGE", "ollama-image"),
            OllamaBindHost = settings.Get("OLLAMA_BIND_HOST", "ollama-bind-host"),
            OllamaPort = settings.Int("OLLAMA_PORT", "ollama-port"),
            OllamaDataVolume = settings.Get("OLLAMA_DATA_VOLUME", "ollama-data-volume"),
            OllamaDataPath = settings.Get("OLLAMA_DATA_PATH", "ollama-data-path"),
            CaddyImage = settings.Image("CADDY_IMAGE", "caddy-image"),
            CaddyContainerName = settings.Get("CADDY_CONTAINER_NAME", "caddy-container-name"),
            CaddyDataVolume = settings.Get("CADDY_DATA_VOLUME", "caddy-data-volume"),
            CaddyDataPath = settings.Get("CADDY_DATA_PATH", "caddy-data-path"),
            Caddyfile = settings.Get("CADDYFILE", "caddyfile"),
            CaddyCertificateInstallerResourceName = settings.Get("CADDY_CERTIFICATE_INSTALLER_RESOURCE_NAME", "caddy-certificate-installer-resource-name"),
            ChatEndpoint = settings.Get("CHAT_ENDPOINT", "chat-endpoint"),
            ChatApiKey = settings.Get("CHAT_API_KEY", "chat-api-key"),
            ChatModelId = chatModelId,
            ChatMaxContextLength = settings.Int("CHAT_MAX_CONTEXT_LENGTH", "chat-max-context-length"),
            ChatMaxOutputTokens = settings.Int("CHAT_MAX_OUTPUT_TOKENS", "chat-max-output-tokens"),
            EmbeddingApiKey = settings.Get("EMBEDDING_API_KEY", "embedding-api-key"),
            EmbeddingModelId = settings.Get("EMBEDDING_MODEL_ID", "embedding-model-id"),
            EmbeddingDimension = settings.Int("EMBEDDING_DIMENSION", "embedding-dimension"),
            FirecrawlBaseUrl = settings.Get("FIRECRAWL_BASE_URL", "firecrawl-base-url"),
            FirecrawlApiKey = settings.Get("FIRECRAWL_API_KEY", "firecrawl-api-key"),
            FirecrawlTimeoutSeconds = settings.Int("FIRECRAWL_TIMEOUT_SECONDS", "firecrawl-timeout-seconds"),
            CrawlPostgresResourceName = settings.Get("CRAWL_POSTGRES_RESOURCE_NAME", "crawl-postgres-resource-name"),
            CrawlPostgresImage = settings.Image("CRAWL_POSTGRES_IMAGE", "crawl-postgres-image"),
            CrawlPostgresDatabase = settings.Get("CRAWL_POSTGRES_DATABASE", "crawl-postgres-database"),
            CrawlPostgresUser = settings.Get("CRAWL_POSTGRES_USER", "crawl-postgres-user"),
            CrawlPostgresPassword = settings.Get("CRAWL_POSTGRES_PASSWORD", "crawl-postgres-password"),
            CrawlPostgresPort = settings.Int("CRAWL_POSTGRES_PORT", "crawl-postgres-port"),
            CrawlPostgresDataVolume = settings.Get("CRAWL_POSTGRES_DATA_VOLUME", "crawl-postgres-data-volume"),
            CrawlPostgresDataPath = settings.Get("CRAWL_POSTGRES_DATA_PATH", "crawl-postgres-data-path"),
            CrawlNuqSchemaFixPath = settings.Get("CRAWL_NUQ_SCHEMA_FIX_PATH", "crawl-nuq-schema-fix-path"),
            CrawlRedisResourceName = settings.Get("CRAWL_REDIS_RESOURCE_NAME", "crawl-redis-resource-name"),
            CrawlRedisImage = settings.Image("CRAWL_REDIS_IMAGE", "crawl-redis-image"),
            CrawlRedisBindHost = settings.Get("CRAWL_REDIS_BIND_HOST", "crawl-redis-bind-host"),
            CrawlRedisPort = settings.Int("CRAWL_REDIS_PORT", "crawl-redis-port"),
            CrawlRabbitMqResourceName = settings.Get("CRAWL_RABBITMQ_RESOURCE_NAME", "crawl-rabbitmq-resource-name"),
            CrawlRabbitMqUser = settings.Get("CRAWL_RABBITMQ_USER", "crawl-rabbitmq-user"),
            CrawlRabbitMqPassword = settings.Get("CRAWL_RABBITMQ_PASSWORD", "crawl-rabbitmq-password"),
            CrawlRabbitMqPort = settings.Int("CRAWL_RABBITMQ_PORT", "crawl-rabbitmq-port"),
            PlaywrightResourceName = settings.Get("PLAYWRIGHT_RESOURCE_NAME", "playwright-resource-name"),
            PlaywrightImage = settings.Image("PLAYWRIGHT_IMAGE", "playwright-image"),
            PlaywrightPort = settings.Int("PLAYWRIGHT_PORT", "playwright-port"),
            PlaywrightDataVolume = settings.Get("PLAYWRIGHT_DATA_VOLUME", "playwright-data-volume"),
            PlaywrightDataPath = settings.Get("PLAYWRIGHT_DATA_PATH", "playwright-data-path"),
            SearxngResourceName = settings.Get("SEARXNG_RESOURCE_NAME", "searxng-resource-name"),
            SearxngImage = settings.Image("SEARXNG_IMAGE", "searxng-image"),
            SearxngBindHost = settings.Get("SEARXNG_BIND_HOST", "searxng-bind-host"),
            SearxngPort = settings.Int("SEARXNG_PORT", "searxng-port"),
            SearxngSecret = settings.Get("SEARXNG_SECRET", "searxng-secret"),
            SearxngSettingsPath = settings.Get("SEARXNG_SETTINGS_PATH", "searxng-settings-path"),
            SearxngCacheVolume = settings.Get("SEARXNG_CACHE_VOLUME", "searxng-cache-volume"),
            SearxngCachePath = settings.Get("SEARXNG_CACHE_PATH", "searxng-cache-path"),
            FirecrawlResourceName = settings.Get("FIRECRAWL_RESOURCE_NAME", "firecrawl-resource-name"),
            FirecrawlImage = settings.Image("FIRECRAWL_IMAGE", "firecrawl-image"),
            FirecrawlBindHost = settings.Get("FIRECRAWL_BIND_HOST", "firecrawl-bind-host"),
            FirecrawlPort = settings.Int("FIRECRAWL_PORT", "firecrawl-port"),
            FirecrawlUseDbAuthentication = settings.Bool("FIRECRAWL_USE_DB_AUTHENTICATION", "firecrawl-use-db-authentication"),
            FirecrawlBullAuthKey = settings.Get("FIRECRAWL_BULL_AUTH_KEY", "firecrawl-bull-auth-key"),
            FirecrawlBlockMedia = settings.Bool("FIRECRAWL_BLOCK_MEDIA", "firecrawl-block-media"),
            FirecrawlMaxCpu = settings.Get("FIRECRAWL_MAX_CPU", "firecrawl-max-cpu"),
            FirecrawlMaxRam = settings.Get("FIRECRAWL_MAX_RAM", "firecrawl-max-ram"),
            FirecrawlDataVolume = settings.Get("FIRECRAWL_DATA_VOLUME", "firecrawl-data-volume"),
            FirecrawlDataPath = settings.Get("FIRECRAWL_DATA_PATH", "firecrawl-data-path"),
            VllmResourceName = settings.Get("VLLM_RESOURCE_NAME", "vllm-resource-name"),
            VllmImage = settings.Image("VLLM_IMAGE", "vllm-image"),
            VllmPort = vllmPort,
            VllmHuggingFaceCacheVolume = settings.Get("VLLM_HUGGINGFACE_CACHE_VOLUME", "vllm-huggingface-cache-volume"),
            VllmHuggingFaceCachePath = settings.Get("VLLM_HUGGINGFACE_CACHE_PATH", "vllm-huggingface-cache-path"),
            VllmCompileCacheVolume = settings.Get("VLLM_COMPILE_CACHE_VOLUME", "vllm-compile-cache-volume"),
            VllmCompileCachePath = settings.Get("VLLM_COMPILE_CACHE_PATH", "vllm-compile-cache-path"),
            VllmArgs = CommandLine.Split(Expand(settings.Get("VLLM_ARGS", "vllm-args"), replacements)),
            VllmContainerRuntimeArgs = CommandLine.Split(settings.GetOptional("VLLM_CONTAINER_RUNTIME_ARGS", "vllm-container-runtime-args"))
        };
    }

    private static string Expand(string value, IReadOnlyDictionary<string, string> replacements)
    {
        foreach (var replacement in replacements)
        {
            value = value.Replace("{" + replacement.Key + "}", replacement.Value, StringComparison.OrdinalIgnoreCase);
        }

        return value;
    }
}

enum AppHostProfile
{
    Full,
    Light
}

enum AppExecutionMode
{
    Source,
    Images
}
