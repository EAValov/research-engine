using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

using ResearchApi.Domain;

namespace ResearchApi.Infrastructure;

public class InMemoryResearchJobStore : IResearchJobStore
{
    private readonly ConcurrentDictionary<Guid, ResearchJob> _jobs = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<ResearchEvent>> _events = new();

    public ResearchJob CreateJob(string query, IEnumerable<Clarification> clarifications, int breadth, int depth, string language, string? region)
    {
        var job = new ResearchJob()
        {
            Id = Guid.NewGuid(),
            Query = query,
            Clarifications = clarifications.ToList(),
            Breadth = breadth,
            Depth = depth,
            Status = ResearchJobStatus.Pending,
            Events = new List<ResearchEvent>(),
            ReportMarkdown = null,
            VisitedUrls = new List<VisitedUrl>(),
            TargetLanguage = language,
            Region = region
        };

        _jobs.TryAdd(job.Id, job);
        _events.TryAdd(job.Id, new ConcurrentQueue<ResearchEvent>());

        return job;
    }

    public ResearchJob? GetJob(Guid id)
    {
        return _jobs.TryGetValue(id, out var job) ? job : null;
    }

    public void UpdateJob(ResearchJob job)
    {
        _jobs.AddOrUpdate(job.Id, job, (_, _) => job);
    }

    public IReadOnlyList<ResearchEvent> GetEvents(Guid jobId)
    {
        if (_events.TryGetValue(jobId, out var queue))
        {
            return queue.ToList();
        }

        return Array.Empty<ResearchEvent>();
    }

    public void AppendEvent(Guid jobId, ResearchEvent ev)
    {
        if (_events.TryGetValue(jobId, out var queue))
        {
            queue.Enqueue(ev);
        }
    }
}
