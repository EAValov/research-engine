using ResearchApi.Domain;
using ResearchApi.Infrastructure;
using ResearchApi.Application;
using Microsoft.EntityFrameworkCore;
using Serilog;
using ResearchApi.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using ResearchApi.Bootstrap;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Host.UseSerilog();

IConfigurationRoot config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
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

builder.Services.AddHttpClient();

// ---------- Options ----------
builder.Services.Configure<ChatConfig>(
    config.GetSection(nameof(ChatConfig)));
var chatConfig = config.GetSection(nameof(ChatConfig)).Get<ChatConfig>()!;

builder.Services.Configure<EmbeddingConfig>(
    config.GetSection(nameof(EmbeddingConfig)));
var embeddingConfig = config.GetSection(nameof(EmbeddingConfig)).Get<EmbeddingConfig>()!;

builder.Services.Configure<TokenizerConfig>(
    config.GetSection(nameof(TokenizerConfig)));

builder.Services.Configure<ResearchOrchestratorConfig>(
    config.GetSection(nameof(ResearchOrchestratorConfig)));

builder.Services
    .AddOptions<LearningSimilarityOptions>()
    .Bind(builder.Configuration.GetSection(nameof(LearningSimilarityOptions)))
    .ValidateDataAnnotations()
    .Validate(options => options.LocalMinImportance <= options.GlobalMinImportance,
        "LocalMinImportance must be <= GlobalMinImportance")
    .ValidateOnStart();

// ---------- Search & Crawl ----------
builder.Services.AddSearchAndCrawlClients(config);

// ---------- Core services ----------
builder.Services.AddSingleton<IChatModel, OpenAiChatModel>();
builder.Services.AddSingleton<IEmbeddingModel, OpenAiEmbeddingModel>();
builder.Services.AddSingleton<ITokenizer, VllmTokenizer>();

builder.Services.AddScoped<IResearchJobStore, PostgresResearchJobStore>();
builder.Services.AddScoped<IResearchOrchestrator, ResearchOrchestrator>();
builder.Services.AddScoped<ILearningEmbeddingService, LearningEmbeddingService>();
builder.Services.AddScoped<IResearchContentStore, ResearchContentStore>();
builder.Services.AddScoped<IQueryPlanningService, QueryPlanningService>();
builder.Services.AddScoped<ILearningExtractionService, LearningExtractionService>();
builder.Services.AddScoped<IReportSynthesisService, ReportSynthesisService>();

// ---------- Health checks ----------
var healthChecks = builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["ready"])
    .AddUrlGroup(new Uri($"{chatConfig.Endpoint.TrimEnd('/')}/models"), "chat", tags: ["ready", "llm", "chat"])
    .AddUrlGroup(new Uri($"{embeddingConfig.Endpoint.TrimEnd('/')}/models"), "embedding", tags: ["ready", "llm", "embedding"])
    .AddNpgSql(
        builder.Configuration.GetConnectionString("ResearchDb")!,
        name: "postgres",
        tags: ["ready", "db"]
    )
    .AddSearchAndCrawlHealthChecks(config);

var app = builder.Build();

app.MapHealthChecks("/health/live",
    new HealthCheckOptions { Predicate = check => check.Name == "self" });

app.MapHealthChecks("/health/ready",
    new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

// Map custom endpoints
app.MapDeepResearchModel();

// DB migration
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ResearchDbContext>();
    db.Database.Migrate();
}

app.Run();
