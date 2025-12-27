using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ResearchApi.Application;
using ResearchApi.Configuration;
using ResearchApi.Domain;
using ResearchApi.Infrastructure;
using ResearchApi.Infrastructure.Authentication;
using Scalar.AspNetCore;
using Serilog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Host.UseSerilog();

// Keep your existing pattern, but prefer builder.Configuration where possible
IConfigurationRoot config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

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
builder.Services.Configure<ChatConfig>(config.GetSection(nameof(ChatConfig)));
var chatConfig = config.GetSection(nameof(ChatConfig)).Get<ChatConfig>()!;

builder.Services.Configure<EmbeddingConfig>(config.GetSection(nameof(EmbeddingConfig)));
var embeddingConfig = config.GetSection(nameof(EmbeddingConfig)).Get<EmbeddingConfig>()!;

builder.Services.Configure<ResearchOrchestratorConfig>(config.GetSection(nameof(ResearchOrchestratorConfig)));

builder.Services
    .AddOptions<LearningSimilarityOptions>()
    .Bind(builder.Configuration.GetSection(nameof(LearningSimilarityOptions)))
    .ValidateDataAnnotations()
    .Validate(options => options.LocalMinImportance <= options.GlobalMinImportance,
        "LocalMinImportance must be <= GlobalMinImportance")
    .ValidateOnStart();

// ---------- Validation (Minimal APIs, .NET 10) ----------
builder.Services.AddValidation();

// ---------- Authentication + Authorization ----------
builder.Services.AddAuthentication("Bearer")
    .AddScheme<BearerAuthenticationOptions, BearerAuthenticationHandler>("Bearer", options =>
    {
        builder.Configuration.GetSection(nameof(BearerAuthenticationOptions)).Bind(options);
    });

builder.Services.AddAuthorization();

// ---------- Redis ----------

builder.Services.AddOptions<RedisEventBusOptions>()
    .Bind(builder.Configuration.GetSection(nameof(RedisEventBusOptions)))
    .ValidateDataAnnotations()
    .ValidateOnStart();

var redisOptions = builder.Configuration.GetSection(nameof(RedisEventBusOptions))
    .Get<RedisEventBusOptions>();

// Redis multiplexer (singleton)
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisOptions!.ConnectionString));

// Event bus
builder.Services.AddSingleton<IResearchEventBus, RedisResearchEventBus>();

// ---------- Search & Crawl ----------
var firecrawlSection = config.GetSection(nameof(FirecrawlOptions));
var firecrawlOptions = firecrawlSection.Get<FirecrawlOptions>();

builder.Services.Configure<FirecrawlOptions>(firecrawlSection);

builder.Services
    .AddHttpClient<FirecrawlClient>()
    .ConfigureHttpClient(c =>
    {
        c.Timeout = TimeSpan.FromSeconds(firecrawlOptions!.HttpClientTimeoutSeconds);
        if (!string.IsNullOrWhiteSpace(firecrawlOptions.BaseUrl))
        {
            c.BaseAddress = new Uri(firecrawlOptions.BaseUrl);
        }
    });
    
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
    .AddUrlGroup(new Uri($"{chatConfig.Endpoint.TrimEnd('/')}/models"), "chat", tags: ["ready", "llm", "chat"])
    .AddUrlGroup(new Uri($"{embeddingConfig.Endpoint.TrimEnd('/')}/models"), "embedding", tags: ["ready", "llm", "embedding"])
    .AddNpgSql(
        builder.Configuration.GetConnectionString("ResearchDb")!,
        name: "postgres",
        tags: ["ready", "db"])
    .AddRedis(redisOptions!.ConnectionString, name: "redis", tags: ["ready", "cache"]);

builder.Services.AddOpenApi();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live",
    new HealthCheckOptions { Predicate = check => check.Name == "self" });

app.MapHealthChecks("/health/ready",
    new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

// Map custom endpoints
app.MapResearchModel();
app.MapResearchJobsApi();
app.MapResearchProtocolApi();

app.MapOpenApi();
app.MapScalarApiReference();

if(!builder.Environment.IsEnvironment("Testing"))
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ResearchDbContext>();
        db.Database.Migrate();
    }
}

app.Run();