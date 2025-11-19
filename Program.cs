using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

using ResearchApi.Domain;
using ResearchApi.Infrastructure;
using ResearchApi.Application;
using ResearchApi.Endpoints;
using ResearchApi.Configuration;


var builder = WebApplication.CreateBuilder(args);

IConfigurationRoot config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddEnvironmentVariables()
    .Build();

builder.Services.Configure<FirecrawlOptions>(
    config.GetSection("FirecrawlOptions"));

builder.Services.PostConfigure<FirecrawlOptions>(options =>
{
    var baseUrlFromEnv = config["FIRECRAWL_BASE_URL"];

    if (string.IsNullOrWhiteSpace(baseUrlFromEnv))
    {
        Console.WriteLine($"Firecrawl base url is not configured in env variables (FIRECRAWL_BASE_URL is empty). Used default value {options.BaseUrl}");
    } 
    else 
    {
        options.BaseUrl = baseUrlFromEnv;
    }
});   

builder.Services.Configure<LlmOptions>(
    config.GetSection("LlmOptions"));

builder.Services.PostConfigure<LlmOptions>(options =>
{
    var llm_endpoint = config["LLM_ENDPOINT"];

    if (string.IsNullOrWhiteSpace(llm_endpoint))
    {
        Console.WriteLine($"LLM api endpoint is not configured in env variables (LLM_ENDPOINT is empty). Used default value {options.Endpoint}");
    } 
    else 
    {
        options.Endpoint = llm_endpoint;
    }

    var llm_model = config["LLM_MODEL"];

    if (string.IsNullOrWhiteSpace(llm_model))
    {
        Console.WriteLine($"LLM model is not configured in env variables (LLM_MODEL is empty). Used default value {options.Model}");
    } 
    else 
    {
        options.Model = llm_model;
    }
});       

// Add services to the container.
builder.Services.AddOpenApi();

// Register services
builder.Services.AddSingleton<ILlmClient, MicrosoftAiLlmClient>();
builder.Services.AddSingleton<ISearchClient, FirecrawlSearchClient>();
builder.Services.AddSingleton<IResearchJobStore, InMemoryResearchJobStore>();
builder.Services.AddSingleton<IResearchOrchestrator, ResearchOrchestrator>();

// Register HttpClient for Firecrawl
builder.Services.AddHttpClient();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Map custom endpoints
app.MapResearchEndpoints();
app.MapHealthEndpoints();
app.MapDeepResearchModel();

app.Run();
