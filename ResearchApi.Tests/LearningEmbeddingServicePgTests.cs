using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ResearchApi.Domain;
using ResearchApi.Infrastructure;
using ResearchApi.Application;
using Xunit;

namespace ResearchApi.IntegrationTests;

[Collection("LlmIntegration")]
public class LearningEmbeddingServicePgTests
{
    private readonly LlmIntegrationFixture _fixture;

    public LearningEmbeddingServicePgTests(LlmIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    private ILearningEmbeddingService CreateService()
    {
        var scope = _fixture.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ILearningEmbeddingService>();
    }

    [Fact]
    public async Task PopulateEmbeddingsAsync_Sets_Embeddings_For_Learnings_Without_Embedding()
    {
        await _fixture.CleanupDatabaseAsync();

        var service = CreateService();

        var learnings = new List<Learning>
        {
            new()
            {
                Id        = Guid.NewGuid(),
                JobId     = Guid.NewGuid(),
                PageId    = Guid.NewGuid(),
                QueryHash = "hash1",
                Text      = "Erste wichtige Erkenntnis über KI-Anwendungen.",
                SourceUrl = "https://example.com/1",
                CreatedAt = DateTimeOffset.UtcNow
            },
            new()
            {
                Id        = Guid.NewGuid(),
                JobId     = Guid.NewGuid(),
                PageId    = Guid.NewGuid(),
                QueryHash = "hash2",
                Text      = "Zweite Erkenntnis zur Automatisierung in der Industrie.",
                SourceUrl = "https://example.com/2",
                CreatedAt = DateTimeOffset.UtcNow
            }
        };

        // Act
        var result = await service.PopulateEmbeddingsAsync(learnings, CancellationToken.None);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, l => Assert.NotNull(l.Embedding));
    }

    [Fact]
    public async Task PopulateEmbeddingsAsync_Returns_Empty_When_All_Already_Embedded()
    {
        await _fixture.CleanupDatabaseAsync();

        var service = CreateService();

        // здесь мы НЕ сохраняем в БД, просто проверяем поведение метода
        var learnings = new List<Learning>
        {
            new()
            {
                Id        = Guid.NewGuid(),
                JobId     = Guid.NewGuid(),
                PageId    = Guid.NewGuid(),
                QueryHash = "hash",
                Text      = "Some text",
                SourceUrl = "https://example.com/embedded",
                CreatedAt = DateTimeOffset.UtcNow,
                // любая заглушка для Embedding, чтобы пройти условие (в БД не отправляем)
                Embedding = new Pgvector.Vector(new float[] { 0.1f, 0.2f, 0.3f })
            }
        };

        // Act
        var result = await service.PopulateEmbeddingsAsync(learnings, CancellationToken.None);

        // Assert: метод не должен ничего перерасчитывать и возвращает пустой список
        Assert.Empty(result);
        Assert.NotNull(learnings[0].Embedding);
    }

