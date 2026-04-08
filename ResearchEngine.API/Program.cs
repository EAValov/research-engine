using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ResearchEngine.Application;
using ResearchEngine.Configuration;
using ResearchEngine.Domain;
using ResearchEngine.Infrastructure;
using ResearchEngine.API;
using ResearchEngine.API.Authentication;
using Scalar.AspNetCore;
using Serilog;
using AspNetCoreRateLimit;
using StackExchange.Redis;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);
var runtimeSettingsBootstrap = RuntimeSettingsBootstrap.LoadValidated(builder.Configuration);

// ---------- Logging ----------
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Host.UseSerilog();

var researchDb = builder.Configuration.GetConnectionString("ResearchDb")
    ?? throw new InvalidOperationException("Missing connection string: ResearchDb");

// Prefer explicit HangfireDb, fallback to researchDb if not provided
var hangfireDb = builder.Configuration.GetConnectionString("HangfireDb") ?? researchDb;

// ---------- DB ----------
builder.Services.AddDbContextFactory<ResearchDbContext>(options =>
{
    options.UseNpgsql(
        researchDb,
        npgsql =>
        {
            npgsql.UseVector();
            npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
        });
});

// ---- Hangfire ----
builder.Services.AddHangfire((sp, cfg) =>
{
    var storageOptions = new PostgreSqlStorageOptions
    {
        PrepareSchemaIfNecessary = !builder.Environment.IsEnvironment("Testing"), // don't do it for testing
        SchemaName = "hangfire"
    };

    var pollMs = builder.Configuration.GetValue<int?>("Hangfire:QueuePollMs");
    if(pollMs.HasValue)
        storageOptions.QueuePollInterval = TimeSpan.FromMilliseconds(pollMs.Value);

    cfg.UsePostgreSqlStorage(
        opts => opts.UseNpgsqlConnection(hangfireDb),
        storageOptions);

    if (builder.Environment.IsEnvironment("Testing"))
    {
        cfg.UseFilter(new AutomaticRetryAttribute { Attempts = 0 }); // no retries in tests
    }
});

var enableHangfireServer = builder.Configuration.GetValue("Hangfire:EnableServer", true); // set to false if only web api is needed

if (enableHangfireServer && !builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHangfireServer(options =>
    {
        options.WorkerCount = builder.Configuration.GetValue("Hangfire:WorkerCount", 2);
        options.Queues = ["jobs", "synthesis"];
        options.ServerName = $"api-{Environment.MachineName}";
    });
}

// ---------- Http ----------
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();

builder.Services
    .AddOptions<ReleaseCheckOptions>()
    .Bind(builder.Configuration.GetSection(nameof(ReleaseCheckOptions)));

builder.Services.AddHttpClient(GitHubReleaseUpdateService.HttpClientName, client =>
{
    client.BaseAddress = new Uri("https://api.github.com/");
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("ResearchEngine-UpdateCheck");
});

builder.Services.AddSingleton<IReleaseUpdateService, GitHubReleaseUpdateService>();

// ---------- Options ----------
builder.Services
    .AddOptions<EmbeddingConfig>()
    .Bind(builder.Configuration.GetSection(nameof(EmbeddingConfig)))
    .Validate(
        config => !string.IsNullOrWhiteSpace(config.ModelId),
        $"{nameof(EmbeddingConfig.ModelId)} must be configured.")
    .ValidateOnStart();

// ---------- Validation (Minimal APIs) ----------
builder.Services.AddValidation();

// ---------- Authentication + Authorization ----------
builder.Services.AddAuthentication("Bearer")
    .AddScheme<AuthenticationOptions, ApiKeyAuthenticationHandler>(
        "Bearer",
        options =>
        {
            builder.Configuration
                .GetSection(nameof(AuthenticationOptions))
                .Bind(options);
        });

builder.Services.AddAuthorization();

// ---------- Redis ----------
builder.Services
    .AddOptions<RedisEventBusOptions>()
    .Bind(builder.Configuration.GetSection(nameof(RedisEventBusOptions)))
    .ValidateDataAnnotations()
    .ValidateOnStart();

var redisOptions = builder.Configuration
    .GetSection(nameof(RedisEventBusOptions))
    .Get<RedisEventBusOptions>()
    ?? throw new InvalidOperationException("RedisEventBusOptions not configured");

// Redis multiplexer (singleton)
builder.Services.AddSingleton<IConnectionMultiplexer>(
    _ => ConnectionMultiplexer.Connect(redisOptions.ConnectionString));

// Event bus
builder.Services.AddSingleton<IResearchEventBus, RedisResearchEventBus>();

var ipRateLimitingSection = builder.Configuration.GetSection("IpRateLimiting");
var ipRateLimitingEnabled =
    builder.Configuration.GetValue<bool>("IpRateLimiting:Enabled") &&
    ipRateLimitingSection.Exists() &&
    ipRateLimitingSection.GetSection("GeneralRules").GetChildren().Any();

if (ipRateLimitingEnabled)
{   
    builder.Services.AddOptions();

    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisOptions.ConnectionString;
        options.InstanceName = "ratelimit:";
    });

    builder.Services.Configure<IpRateLimitOptions>(ipRateLimitingSection);
    builder.Services.Configure<IpRateLimitPolicies>(
        builder.Configuration.GetSection("IpRateLimitPolicies"));

    builder.Services.AddSingleton<IProcessingStrategy, AsyncKeyLockProcessingStrategy>();

    builder.Services.AddSingleton<IIpPolicyStore, DistributedCacheIpPolicyStore>();
    builder.Services.AddSingleton<IRateLimitCounterStore, DistributedCacheRateLimitCounterStore>();
    builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
}

