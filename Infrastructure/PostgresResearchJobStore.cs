
using Microsoft.EntityFrameworkCore;
using ResearchApi.Domain;

namespace ResearchApi.Infrastructure;

public class PostgresResearchJobStore : IResearchJobStore
{
    private readonly ResearchDbContext _db;

    public PostgresResearchJobStore(ResearchDbContext db)
    {
        _db = db;
    }

    public ResearchJob CreateJob(
        string query,
        IEnumerable<Clarification> clarifications,
        int breadth,
        int depth,
        string language,
        string? region)
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

        _db.ResearchJobs.Add(jobEntity);
        _db.SaveChanges();

        return jobEntity;
    }

    public ResearchJob? GetJob(Guid id)
    {
        var job = _db.ResearchJobs
            .Include(j => j.Clarifications)
            .Include(j => j.Events)
            .Include(j => j.VisitedUrls)
            .FirstOrDefault(j => j.Id == id);

        return job;
    }

    public void UpdateJob(ResearchJob job)
    {
        var entity = _db.ResearchJobs
            .Include(j => j.Clarifications)
            .Include(j => j.Events)
            .Include(j => j.VisitedUrls)
            .FirstOrDefault(j => j.Id == job.Id);

        if (entity is null)
            return;

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

        _db.SaveChanges();
    }

    public IReadOnlyList<ResearchEvent> GetEvents(Guid jobId)
    {
        return _db.ResearchEvents
            .Where(e => e.JobId == jobId)
            .OrderBy(e => e.Timestamp)
            .Select(e => new ResearchEvent(e.Timestamp, e.Stage, e.Message))
            .ToList();
    }

    public void AppendEvent(Guid jobId, ResearchEvent ev)
    {
        var entity = new ResearchEvent
        {
            JobId = jobId,
            Timestamp = ev.Timestamp,
            Stage = ev.Stage,
            Message = ev.Message
        };

        _db.ResearchEvents.Add(entity);
        _db.SaveChanges();
    }
}
