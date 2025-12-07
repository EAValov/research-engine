using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ResearchApi.Domain;

namespace ResearchApi.Infrastructure;

public class ResearchContentStore (IDbContextFactory<ResearchDbContext> dbContextFactory, ILogger<ResearchContentStore> logger) : IResearchContentStore
{
    public async Task<ScrapedPage> UpsertScrapedPageAsync(
        string url,
        string content,
        string? language,
        string? region,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL must not be empty.", nameof(url));

        if (content is null)
            throw new ArgumentNullException(nameof(content));

        var normalizedUrl = url.Trim();
        var contentHash = ComputeSha256(content);
    
        await using var _db = await dbContextFactory.CreateDbContextAsync(ct);

        var existing = await _db.ScrapedPages
            .FirstOrDefaultAsync(p => p.Url == normalizedUrl, ct);

        if (existing != null)
        {
            if (existing.ContentHash == contentHash)
            {
                logger.LogDebug(
                    "Scraped page for URL {Url} already up-to-date (same content hash).",
                    normalizedUrl);
                return existing;
            }

            // Content changed → clear old learnings for this page
            logger.LogDebug(
                "Content for URL {Url} changed (hash {Old} -> {New}), deleting stale learnings.",
                normalizedUrl, existing.ContentHash, contentHash);

            await _db.Learnings
                .Where(l => l.PageId == existing.Id)
                .ExecuteDeleteAsync(ct);

            existing.Content     = content;
            existing.ContentHash = contentHash;
            existing.Language    = language;
            existing.Region      = region;
            existing.CreatedAt   = DateTimeOffset.UtcNow;

            await _db.SaveChangesAsync(ct);
            return existing;
        }

        var page = new ScrapedPage
        {
            Id          = Guid.NewGuid(),
            Url         = normalizedUrl,
            Language    = language,
            Region      = region,
            Content     = content,
            ContentHash = contentHash,
            CreatedAt   = DateTimeOffset.UtcNow
        };

        _db.ScrapedPages.Add(page);
        await _db.SaveChangesAsync(ct);

        logger.LogInformation("Inserted new scraped page for URL {Url}.", normalizedUrl);

        return page;
    }

    public async Task<IReadOnlyList<Learning>> GetLearningsForPageAndQueryAsync(
        Guid pageId,
        string queryHash,
        CancellationToken ct = default)
    {  
        await using var _db = await dbContextFactory.CreateDbContextAsync(ct);

        var list = await _db.Learnings
            .Where(l => l.PageId == pageId && l.QueryHash == queryHash)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync(ct);

        logger.LogDebug(
            "Loaded {Count} learnings from DB for Page {PageId} and QueryHash={QueryHash}.",
            list.Count, pageId, queryHash);

        return list;
    }

    public async Task AddLearningsAsync(
        Guid jobId,
        Guid pageId,
        IEnumerable<Learning> learnings,
        CancellationToken ct)
    {
        
        await using var _db = await dbContextFactory.CreateDbContextAsync(ct);
        var jobExists = await _db.ResearchJobs.AnyAsync(j => j.Id == jobId, ct);
        if (!jobExists)
            throw new InvalidOperationException($"ResearchJob {jobId} does not exist.");

        var page = await _db.ScrapedPages.FirstOrDefaultAsync(p => p.Id == pageId, ct);
        if (page is null)
            throw new InvalidOperationException($"ScrapedPage {pageId} does not exist.");

        _db.Learnings.AddRange(learnings);
        await _db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Persisted {Count} learnings for Job {JobId} and Page {PageId}.",
            learnings.Count(), jobId, pageId);
    }

    private string ComputeSha256(string input)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash  = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash); // uppercase hex is fine
    }

    public string ComputeQueryHash(string query)
    {
        var normalized = query?.Trim().ToLowerInvariant() ?? string.Empty;
        return ComputeSha256(normalized);
    }
}