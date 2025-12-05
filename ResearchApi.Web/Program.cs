using ResearchApi.Domain;
using ResearchApi.Infrastructure;
using ResearchApi.Application;
using ResearchApi.Configuration;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Core;
using Serilog.Filters;

// Configure Serilog with all settings in code
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .Enrich.FromLogContext()
    .WriteTo.File(
        "logs/app.log",
        outputTemplate: "{Timestamp:dd/MM/yyyy HH:mm:ss ffff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
        rollingInterval: RollingInterval.Hour,
        retainedFileCountLimit: 30)
    .WriteTo.Logger(lc => lc
        .Filter.ByIncludingOnly(Matching.FromSource("ResearchApi"))
        .MinimumLevel.Information()
        .WriteTo.Console())
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

// Configure logging to use Serilog
builder.Logging.ClearProviders();
builder.Logging.AddSerilog(Log.Logger);

IConfigurationRoot config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddEnvironmentVariables()
    .Build();

builder.Services.AddDbContextFactory<ResearchDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("ResearchDb"),
        npgsql =>
        {
            npgsql.UseVector();
        });
});

builder.Services.AddTransient<HttpFileLoggingHandler>();
builder.Services.AddHttpClient("Default")
    .AddHttpMessageHandler<HttpFileLoggingHandler>();

builder.Services
    .AddHttpClient<FirecrawlClient>()
    .ConfigureHttpClient(c=> c.Timeout = TimeSpan.FromMinutes(5))
    .AddHttpMessageHandler<HttpFileLoggingHandler>();

builder.Services.Configure<FirecrawlOptions>(
    config.GetSection(nameof(FirecrawlOptions)));

builder.Services.Configure<LlmServiceConfig>(
    config.GetSection(nameof(LlmServiceConfig)));

// Add LLM chunking options
builder.Services.Configure<LlmChunkingOptions>(
    config.GetSection(nameof(LlmChunkingOptions)));

// Register services
builder.Services.AddScoped<ILlmService, LlmService>();
builder.Services.AddScoped<ISearchClient>(sp => sp.GetRequiredService<FirecrawlClient>());
builder.Services.AddScoped<ICrawlClient>(sp => sp.GetRequiredService<FirecrawlClient>());

builder.Services.AddScoped<IResearchJobStore, PostgresResearchJobStore>();
builder.Services.AddScoped<IResearchOrchestrator, ResearchOrchestrator>();
builder.Services.AddScoped<ILearningEmbeddingService, LearningEmbeddingService>();
builder.Services.AddScoped<IResearchContentStore, ResearchContentStore>();
builder.Services.AddScoped<IQueryPlanningService, QueryPlanningService>();
builder.Services.AddScoped<ILearningExtractionService, LearningExtractionService>();
builder.Services.AddScoped<IReportSynthesisService, ReportSynthesisService>();


var app = builder.Build();

// Map custom endpoints
app.MapHealthEndpoints();
app.MapDeepResearchModel();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ResearchDbContext>();
    db.Database.Migrate(); 
}

app.Run();
