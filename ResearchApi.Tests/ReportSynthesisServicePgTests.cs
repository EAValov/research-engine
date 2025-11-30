using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResearchApi.Domain;
using ResearchApi.Application;
using ResearchApi.Infrastructure;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;

namespace ResearchApi.IntegrationTests;

[Collection("LlmIntegration")]
public class ReportSynthesisServicePgTests
{
    private readonly LlmIntegrationFixture _fixture;

    public ReportSynthesisServicePgTests(LlmIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task WriteFinalReportAsync_Uses_Learnings_And_Produces_Sources()
    {
        await _fixture.CleanupDatabaseAsync();

        using var scope = _fixture.Services.CreateScope();
        var embeddingService = scope.ServiceProvider.GetRequiredService<ILearningEmbeddingService>();
        var synthesisService = scope.ServiceProvider.GetRequiredService<IReportSynthesisService>();
        var contentStore     = scope.ServiceProvider.GetRequiredService<IResearchContentStore>();
        var dbFactory        = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ResearchDbContext>>();
        await using var db   = await dbFactory.CreateDbContextAsync();

        // --- Job ---
        var jobId = Guid.NewGuid();
        var job = new ResearchJob
        {
            Id             = jobId,
            Query          = "Wie entwickelt sich der Markt für KI-gestützte Produktionsautomatisierung in Bayern bis 2030?",
            Breadth        = 2,
            Depth          = 2,
            Status         = ResearchJobStatus.Running,
            TargetLanguage = "de",
            Region         = "DE",
            CreatedAt      = DateTimeOffset.UtcNow,
            UpdatedAt      = DateTimeOffset.UtcNow,
            VisitedUrls    = new List<VisitedUrl>
            {
                new() { Url = "https://www.bayern-innovativ.de/de/seite/ki-produktionsnetzwerk" },
                new() { Url = "https://www.bayern.de/politik/hightech-agenda/" }
            }
        };

        db.ResearchJobs.Add(job);
        await db.SaveChangesAsync();

        // --- Pages через ResearchContentStore ---
        var page1 = await contentStore.UpsertScrapedPageAsync(
            url: "https://www.bayern-innovativ.de/de/seite/ki-produktionsnetzwerk",
            content: "Markdown content über KI-Produktionsnetzwerk in Bayern...",
            language: "de",
            region: "DE",
            ct: CancellationToken.None);

        var page2 = await contentStore.UpsertScrapedPageAsync(
            url: "https://www.bayern.de/politik/hightech-agenda/",
            content: "Markdown content über Hightech Agenda Bayern...",
            language: "de",
            region: "DE",
            ct: CancellationToken.None);

        // Посчитаем QueryHash так же, как в основном коде
        var tmpStore = new ResearchContentStore(dbFactory, NullLogger<ResearchContentStore>.Instance);
        var queryHash = tmpStore.ComputeQueryHash(job.Query);

        var learningsForPage1 = new List<Learning>
        {
            new()
            {
                Id        = Guid.NewGuid(),
                JobId     = jobId,
                PageId    = page1.Id,
                QueryHash = queryHash,
                SourceUrl = page1.Url,
                Text      = "Bayern Innovativ fördert KI-Projekte im Mittelstand.",
                CreatedAt = DateTimeOffset.UtcNow
            }
        };

        var learningsForPage2 = new List<Learning>
        {
            new()
            {
                Id        = Guid.NewGuid(),
                JobId     = jobId,
                PageId    = page2.Id,
                QueryHash = queryHash,
                SourceUrl = page2.Url,
                Text      = "Die Hightech Agenda Bayern investiert Milliarden in KI-Standortentwicklung.",
                CreatedAt = DateTimeOffset.UtcNow
            }
        };

        // эмбеддинги через ILearningEmbeddingService
        await embeddingService.PopulateEmbeddingsAsync(
            learningsForPage1.Concat(learningsForPage2),
            CancellationToken.None);

        // сохраняем learnings через ResearchContentStore
        await contentStore.AddLearningsAsync(jobId, page1.Id, learningsForPage1, CancellationToken.None);
        await contentStore.AddLearningsAsync(jobId, page2.Id, learningsForPage2, CancellationToken.None);

        // перечитываем learnings для job (как будет делать твой RAG слой)
        var allLearningsForJob = await db.Learnings
            .Where(l => l.JobId == jobId)
            .ToListAsync();

        var clarificationsText = string.Empty;

        // Act
        var report = await synthesisService.WriteFinalReportAsync(
            job,
            clarificationsText,
            allLearningsForJob,
            CancellationToken.None);

        // Assert – минимальные sanity-checks
        Assert.Contains("## Sources", report);
        Assert.Contains("Bayern Innovativ", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hightech Agenda", report, StringComparison.OrdinalIgnoreCase);

        // Проверяем цитаты
        Assert.Contains("[1] https://www.bayern-innovativ.de/de/seite/ki-produktionsnetzwerk", report);
        Assert.Contains("[2] https://www.bayern.de/politik/hightech-agenda/", report);
    }
}
