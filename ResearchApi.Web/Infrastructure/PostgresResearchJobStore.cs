
using Microsoft.EntityFrameworkCore;
using ResearchApi.Domain;

namespace ResearchApi.Infrastructure;

public class PostgresResearchJobStore (
    IDbContextFactory<ResearchDbContext> dbContextFactory,
    ILogger<IResearchJobStore> logger
) : IResearchJobStore
{
    public async Task<ResearchJob> CreateJobAsync(
        string query,
        IEnumerable<Clarification> clarifications,
        int breadth,
        int depth,
        string language,
        string? region,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        var jobEntity = new ResearchJob
        {
            Id = Guid.NewGuid(),
            Query = query,
            Breadth = breadth,
            Depth = depth,
            Status = ResearchJobStatus.Pending,
            TargetLanguage = language,
            Region = region,
            CreatedAt = now,
            UpdatedAt = now,
            Clarifications = clarifications.Select(c => new Clarification
            {
                Question = c.Question,
                Answer = c.Answer
            }).ToList()
        };

        await using var _db = await dbContextFactory.CreateDbContextAsync(ct);

        _db.ResearchJobs.Add(jobEntity);
        await _db.SaveChangesAsync();

        logger.LogInformation("Job {Id} created", jobEntity.Id);

        return jobEntity;
    }

    public async Task<ResearchJob?> GetJobAsync(Guid id, CancellationToken ct = default)
    {
        await using var _db = await dbContextFactory.CreateDbContextAsync(ct);

        return await _db.ResearchJobs
           .Include(j => j.Clarifications)
           .Include(j => j.Events)
           .Include(j => j.VisitedUrls)
           .FirstOrDefaultAsync(j => j.Id == id, ct);
    }

    public async Task<int> UpdateJobAsync(ResearchJob job, CancellationToken ct = default)
    {
        await using var _db = await dbContextFactory.CreateDbContextAsync(ct);

        var entity = await _db.ResearchJobs
            .Include(j => j.Clarifications)
            .Include(j => j.Events)
            .Include(j => j.VisitedUrls)
            .FirstOrDefaultAsync(j => j.Id == job.Id, ct);

        if (entity is null)
            return 0;

        entity.Query = job.Query;
        entity.Breadth = job.Breadth;
        entity.Depth = job.Depth;
        entity.Status = job.Status;
        entity.ReportMarkdown = job.ReportMarkdown;
        entity.TargetLanguage = job.TargetLanguage;
        entity.Region = job.Region;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        // Visited URLs: simple re-sync
        entity.VisitedUrls.Clear();
        foreach (var url in job.VisitedUrls.Distinct())
        {
            entity.VisitedUrls.Add(new VisitedUrl
            {
                Url = url.Url,
                JobId = job.Id
            });
        }

        logger.LogInformation("job {Id} updated", entity.Id);

        return await _db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<ResearchEvent>> GetEventsAsync(Guid jobId, CancellationToken ct = default)
    {
        await using var _db = await dbContextFactory.CreateDbContextAsync(ct);

        return await _db.ResearchEvents
            .Where(e => e.JobId == jobId)
            .OrderBy(e => e.Timestamp)
            .Select(e => new ResearchEvent(e.Timestamp, e.Stage, e.Message))
            .ToListAsync(ct);
    }

    public async Task<int> AppendEventAsync(Guid jobId, ResearchEvent ev, CancellationToken ct = default)
    {
        await using var _db = await dbContextFactory.CreateDbContextAsync(ct);
        var entity = new ResearchEvent
        {
            JobId = jobId,
            Timestamp = ev.Timestamp,
            Stage = ev.Stage,
            Message = ev.Message
        };

        await _db.ResearchEvents.AddAsync(entity, ct);

        logger.LogInformation("Job {JobId} [{Stage}] {Message}", jobId, ev.Stage, ev.Message);
        return await _db.SaveChangesAsync(ct);
    }
}