    [Fact]
    public async Task GetSimilarLearningsAsync_Returns_Empty_For_Empty_Query()
    {
        await _fixture.CleanupDatabaseAsync();

        var service = CreateService();

        var result = await service.GetSimilarLearningsAsync(
            queryText: "",
            jobId: null,
            queryHash: null,
            language: null,
            region: null,
            topK: 10,
            ct: CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSimilarLearningsAsync_Applies_Filters_And_Returns_Results()
    {
        await _fixture.CleanupDatabaseAsync();

        var scope     = _fixture.Services.CreateScope();
        var provider  = scope.ServiceProvider;
        var llm       = provider.GetRequiredService<ILlmService>();
        var dbFactory = provider.GetRequiredService<IDbContextFactory<ResearchDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var logger    = provider.GetService<ILogger<LearningEmbeddingService>>() 
                        ?? NullLogger<LearningEmbeddingService>.Instance;

        var service = CreateService();

        // --- Arrange: подготовим данные в реальной БД ---

        var jobId1 = Guid.NewGuid();
        var job1 = new ResearchJob
        {
            Id             = jobId1,
            Query          = "Testjob 1",
            Breadth        = 2,
            Depth          = 2,
            Status         = ResearchJobStatus.Running,
            TargetLanguage = "de",
            Region         = "DE",
            CreatedAt      = DateTimeOffset.UtcNow,
            UpdatedAt      = DateTimeOffset.UtcNow
        };

        var jobId2 = Guid.NewGuid();
        var job2 = new ResearchJob
        {
            Id             = jobId2,
            Query          = "Testjob 2",
            Breadth        = 1,
            Depth          = 1,
            Status         = ResearchJobStatus.Running,
            TargetLanguage = "de",
            Region         = "DE",
            CreatedAt      = DateTimeOffset.UtcNow,
            UpdatedAt      = DateTimeOffset.UtcNow
        };

        db.ResearchJobs.AddRange(job1, job2);

        var page1 = new ScrapedPage
        {
            Id          = Guid.NewGuid(),
            Url         = "https://example.com/page1",
            Language    = "de",
            Region      = "DE",
            Content     = "Inhalt Seite 1",
            ContentHash = "hash1",
            CreatedAt   = DateTimeOffset.UtcNow
        };
        var page2 = new ScrapedPage
        {
            Id          = Guid.NewGuid(),
            Url         = "https://example.com/page2",
            Language    = "de",
            Region      = "DE",
            Content     = "Inhalt Seite 2",
            ContentHash = "hash2",
            CreatedAt   = DateTimeOffset.UtcNow
        };
        var page3 = new ScrapedPage
        {
            Id          = Guid.NewGuid(),
            Url         = "https://example.com/page3",
            Language    = "en",
            Region      = "US",
            Content     = "Content page 3",
            ContentHash = "hash3",
            CreatedAt   = DateTimeOffset.UtcNow
        };

        db.ScrapedPages.AddRange(page1, page2, page3);
        await db.SaveChangesAsync();

        var hashCommon = "common-hash";

        var learning1 = new Learning
        {
            Id        = Guid.NewGuid(),
            JobId     = jobId1,
            PageId    = page1.Id,
            QueryHash = hashCommon,
            Text      = "Erkenntnis über KI in der bayerischen Industrie.",
            SourceUrl = page1.Url,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var learning2 = new Learning
        {
            Id        = Guid.NewGuid(),
            JobId     = jobId1,
            PageId    = page2.Id,
            QueryHash = hashCommon,
            Text      = "Weitere Erkenntnis über Automatisierung im Maschinenbau.",
            SourceUrl = page2.Url,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var learning3 = new Learning
        {
            Id        = Guid.NewGuid(),
            JobId     = jobId2,
            PageId    = page3.Id,
            QueryHash = "other-hash",
            Text      = "Some English learning about AI.",
            SourceUrl = page3.Url,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.Learnings.AddRange(learning1, learning2, learning3);
        await db.SaveChangesAsync();

        // Проставляем эмбеддинги для всех трёх learnings через тот же сервис
        await service.PopulateEmbeddingsAsync(new[] { learning1, learning2, learning3 }, CancellationToken.None);
        await db.SaveChangesAsync();

        // --- Act: дергаем GetSimilarLearningsAsync с фильтрами ---
        var result = await service.GetSimilarLearningsAsync(
            queryText: "KI und Automatisierung in Bayern",
            jobId: jobId1,
            queryHash: hashCommon,
            language: "de",
            region: "DE",
            topK: 10, // больше, чем количество кандидатов, чтобы не было отсечения по Take()
            ct: CancellationToken.None);

        // --- Assert ---
        // Должны попасть только learning1 и learning2 (оба jobId1, hashCommon, de/DE)
        Assert.NotEmpty(result);
        Assert.Equal(2, result.Count);

        var ids = result.Select(l => l.Id).ToHashSet();
        Assert.Contains(learning1.Id, ids);
        Assert.Contains(learning2.Id, ids);
        Assert.DoesNotContain(learning3.Id, ids);
    }
}
