using ResearchApi.Domain;
using ResearchApi.Infrastructure;
using ResearchApi.Application;
using ResearchApi.Configuration;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddFilter<Microsoft.Extensions.Logging.Console.ConsoleLoggerProvider>(
    category: null,
    level: LogLevel.Information);

builder.Logging.AddFile("logs/app-{Date}.log", LogLevel.Debug);

IConfigurationRoot config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddEnvironmentVariables()
    .Build();

builder.Services.AddDbContext<ResearchDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("ResearchDb"),
        npgsql =>
        {
            npgsql.UseVector(); 
        });
});

// Register HttpClient for Firecrawl
builder.Services.AddHttpClient();

builder.Services.Configure<FirecrawlOptions>(
    config.GetSection(nameof(FirecrawlOptions)));

builder.Services.Configure<LlmServiceConfig>(
    config.GetSection(nameof(LlmServiceConfig)));

// Add LLM chunking options
builder.Services.Configure<LlmChunkingOptions>(
    config.GetSection(nameof(LlmChunkingOptions)));

// Register services
builder.Services.AddScoped<ILlmService, LlmService>();
builder.Services.AddScoped<ISearchClient, FirecrawlClient>();
builder.Services.AddScoped<ICrawlClient, FirecrawlClient>();

builder.Services.AddScoped<IResearchJobStore, PostgresResearchJobStore>();
builder.Services.AddScoped<IResearchOrchestrator, ResearchOrchestrator>();
builder.Services.AddScoped<ILearningEmbeddingService, LearningEmbeddingService>();
builder.Services.AddScoped<IResearchContentStore, ResearchContentStore>();

var app = builder.Build();

// Map custom endpoints
app.MapHealthEndpoints();
app.MapDeepResearchModel();

app.Run();