// ---------- Search & Crawl ----------
builder.Services.Configure<FirecrawlOptions>(
    builder.Configuration.GetSection(nameof(FirecrawlOptions)));

var firecrawlOptions = builder.Configuration
    .GetSection(nameof(FirecrawlOptions))
    .Get<FirecrawlOptions>();

builder.Services
    .AddHttpClient<FirecrawlClient>()
    .ConfigureHttpClient(c =>
    {
        c.Timeout = TimeSpan.FromSeconds(
            firecrawlOptions?.HttpClientTimeoutSeconds ?? 30);

        if (!string.IsNullOrWhiteSpace(firecrawlOptions?.BaseUrl))
            c.BaseAddress = new Uri(firecrawlOptions.BaseUrl);
    });

builder.Services.AddScoped<ISearchClient, FirecrawlClient>();
builder.Services.AddScoped<ICrawlClient, FirecrawlClient>();

// ---------- Core services ----------
builder.Services.AddSingleton(runtimeSettingsBootstrap);
builder.Services.AddSingleton<IRuntimeSettingsRepository, PostgresRuntimeSettingsRepository>();
builder.Services.AddScoped<IRuntimeSettingsAccessor, ScopedRuntimeSettingsAccessor>();

builder.Services.AddScoped<IChatModel, OpenAiChatModel>();
builder.Services.AddSingleton<IEmbeddingModel, OpenAiEmbeddingModel>();
builder.Services.AddScoped<ITokenizer, VllmTokenizer>();

builder.Services.AddScoped<IResearchOrchestrator, ResearchOrchestrator>();
builder.Services.AddScoped<IResearchProtocolService, ResearchProtocolService>();
builder.Services.AddScoped<ILearningIntelService, LearningIntelService>();
builder.Services.AddScoped<IQueryPlanningService, QueryPlanningService>();
builder.Services.AddScoped<IReportSynthesisService, ReportSynthesisService>();
builder.Services.AddSingleton<ISourceReliabilityEvaluator, SourceReliabilityEvaluator>();
builder.Services.AddSingleton<IJobSseTicketService, JobSseTicketService>();

builder.Services.AddScoped<IResearchJobStore, PostgresResearchJobStore>();
builder.Services.AddScoped<IResearchJobRepository>(sp => sp.GetRequiredService<IResearchJobStore>());
builder.Services.AddScoped<IResearchEventRepository>(sp => sp.GetRequiredService<IResearchJobStore>());
builder.Services.AddScoped<IResearchSourceRepository>(sp => sp.GetRequiredService<IResearchJobStore>());
builder.Services.AddScoped<IResearchLearningRepository>(sp => sp.GetRequiredService<IResearchJobStore>());
builder.Services.AddScoped<IResearchLearningGroupRepository>(sp => sp.GetRequiredService<IResearchJobStore>());
builder.Services.AddScoped<IResearchSynthesisRepository>(sp => sp.GetRequiredService<IResearchJobStore>());
builder.Services.AddScoped<IResearchSynthesisOverridesRepository>(sp => sp.GetRequiredService<IResearchJobStore>());

// ---------- Health checks ----------
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["ready"])
    .AddCheck<ChatBackendHealthCheck>(
        "chat",
        tags: ["ready", "llm", "chat"])
    .AddUrlGroup(
        new Uri($"{builder.Configuration["EmbeddingConfig:Endpoint"]!.TrimEnd('/')}/models"),
        "embedding",
        tags: ["ready", "llm", "embedding"])
    .AddNpgSql(
        builder.Configuration.GetConnectionString("ResearchDb")!,
        name: "postgres",
        tags: ["ready", "db"])
    .AddRedis(
        redisOptions.ConnectionString,
        name: "redis",
        tags: ["ready", "event"]);

// ---------- OpenAPI ----------
builder.Services.AddOpenApi();

builder.Services.AddDataProtection();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddCors(options =>
{
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

    options.AddPolicy("WebUIDev", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins);
        }

        policy
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

if (ipRateLimitingEnabled)
{   
    // if we're behind the reverse-proxy
    var options = new ForwardedHeadersOptions {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    };

    options.KnownProxies.Clear();
    options.KnownIPNetworks.Clear();
    
    app.UseForwardedHeaders(options);
    app.UseIpRateLimiting();
}

app.UseResearchEngineRequestLogging();

app.UseCors("WebUIDev");  
app.UseAuthentication();
app.UseAuthorization();

// ---------- Health endpoints ----------
app.MapVersionControlApi();

app.MapHealthChecks("/health/live",
    new HealthCheckOptions { Predicate = check => check.Name == "self" });

app.MapHealthChecks("/health/ready",
    new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

// ---------- API ----------
app.MapResearchApi();
app.MapResearchProtocolApi();

app.MapOpenApi();
app.MapOpenApi("/openapi/{documentName}.yaml");
app.MapScalarApiReference();
app.UseHangfireDashboard("/hangfire");

// ---------- Migrations (prod only) ----------
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ResearchDbContext>();
    db.Database.Migrate();
    var runtimeSettingsRepository = scope.ServiceProvider.GetRequiredService<IRuntimeSettingsRepository>();
    await runtimeSettingsRepository.EnsureInitializedAsync();
}

app.Run();
