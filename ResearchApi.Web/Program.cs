using System;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ResearchApi.Application;
using ResearchApi.Bootstrap;
using ResearchApi.Configuration;
using ResearchApi.Domain;
using ResearchApi.Infrastructure;
using ResearchApi.Infrastructure.Authentication;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Host.UseSerilog();

// Prefer using builder.Configuration everywhere; keep the root config for the existing pattern
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

builder.Services.Configure<TokenizerConfig>(config.GetSection(nameof(TokenizerConfig)));

builder.Services.Configure<ResearchOrchestratorConfig>(config.GetSection(nameof(ResearchOrchestratorConfig)));

// ---------- Validation ----------
builder.Services.AddValidation();

builder.Services
    .AddOptions<LearningSimilarityOptions>()
    .Bind(builder.Configuration.GetSection(nameof(LearningSimilarityOptions)))
    .ValidateDataAnnotations()
    .Validate(options => options.LocalMinImportance <= options.GlobalMinImportance,
        "LocalMinImportance must be <= GlobalMinImportance")
    .ValidateOnStart();

// ---------- Authentication ----------
builder.Services.AddAuthentication("Bearer")
    .AddScheme<BearerAuthenticationOptions, BearerAuthenticationHandler>("Bearer", options =>
    {
        builder.Configuration.GetSection("Auth").Bind(options);
    });

// ---------- Optional Webhooks + Event Streaming ----------
var webhooksEnabled = builder.Services.AddOptionalWebhooks(builder.Configuration);

// Configure IResearchJobStore as Postgres store, and decorate with PublishingResearchJobStore if webhooks are enabled
builder.Services.ConfigureJobStore(webhooksEnabled);

// ---------- Search & Crawl ----------
builder.Services.AddSearchAndCrawlClients(config);

// ---------- Core services ----------
builder.Services.AddSingleton<IChatModel, OpenAiChatModel>();
builder.Services.AddSingleton<IEmbeddingModel, OpenAiEmbeddingModel>();
builder.Services.AddSingleton<ITokenizer, VllmTokenizer>();

builder.Services.AddScoped<IResearchOrchestrator, ResearchOrchestrator>();
builder.Services.AddScoped<IResearchProtocolService, ResearchProtocolService>();
builder.Services.AddScoped<ILearningEmbeddingService, LearningEmbeddingService>();
builder.Services.AddScoped<IResearchContentStore, ResearchContentStore>();
builder.Services.AddScoped<IQueryPlanningService, QueryPlanningService>();
builder.Services.AddScoped<ILearningExtractionService, LearningExtractionService>();
builder.Services.AddScoped<IReportSynthesisService, ReportSynthesisService>();

// ---------- Health checks ----------
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "ready" })
    .AddUrlGroup(new Uri($"{chatConfig.Endpoint.TrimEnd('/')}/models"), "chat", tags: new[] { "ready", "llm", "chat" })
    .AddUrlGroup(new Uri($"{embeddingConfig.Endpoint.TrimEnd('/')}/models"), "embedding", tags: new[] { "ready", "llm", "embedding" })
    .AddNpgSql(
        builder.Configuration.GetConnectionString("ResearchDb")!,
        name: "postgres",
        tags: new[] { "ready", "db" })
    .AddSearchAndCrawlHealthChecks(config);

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live",
    new HealthCheckOptions { Predicate = check => check.Name == "self" });

app.MapHealthChecks("/health/ready",
    new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

// Map custom endpoints
app.MapDeepResearchModel();
app.MapDeepResearchJobsApi();
app.MapDeepResearchProtocolApi();

// DB migration
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ResearchDbContext>();
    db.Database.Migrate();
}

app.Run();
