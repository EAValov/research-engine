using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ResearchEngine.Application;
using ResearchEngine.Configuration;
using ResearchEngine.Domain;
using ResearchEngine.Infrastructure;
using ResearchEngine.Web;
using ResearchEngine.Web.Authentication;
using ResearchEngine.Web.OpenAI;
using Scalar.AspNetCore;
using Serilog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ---------- Logging ----------
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Host.UseSerilog();

// ---------- DB ----------
builder.Services.AddDbContextFactory<ResearchDbContext>(options =>
{
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("ResearchDb"),
        npgsql =>
        {
            npgsql.UseVector();
            npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
        });
});

// ---------- Http ----------
builder.Services.AddHttpClient();

// ---------- Options ----------
builder.Services.Configure<ChatConfig>(
    builder.Configuration.GetSection(nameof(ChatConfig)));

builder.Services.Configure<EmbeddingConfig>(
    builder.Configuration.GetSection(nameof(EmbeddingConfig)));

builder.Services.Configure<ResearchOrchestratorConfig>(
    builder.Configuration.GetSection(nameof(ResearchOrchestratorConfig)));

builder.Services
    .AddOptions<LearningSimilarityOptions>()
    .Bind(builder.Configuration.GetSection(nameof(LearningSimilarityOptions)))
    .ValidateDataAnnotations()
    .Validate(
        options => options.LocalMinImportance <= options.GlobalMinImportance,
        "LocalMinImportance must be <= GlobalMinImportance")
    .ValidateOnStart();

// ---------- Validation (Minimal APIs) ----------
builder.Services.AddValidation();

// ---------- Authentication + Authorization ----------
builder.Services.AddAuthentication("Bearer")
    .AddScheme<BearerAuthenticationOptions, BearerAuthenticationHandler>(
        "Bearer",
        options =>
        {
            builder.Configuration
                .GetSection(nameof(BearerAuthenticationOptions))
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
builder.Services.AddSingleton<IChatModel, OpenAiChatModel>();
builder.Services.AddSingleton<IEmbeddingModel, OpenAiEmbeddingModel>();
builder.Services.AddSingleton<ITokenizer, VllmTokenizer>();

builder.Services.AddScoped<IResearchOrchestrator, ResearchOrchestrator>();
builder.Services.AddScoped<IResearchProtocolService, ResearchProtocolService>();
builder.Services.AddScoped<ILearningIntelService, LearningIntelService>();
builder.Services.AddScoped<IQueryPlanningService, QueryPlanningService>();
builder.Services.AddScoped<IReportSynthesisService, ReportSynthesisService>();

builder.Services.AddScoped<IResearchJobStore, PostgresResearchJobStore>();

// ---------- Health checks ----------
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["ready"])
    .AddUrlGroup(
        new Uri($"{builder.Configuration["ChatConfig:Endpoint"]!.TrimEnd('/')}/models"),
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

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// ---------- Health endpoints ----------
app.MapHealthChecks("/health/live",
    new HealthCheckOptions { Predicate = check => check.Name == "self" });

app.MapHealthChecks("/health/ready",
    new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

// ---------- API ----------
app.MapResearchModel();
app.MapResearchJobsApi();
app.MapResearchProtocolApi();

app.MapOpenApi();
app.MapScalarApiReference();

// ---------- Migrations (prod only) ----------
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ResearchDbContext>();
    db.Database.Migrate();
}

app.Run();