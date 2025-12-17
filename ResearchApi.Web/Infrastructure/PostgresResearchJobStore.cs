using Microsoft.EntityFrameworkCore;
using ResearchApi.Domain;

namespace ResearchApi.Infrastructure;

public sealed class PostgresResearchJobStore(
    IDbContextFactory<ResearchDbContext> dbContextFactory,
    IResearchEventBus eventBus,
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

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        db.ResearchJobs.Add(jobEntity);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Job {Id} created", jobEntity.Id);

        // Optional: emit Created event right away (nice for webhook/SSE clients)
        await AppendEventAsync(
            jobEntity.Id,
            new ResearchEvent(DateTimeOffset.UtcNow, ResearchEventStage.Created, "Job created"),
            ct);

        return jobEntity;
    }

    public async Task<ResearchJob?> GetJobAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        return await db.ResearchJobs
            .Include(j => j.Clarifications)
            .Include(j => j.Events)
            .Include(j => j.VisitedUrls)
            .FirstOrDefaultAsync(j => j.Id == id, ct);
    }

    public async Task<int> UpdateJobAsync(ResearchJob job, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var entity = await db.ResearchJobs
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

        entity.VisitedUrls.Clear();
        foreach (var url in job.VisitedUrls.DistinctBy(u => u.Url))
        {
            entity.VisitedUrls.Add(new VisitedUrl
            {
                Url = url.Url,
                JobId = job.Id
            });
        }

        logger.LogInformation("Job {Id} updated", entity.Id);

        return await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ResearchEvent>> GetEventsAsync(Guid jobId, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        return await db.ResearchEvents
            .Where(e => e.JobId == jobId)
            .OrderBy(e => e.Id) // stable order for replay
            .ToListAsync(ct);
    }

    public async Task<int> AppendEventAsync(Guid jobId, ResearchEvent ev, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        // Persist
        ev.JobId = jobId;
        await db.ResearchEvents.AddAsync(ev, ct);

        logger.LogInformation("Job {JobId} [{Stage}] {Message}", jobId, ev.Stage, ev.Message);

        var saved = await db.SaveChangesAsync(ct);

        // Publish AFTER save (so ev.Id is populated by EF)
        try
        {
            await eventBus.PublishAsync(jobId, ev, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish event for job {JobId}", jobId);
        }

        return saved;
    }
}