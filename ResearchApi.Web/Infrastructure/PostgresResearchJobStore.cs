using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;
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
                Id = Guid.NewGuid(),
                Question = c.Question,
                Answer = c.Answer,
                CreatedAt = now
            }).ToList()
        };

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        db.ResearchJobs.Add(jobEntity);
        await db.SaveChangesAsync(ct);

        await AppendEventAsync(
            jobEntity.Id,
            new ResearchEvent(DateTimeOffset.UtcNow, ResearchEventStage.Created, "Job created"),
            ct);

        return jobEntity;
    }

    public async Task<ResearchJob?> GetJobAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        // Keep this lightweight: include sources + syntheses, but do not eager load source content/learnings
        return await db.ResearchJobs
            .Include(j => j.Clarifications)
            .Include(j => j.Events)
            .Include(j => j.Sources)
            .Include(j => j.Syntheses)
            .FirstOrDefaultAsync(j => j.Id == id, ct);
    }

    public async Task<int> UpdateJobAsync(ResearchJob job, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var entity = await db.ResearchJobs
            .FirstOrDefaultAsync(j => j.Id == job.Id, ct);

        if (entity is null)
            return 0;

        entity.Query = job.Query;
        entity.Breadth = job.Breadth;
        entity.Depth = job.Depth;
        entity.Status = job.Status;
        entity.TargetLanguage = job.TargetLanguage;
        entity.Region = job.Region;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        logger.LogInformation("Job {Id} updated", entity.Id);

        return await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ResearchEvent>> GetEventsAsync(Guid jobId, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        return await db.ResearchEvents
            .Where(e => e.JobId == jobId)
            .OrderBy(e => e.Timestamp)
            .ThenBy(e => e.Id)
            .ToListAsync(ct);
    }

    public async Task<int> AppendEventAsync(Guid jobId, ResearchEvent ev, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        ev.JobId = jobId;
        await db.ResearchEvents.AddAsync(ev, ct);

        logger.LogInformation("Job {JobId} [{Stage}] {Message}", jobId, ev.Stage, ev.Message);

        var saved = await db.SaveChangesAsync(ct);

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

    public async Task<bool> SourceExistsAsync(Guid jobId, string url, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        return await db.Sources.AnyAsync(s => s.JobId == jobId && s.Url == url, ct);
    }

    public async Task<Source> UpsertSourceAsync(
        Guid jobId,
        string url,
        string contentHash,
        string content,
        string? title,
        string? language,
        string? region,
        CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var existing = await db.Sources
            .FirstOrDefaultAsync(s => s.JobId == jobId && s.Url == url, ct);

        if (existing is not null)
        {
            var contentChanged = !string.Equals(existing.ContentHash, contentHash, StringComparison.Ordinal);

            existing.ContentHash = contentHash;
            existing.Content = content;
            existing.Title = title;
            existing.Language = language;
            existing.Region = region;

            if (contentChanged)
            {
                // Delete stale learnings for this source.
                // Cascades will remove LearningEmbeddings via FK if configured (recommended).
                await db.Learnings
                    .Where(l => l.SourceId == existing.Id)
                    .ExecuteDeleteAsync(ct);
            }

            await db.SaveChangesAsync(ct);
            return existing;
        }

        var now = DateTimeOffset.UtcNow;

        var source = new Source
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            Url = url,
            ContentHash = contentHash,
            Content = content,
            Title = title,
            Language = language,
            Region = region,
            CreatedAt = now
        };

        db.Sources.Add(source);
        await db.SaveChangesAsync(ct);
        return source;
    }

    public async Task<Learning> AddLearningAsync(
        Guid jobId,
        Guid sourceId,
        string queryHash,
        string text,
        float importanceScore,
        string evidenceText,
        float[]? embeddingVector,
        CancellationToken ct = default)
    {
        // Enforce evidence truncation rule here to keep rows sane
        const int maxEvidenceLen = 20_000;
        if (evidenceText.Length > maxEvidenceLen)
            evidenceText = evidenceText.Substring(0, maxEvidenceLen);

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        // Ensure source exists (foreign key safety)
        var sourceExists = await db.Sources.AnyAsync(s => s.Id == sourceId && s.JobId == jobId, ct);
        if (!sourceExists)
            throw new InvalidOperationException($"Source {sourceId} does not exist for job {jobId}.");

        var now = DateTimeOffset.UtcNow;

        var learning = new Learning
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            SourceId = sourceId,
            QueryHash = queryHash,
            Text = text,
            ImportanceScore = importanceScore,
            EvidenceText = evidenceText,
            CreatedAt = now
        };

        db.Learnings.Add(learning);

        if (embeddingVector is not null)
        {
            var emb = new LearningEmbedding
            {
                Id = Guid.NewGuid(),
                LearningId = learning.Id,
                Vector = new Pgvector.Vector(embeddingVector),
                CreatedAt = now
            };
            db.LearningEmbeddings.Add(emb);
        }

        await db.SaveChangesAsync(ct);
        return learning;
    }

    public async Task<Synthesis> CreateSynthesisAsync(
        Guid jobId,
        Guid? parentSynthesisId,
        string? outline,
        string? instructions,
        CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var now = DateTimeOffset.UtcNow;

        var synthesis = new Synthesis
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            ParentSynthesisId = parentSynthesisId,
            Status = SynthesisStatus.Created,
            Outline = outline,
            Instructions = instructions,
            CreatedAt = now
        };

        db.Syntheses.Add(synthesis);
        await db.SaveChangesAsync(ct);
        return synthesis;
    }

    public async Task<int> MarkSynthesisRunningAsync(Guid synthesisId, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var entity = await db.Syntheses.FirstOrDefaultAsync(s => s.Id == synthesisId, ct);
        if (entity is null) return 0;

        entity.Status = SynthesisStatus.Running;
        return await db.SaveChangesAsync(ct);
    }

    public async Task<int> CompleteSynthesisAsync(
        Guid synthesisId,
        IReadOnlyList<SynthesisSection> sections,
        CancellationToken ct = default)
    {
        if (sections is null)
            throw new ArgumentNullException(nameof(sections));

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var entity = await db.Syntheses.FirstOrDefaultAsync(s => s.Id == synthesisId, ct);
        if (entity is null) return 0;

        // Replace any existing sections for idempotency/retry safety.
        await db.SynthesisSections
            .Where(s => s.SynthesisId == synthesisId)
            .ExecuteDeleteAsync(ct);

        var now = DateTimeOffset.UtcNow;

        // Normalize + attach
        for (int i = 0; i < sections.Count; i++)
        {
            var s = sections[i];

            if (s.Id == Guid.Empty)
                s.Id = Guid.NewGuid();

            s.SynthesisId = synthesisId;

            if (s.SectionKey == Guid.Empty)
                s.SectionKey = Guid.NewGuid();

            // Force deterministic ordering
            s.Index = i;

            s.Title = (s.Title ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(s.Title))
                s.Title = $"Section {i + 1}";

            s.Description ??= string.Empty;
            s.ContentMarkdown ??= string.Empty;

            if (s.CreatedAt == default)
                s.CreatedAt = now;
        }

        db.SynthesisSections.AddRange(sections);

        entity.Status = SynthesisStatus.Completed;
        entity.CompletedAt = now;
        entity.ErrorMessage = null;

        return await db.SaveChangesAsync(ct);
    }

    public async Task<int> FailSynthesisAsync(Guid synthesisId, string errorMessage, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var entity = await db.Syntheses.FirstOrDefaultAsync(s => s.Id == synthesisId, ct);
        if (entity is null) return 0;

        entity.Status = SynthesisStatus.Failed;
        entity.ErrorMessage = errorMessage.Length > 4000 ? errorMessage[..4000] : errorMessage;
        entity.CompletedAt = DateTimeOffset.UtcNow;

        return await db.SaveChangesAsync(ct);
    }

    public async Task<Synthesis?> GetSynthesisAsync(Guid synthesisId, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        return await db.Syntheses
            .Include(s => s.Sections.OrderBy(x => x.Index))
            .FirstOrDefaultAsync(s => s.Id == synthesisId, ct);
    }

    public async Task<Synthesis?> GetLatestSynthesisAsync(Guid jobId, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        return await db.Syntheses
            .Where(s => s.JobId == jobId)
            .OrderByDescending(s => s.CreatedAt)
            .Include(s => s.Sections.OrderBy(x => x.Index))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<Learning>> GetLearningsForSourceAndQueryAsync(
        Guid sourceId,
        string queryHash,
        CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        // Include Source + Embedding if you need them downstream (report + similarity).
        // AsNoTracking because these are cached read-only.
        return await db.Learnings
            .AsNoTracking()
            .Include(l => l.Source)
            .Include(l => l.Embedding)
            .Where(l => l.SourceId == sourceId && l.QueryHash == queryHash)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task AddLearningsAsync(
        Guid jobId,
        Guid sourceId,
        IEnumerable<Learning> learnings,
        CancellationToken ct = default)
    {
        var list = learnings as IList<Learning> ?? learnings.ToList();
        if (list.Count == 0)
            return;

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var sourceExists = await db.Sources.AnyAsync(s => s.Id == sourceId && s.JobId == jobId, ct);
        if (!sourceExists)
            throw new InvalidOperationException($"Source {sourceId} does not exist for job {jobId}.");

        // Normalize required fields + enforce evidence limit
        const int maxEvidenceLen = 20_000;

        foreach (var l in list)
        {
            // force ownership (avoid accidental mismatch)
            l.JobId = jobId;
            l.SourceId = sourceId;

            if (l.Id == Guid.Empty)
                l.Id = Guid.NewGuid();

            if (l.CreatedAt == default)
                l.CreatedAt = DateTimeOffset.UtcNow;

            if (l.EvidenceText.Length > maxEvidenceLen)
                l.EvidenceText = l.EvidenceText[..maxEvidenceLen];

            // If Embedding exists, ensure FK points to learning
            if (l.Embedding is not null)
            {
                if (l.Embedding.Id == Guid.Empty)
                    l.Embedding.Id = Guid.NewGuid();

                l.Embedding.LearningId = l.Id;
                if (l.Embedding.CreatedAt == default)
                    l.Embedding.CreatedAt = l.CreatedAt;
            }
        }

        db.Learnings.AddRange(list);

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Persisted {Count} learnings for Job {JobId} and Source {SourceId}.",
            list.Count, jobId, sourceId);
    }

    public async Task AddOrUpdateSynthesisSourceOverridesAsync(
        Guid synthesisId,
        IEnumerable<SynthesisSourceOverrideDto> overrides,
        CancellationToken ct = default)
    {
        var list = overrides?.ToList() ?? new();
        if (list.Count == 0) return;

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var sourceIds = list.Select(x => x.SourceId).Distinct().ToList();

        var existing = await db.SynthesisSourceOverrides
            .Where(o => o.SynthesisId == synthesisId && sourceIds.Contains(o.SourceId))
            .ToListAsync(ct);

        var map = existing.ToDictionary(x => x.SourceId);
        var now = DateTimeOffset.UtcNow;

        foreach (var dto in list)
        {
            if (map.TryGetValue(dto.SourceId, out var row))
            {
                row.Excluded = dto.Excluded;
                row.Pinned   = dto.Pinned;
            }
            else
            {
                db.SynthesisSourceOverrides.Add(new SynthesisSourceOverride
                {
                    Id = Guid.NewGuid(),
                    SynthesisId = synthesisId,
                    SourceId = dto.SourceId,
                    Excluded = dto.Excluded,
                    Pinned = dto.Pinned,
                    CreatedAt = now
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task AddOrUpdateSynthesisLearningOverridesAsync(
        Guid synthesisId,
        IEnumerable<SynthesisLearningOverrideDto> overrides,
        CancellationToken ct = default)
    {
        var list = overrides?.ToList() ?? new();
        if (list.Count == 0) return;

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var learningIds = list.Select(x => x.LearningId).Distinct().ToList();

        var existing = await db.SynthesisLearningOverrides
            .Where(o => o.SynthesisId == synthesisId && learningIds.Contains(o.LearningId))
            .ToListAsync(ct);

        var map = existing.ToDictionary(x => x.LearningId);
        var now = DateTimeOffset.UtcNow;

        foreach (var dto in list)
        {
            if (map.TryGetValue(dto.LearningId, out var row))
            {
                row.ScoreOverride = dto.ScoreOverride;
                row.Excluded      = dto.Excluded;
                row.Pinned        = dto.Pinned;
            }
            else
            {
                db.SynthesisLearningOverrides.Add(new SynthesisLearningOverride
                {
                    Id = Guid.NewGuid(),
                    SynthesisId = synthesisId,
                    LearningId = dto.LearningId,
                    ScoreOverride = dto.ScoreOverride,
                    Excluded = dto.Excluded,
                    Pinned = dto.Pinned,
                    CreatedAt = now
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SourceListItemDto>> ListSourcesAsync(Guid jobId, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        return await db.Sources
            .AsNoTracking()
            .Where(s => s.JobId == jobId)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new SourceListItemDto(
                s.Id,
                s.Url,
                s.Title,
                s.Language,
                s.Region,
                s.CreatedAt,
                s.Learnings.Count))
            .ToListAsync(ct);
    }

    public async Task<PagedResult<LearningListItemDto>> ListLearningsAsync(
        Guid jobId,
        int skip = 0,
        int take = 200,
        CancellationToken ct = default)
    {
        skip = Math.Max(skip, 0);
        take = Math.Clamp(take, 1, 500);

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var baseQuery = db.Learnings
            .AsNoTracking()
            .Where(l => l.JobId == jobId);

        var total = await baseQuery.CountAsync(ct);

        var items = await baseQuery
            .Include(l => l.Source)
            .OrderByDescending(l => l.ImportanceScore)
            .ThenByDescending(l => l.CreatedAt)
            .Skip(skip)
            .Take(take)
            .Select(l => new LearningListItemDto(
                l.Id,
                l.SourceId,
                l.Source.Url,
                l.ImportanceScore,
                l.CreatedAt,
                l.Text))
            .ToListAsync(ct);

        return new PagedResult<LearningListItemDto>(items, skip, take, total);
    }

    public async Task<SynthesisOverridesSnapshot> GetSynthesisOverridesAsync(
        Guid synthesisId,
        CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var syn = await db.Syntheses
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == synthesisId, ct);

        if (syn is null)
            throw new InvalidOperationException($"Synthesis {synthesisId} not found.");

        var sourceOverrides = await db.SynthesisSourceOverrides
            .AsNoTracking()
            .Where(o => o.SynthesisId == synthesisId)
            .Select(o => new SynthesisSourceOverrideDto(o.SourceId, o.Excluded, o.Pinned))
            .ToListAsync(ct);

        var learningOverrides = await db.SynthesisLearningOverrides
            .AsNoTracking()
            .Where(o => o.SynthesisId == synthesisId)
            .Select(o => new SynthesisLearningOverrideDto(o.LearningId, o.ScoreOverride, o.Excluded, o.Pinned))
            .ToListAsync(ct);

        return new SynthesisOverridesSnapshot(
            SynthesisId: synthesisId,
            JobId: syn.JobId,
            SourceOverridesBySourceId: sourceOverrides.ToDictionary(x => x.SourceId),
            LearningOverridesByLearningId: learningOverrides.ToDictionary(x => x.LearningId));
    }

    public async Task<IReadOnlyList<Learning>> VectorSearchLearningsAsync(
        Vector queryVector,
        Guid? jobId,
        string? queryHash,
        string? language,
        string? region,
        float minImportance,
        int topK,
        CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var q = db.Learnings
            .AsNoTracking()
            .Include(l => l.Source)
            .Include(l => l.Embedding)
            .Where(l => l.Embedding != null)
            .Where(l => l.ImportanceScore >= minImportance);

        if (jobId is Guid jid)
            q = q.Where(l => l.JobId == jid);

        if (!string.IsNullOrWhiteSpace(queryHash))
            q = q.Where(l => l.QueryHash == queryHash);

        if (!string.IsNullOrWhiteSpace(language))
            q = q.Where(l => l.Source.Language == language);

        if (!string.IsNullOrWhiteSpace(region))
            q = q.Where(l => l.Source.Region == region);

        // Similarity first, then importance (stable)
        q = q.OrderBy(l => l.Embedding!.Vector.CosineDistance(queryVector))
            .ThenByDescending(l => l.ImportanceScore)
            .Take(Math.Clamp(topK, 1, 200));

        return await q.ToListAsync(ct);
    }
}