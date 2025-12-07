using ResearchApi.Domain;
using ResearchApi.Infrastructure;
using ResearchApi.Application;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Filters;
using ResearchApi.Configuration;
using Serilog.Events;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

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

builder.Services.Configure<FirecrawlOptions>(
    config.GetSection(nameof(FirecrawlOptions)));

var firecrawlOptions = config.GetSection(nameof(FirecrawlOptions)).Get<FirecrawlOptions>()!;

builder.Services
    .AddHttpClient<FirecrawlClient>()
    .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(firecrawlOptions.HttpClientTimeoutSeconds));

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

// Register services
builder.Services.AddSingleton<IChatModel, OpenAiChatModel>();
builder.Services.AddSingleton<IEmbeddingModel, OpenAiEmbeddingModel>();
builder.Services.AddSingleton<ITokenizer, VllmTokenizer>();

builder.Services.AddScoped<ISearchClient>(sp => sp.GetRequiredService<FirecrawlClient>());
builder.Services.AddScoped<ICrawlClient>(sp => sp.GetRequiredService<FirecrawlClient>());

builder.Services.AddScoped<IResearchJobStore, PostgresResearchJobStore>();
builder.Services.AddScoped<IResearchOrchestrator, ResearchOrchestrator>();
builder.Services.AddScoped<ILearningEmbeddingService, LearningEmbeddingService>();
builder.Services.AddScoped<IResearchContentStore, ResearchContentStore>();
builder.Services.AddScoped<IQueryPlanningService, QueryPlanningService>();
builder.Services.AddScoped<ILearningExtractionService, LearningExtractionService>();
builder.Services.AddScoped<IReportSynthesisService, ReportSynthesisService>();


builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["ready"])
   // .AddUrlGroup(new Uri($"{firecrawlOptions.BaseUrl}/health"), "firecrawl", tags: ["ready", "search", "crawl"])
    .AddUrlGroup(new Uri($"{chatConfig.Endpoint}/models"), "chat", tags: ["ready", "llm", "chat"])
    .AddUrlGroup(new Uri($"{embeddingConfig.Endpoint}/models"), "embedding", tags: ["ready", "llm", "embedding"])
    .AddNpgSql(
        builder.Configuration.GetConnectionString("ResearchDb")!,
        name: "postgres",
        tags: ["ready", "db"]
    );

var app = builder.Build();

app.MapHealthChecks("/health/live", new HealthCheckOptions {Predicate = check => check.Name == "self"}); // only the app
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready")}); // everything else

// Map custom endpoints
app.MapDeepResearchModel();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ResearchDbContext>();
    db.Database.Migrate(); 
}

app.Run();
