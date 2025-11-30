using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using ResearchApi.Configuration;
using ResearchApi.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using ResearchApi.Domain; // <- adjust namespace

namespace ResearchApi.IntegrationTests;

public sealed class LlmIntegrationFixture : IAsyncLifetime, IDisposable
{
    public IServiceProvider Services { get; private set; } = default!;

    public ILlmService LlmService { get; private set; } = null!;
    public IOptions<LlmChunkingOptions> ChunkingOptions { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();

        // Логирование (можно ослабить уровень при желании)
        services.AddLogging(b =>
        {
            b.AddConsole();
            b.SetMinimumLevel(LogLevel.Information);
        });

        // ---------- LLM конфиг ----------
        var cfg = new LlmServiceConfig
        {
            ChatEndpoint  = "https://api.llm.local:8443/v1",
            ChatApiKey    = "EMPTY",
            ChatModelId   = "Qwen/Qwen3-32B-AWQ",

            EmbeddingEndpoint  = "https://embedding.llm.local:8443/v1",
            EmbeddingApiKey    = "EMPTY",
            EmbeddingModelId   = "Qwen/Qwen3-Embedding-0.6B",

            IgnoreServerCertificateErrors = true
        };

        services.Configure<LlmChunkingOptions>(options =>
        {
            // keep well below your model max context
            options.MaxPromptTokens = 16000;
        });

        services.AddSingleton(Options.Create(cfg));
        services.AddHttpClient();
        services.AddScoped<ILlmService, LlmService>();

        // ---------- Postgres + pgvector ----------
        var connectionString =
            Environment.GetEnvironmentVariable("RESEARCH_TEST_DB")
            ?? "Host=localhost;Port=5432;Database=research_rag;Username=firecrawl;Password=firecrawl";

        services.AddDbContextFactory<ResearchDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.UseVector();
            });
        });

        // ---------- Domain services ----------
        services.AddScoped<ILearningEmbeddingService, LearningEmbeddingService>();
        services.AddScoped<IResearchContentStore, ResearchContentStore>();
        services.AddScoped<IReportSynthesisService, ReportSynthesisService>();

        Services = services.BuildServiceProvider();

        LlmService      = Services.GetRequiredService<ILlmService>();
        ChunkingOptions = Services.GetRequiredService<IOptions<LlmChunkingOptions>>();

        // Прогоняем миграции и чистим БД
        using var scope = Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ResearchDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Database.MigrateAsync();

        await CleanupDatabaseAsync();
    }

    public async Task CleanupDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ResearchDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        // Таблицы и имена - подправь под свою реальную схему, если отличаются
        // Порядок важен из-за FK, поэтому TRUNCATE ... CASCADE
        await db.Database.ExecuteSqlRawAsync(@"
            TRUNCATE TABLE learnings, scraped_pages, research_events, visited_urls, research_jobs 
            RESTART IDENTITY CASCADE;
        ");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        if (Services is IDisposable d)
            d.Dispose();
    }
}

// Коллекция для шаринга фикстуры по нескольким тест-классам
[CollectionDefinition("LlmIntegration")]
public class LlmIntegrationCollection : ICollectionFixture<LlmIntegrationFixture>
{
}

