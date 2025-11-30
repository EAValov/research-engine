using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ResearchApi.Domain;
using ResearchApi.Infrastructure;
using Xunit;

namespace ResearchApi.IntegrationTests;

[Collection("LlmIntegration")]
public class ResearchContentStorePgTests
{
    private readonly LlmIntegrationFixture _fixture;

    public ResearchContentStorePgTests(LlmIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UpsertScrapedPage_Inserts_New_Page_When_Not_Exists()
    {
        await _fixture.CleanupDatabaseAsync();

        using var scope = _fixture.Services.CreateScope();
        var store    = scope.ServiceProvider.GetRequiredService<IResearchContentStore>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ResearchDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var url     = "https://example.com/page1";
        var content = "Hello world";
        var lang    = "en";
        var region  = "US";

        var page = await store.UpsertScrapedPageAsync(url, content, lang, region, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, page.Id);
        Assert.Equal(url, page.Url);
        Assert.Equal(content, page.Content);
        Assert.Equal(lang, page.Language);
        Assert.Equal(region, page.Region);

        var inDb = await db.ScrapedPages.SingleAsync();
        Assert.Equal(page.Id, inDb.Id);
    }

    [Fact]
    public async Task UpsertScrapedPage_Updates_Content_And_Deletes_Learnings_When_Content_Changes()
    {
        await _fixture.CleanupDatabaseAsync();

        using var scope = _fixture.Services.CreateScope();
        var store     = scope.ServiceProvider.GetRequiredService<IResearchContentStore>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ResearchDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var url       = "https://example.com/page2";
        var contentV1 = "Old content";
        var contentV2 = "New content";

        var pageV1 = new ScrapedPage
        {
            Url         = url,
            Language    = "en",
            Region      = "US",
            Content     = contentV1,
            ContentHash = "hash1",
            CreatedAt   = DateTimeOffset.UtcNow
        };

        var job = new ResearchJob
        {
            Query          = "test",
            Breadth        = 1,
            Depth          = 1,
            Status         = ResearchJobStatus.Running,
            TargetLanguage = "en",
            Region         = "US",
            CreatedAt      = DateTimeOffset.UtcNow,
            UpdatedAt      = DateTimeOffset.UtcNow
        };

        var learning1 = new Learning
        {
            QueryHash = "hash1",
            Text      = "learning1",
            SourceUrl = url,
            CreatedAt = DateTimeOffset.UtcNow,
            Page = pageV1,
            Job = job
        };

        db.Learnings.Add(learning1);
        await db.SaveChangesAsync();

        Assert.Equal(1, await db.Learnings.CountAsync());

        var pageV2 = await store.UpsertScrapedPageAsync(url, contentV2, "en", "US", CancellationToken.None);

        Assert.Equal(pageV1.Id, pageV2.Id);
        Assert.Equal(contentV2, pageV2.Content);
        Assert.Equal(0, await db.Learnings.CountAsync());
    }

    [Fact]
    public async Task GetLearningsForPageAndQueryAsync_Returns_Only_Matching()
    {
        await _fixture.CleanupDatabaseAsync();

        using var scope = _fixture.Services.CreateScope();
        var store     = scope.ServiceProvider.GetRequiredService<IResearchContentStore>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ResearchDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var url       = "https://example.com/page3";
        var content   = "Some content";
        var page      = await store.UpsertScrapedPageAsync(url, content, "en", "US", CancellationToken.None);
        var jobId     = Guid.NewGuid();
        var queryHash = "hash123";

        var job = new ResearchJob
        {
            Id             = jobId,
            Query          = "test",
            Breadth        = 1,
            Depth          = 1,
            Status         = ResearchJobStatus.Running,
            TargetLanguage = "en",
            Region         = "US",
            CreatedAt      = DateTimeOffset.UtcNow,
            UpdatedAt      = DateTimeOffset.UtcNow
        };
        db.ResearchJobs.Add(job);
        await db.SaveChangesAsync();

        var l1 = new Learning
        {
            Id        = Guid.NewGuid(),
            JobId     = jobId,
            PageId    = page.Id,
            QueryHash = queryHash,
            Text      = "match",
            SourceUrl = url,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var l2 = new Learning
        {
            Id        = Guid.NewGuid(),
            JobId     = jobId,
            PageId    = page.Id,
            QueryHash = "other-hash",
            Text      = "not match",
            SourceUrl = url,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.Learnings.AddRange(l1, l2);
        await db.SaveChangesAsync();

        var result = await store.GetLearningsForPageAndQueryAsync(page.Id, queryHash, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("match", result[0].Text);
    }

    [Fact]
    public async Task AddLearningsAsync_Persists_Learnings_When_Job_And_Page_Exist()
    {
        await _fixture.CleanupDatabaseAsync();

        using var scope = _fixture.Services.CreateScope();
        var store     = scope.ServiceProvider.GetRequiredService<IResearchContentStore>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ResearchDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var url     = "https://example.com/page5";
        var content = "Content";
        var page    = await store.UpsertScrapedPageAsync(url, content, "en", "US", CancellationToken.None);

        var jobId = Guid.NewGuid();
        var job   = new ResearchJob
        {
            Id             = jobId,
            Query          = "test query",
            Breadth        = 2,
            Depth          = 2,
            Status         = ResearchJobStatus.Running,
            TargetLanguage = "en",
            Region         = "US",
            CreatedAt      = DateTimeOffset.UtcNow,
            UpdatedAt      = DateTimeOffset.UtcNow
        };

        db.ResearchJobs.Add(job);
        await db.SaveChangesAsync();

        var learning = new Learning
        {
            Id        = Guid.NewGuid(),
            JobId     = jobId,
            PageId    = page.Id,
            QueryHash = "hash",
            Text      = "text",
            SourceUrl = url,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await store.AddLearningsAsync(jobId, page.Id, new[] { learning }, CancellationToken.None);

        var fromDb = await db.Learnings.SingleAsync();
        Assert.Equal(jobId, fromDb.JobId);
        Assert.Equal(page.Id, fromDb.PageId);
        Assert.Equal("text", fromDb.Text);
    }
}
