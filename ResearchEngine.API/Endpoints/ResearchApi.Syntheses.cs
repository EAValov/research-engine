using Microsoft.AspNetCore.Mvc;
using ResearchEngine.Domain;

namespace ResearchEngine.API;

public static partial class ResearchApi
{
    /// <summary>
    /// POST /api/research/jobs/{jobId}/syntheses
    /// Creates a synthesis row (no long-running work).
    /// </summary>
    private static async Task<IResult> CreateSynthesisAsync(
        Guid jobId,
        [FromBody] StartSynthesisRequest request,
        IResearchJobRepository jobRepository,
        IResearchSynthesisRepository synthesisRepository,
        IResearchSynthesisOverridesRepository synthesisOverridesRepository,
        IReportSynthesisService synthesisService,
        CancellationToken ct)
    {
        var job = await jobRepository.GetJobAsync(jobId, ct);
        if (job is null)
            return Results.NotFound();

        Guid? parentId = request.ParentSynthesisId;

        if (parentId is null && request.UseLatestAsParent == true)
        {
            var latest = await synthesisRepository.GetLatestSynthesisAsync(jobId, ct);
            parentId = latest?.Id;
        }

        var synthesisId = await synthesisService.CreateSynthesisAsync(
            jobId,
            parentId,
            request.Outline,
            request.Instructions,
            ct);

        var sourceOverrides = request.SourceOverrides?.ToList() ?? new List<SynthesisSourceOverrideDto>();
        if (sourceOverrides.Count > 0)
        {
            await synthesisOverridesRepository.AddOrUpdateSynthesisSourceOverridesAsync(
                synthesisId,
                sourceOverrides,
                ct);
        }

        var learningOverrides = request.LearningOverrides?.ToList() ?? new List<SynthesisLearningOverrideDto>();
        if (learningOverrides.Count > 0)
        {
            await synthesisOverridesRepository.AddOrUpdateSynthesisLearningOverridesAsync(
                synthesisId,
                learningOverrides,
                ct);
        }

        return Results.Created(
            $"/api/syntheses/{synthesisId}",
            new CreateSynthesisResponse(jobId, synthesisId));
    }

    /// <summary>
    /// POST /api/research/syntheses/{synthesisId}/run
    /// Enqueues an existing synthesis run via Hangfire.
    /// </summary>
    private static async Task<IResult> RunSynthesisAsync(
        Guid synthesisId,
        IResearchSynthesisRepository synthesisRepository,
        IReportSynthesisService synthesisService,
        CancellationToken ct)
    {
        var syn = await synthesisRepository.GetSynthesisAsync(synthesisId, ct);
        if (syn is null)
            return Results.NotFound();

        if (syn.Status is SynthesisStatus.Completed or SynthesisStatus.Failed)
        {
            return Results.Ok(new RunSynthesisTerminalResponse(
                syn.JobId,
                syn.Id,
                syn.Status.ToString(),
                syn.CreatedAt,
                syn.CompletedAt,
                "Synthesis is already in a terminal state.")
            );
        }

        var hangfireJobId = synthesisService.EnqueueSynthesisRun(syn.Id);

        return Results.Accepted(
            $"/api/syntheses/{syn.Id}",
            new RunSynthesisAcceptedResponse(
                syn.JobId,
                syn.Id,
                hangfireJobId,
                syn.Status.ToString(),
                syn.CreatedAt)
            );
    }

    /// <summary>
    /// GET /api/research/jobs/{jobId}/syntheses
    /// Lists syntheses for a job (paged).
    /// </summary>
    private static async Task<IResult> ListSynthesesAsync(
        Guid jobId,
        [AsParameters] ListSynthesesRequest req,
        IResearchJobRepository jobRepository,
        IResearchSynthesisRepository synthesisRepository,
        CancellationToken ct)
    {
        var job = await jobRepository.GetJobAsync(jobId, ct);
        if (job is null)
            return Results.NotFound();

        var items = await synthesisRepository.ListSynthesesAsync(
            jobId,
            req.SkipValue,
            req.TakeValue,
            ct);

        var list = items.Select(s => new SynthesisListItemDto(
            s.SynthesisId,
            s.JobId,
            s.ParentSynthesisId,
            s.Status,
            s.CreatedAt,
            s.CompletedAt,
            s.ErrorMessage,
            s.SectionCount
        )).ToList();

        return Results.Ok(new ListSynthesesResponse(
            jobId,
            req.SkipValue,
            req.TakeValue,
            items.Count,
            list));
    }

    /// <summary>
    /// GET /api/research/jobs/{jobId}/syntheses/latest
    /// Returns the latest synthesis for a job including sections.
    /// </summary>
    private static async Task<IResult> GetLatestSynthesisAsync(
        Guid jobId,
        IResearchJobRepository jobRepository,
        IResearchSynthesisRepository synthesisRepository,
        CancellationToken ct)
    {
        var job = await jobRepository.GetJobAsync(jobId, ct);
        if (job is null)
            return Results.NotFound();

        var syn = await synthesisRepository.GetLatestSynthesisAsync(jobId, ct);
        if (syn is null)
            return Results.Ok(new LatestSynthesisResponse(jobId, null));

        var sections = (syn.Sections ?? Array.Empty<SynthesisSection>())
            .OrderBy(s => s.Index)
            .Select(s => new SynthesisSectionDto(
                s.Id,
                s.SynthesisId,
                s.SectionKey,
                s.Index,
                s.Title,
                s.Description,
                s.IsConclusion,
                s.Summary,
                s.ContentMarkdown,
                s.CreatedAt
            ))
            .ToList();

        return Results.Ok(new LatestSynthesisResponse(
            jobId,
            new SynthesisDto(
                syn.Id,
                syn.JobId,
                syn.ParentSynthesisId,
                syn.Status.ToString(),
                syn.Outline,
                syn.Instructions,
                syn.CreatedAt,
                syn.CompletedAt,
                syn.ErrorMessage,
                sections
            )));
    }

    /// <summary>
    /// GET /api/research/syntheses/{synthesisId}
    /// Returns a synthesis by id including sections.
    /// </summary>
    private static async Task<IResult> GetSynthesisAsync(
        Guid synthesisId,
        IResearchSynthesisRepository synthesisRepository,
        CancellationToken ct)
    {
        var syn = await synthesisRepository.GetSynthesisAsync(synthesisId, ct);
        if (syn is null)
            return Results.NotFound();

        var sections = (syn.Sections ?? Array.Empty<SynthesisSection>())
            .OrderBy(s => s.Index)
            .Select(s => new SynthesisSectionDto(
                s.Id,
                s.SynthesisId,
                s.SectionKey,
                s.Index,
                s.Title,
                s.Description,
                s.IsConclusion,
                s.Summary,
                s.ContentMarkdown,
                s.CreatedAt
            ))
            .ToList();

        return Results.Ok(new SynthesisDto(
            syn.Id,
            syn.JobId,
            syn.ParentSynthesisId,
            syn.Status.ToString(),
            syn.Outline,
            syn.Instructions,
            syn.CreatedAt,
            syn.CompletedAt,
            syn.ErrorMessage,
            sections));
    }

    /// <summary>
    /// DELETE /api/research/syntheses/{synthesisId}
    /// Deletes a synthesis row and its related sections/overrides.
    /// </summary>
    private static async Task<IResult> DeleteSynthesisAsync(
        Guid synthesisId,
        IResearchSynthesisRepository synthesisRepository,
        CancellationToken ct)
    {
        var deleted = await synthesisRepository.DeleteSynthesisAsync(synthesisId, ct);
        return deleted ? Results.NoContent() : Results.NotFound();
    }

    /// <summary>
    /// PUT /api/research/syntheses/{synthesisId}/overrides/sources
    /// Upserts source-level overrides for a synthesis.
    /// </summary>
    private static async Task<IResult> UpsertSynthesisSourceOverridesAsync(
        Guid synthesisId,
        [FromBody] IReadOnlyList<SynthesisSourceOverrideDto> overrides,
        IResearchSynthesisRepository synthesisRepository,
        IResearchSynthesisOverridesRepository synthesisOverridesRepository,
        CancellationToken ct)
    {
        var syn = await synthesisRepository.GetSynthesisAsync(synthesisId, ct);
        if (syn is null)
            return Results.NotFound();

        var list = overrides?.ToList() ?? new();
        if (list.Count == 0)
            return Results.Ok(new UpsertOverridesResponse(synthesisId, 0));

        await synthesisOverridesRepository.AddOrUpdateSynthesisSourceOverridesAsync(synthesisId, list, ct);
        return Results.Ok(new UpsertOverridesResponse(synthesisId, list.Count));
    }

    /// <summary>
    /// PUT /api/research/syntheses/{synthesisId}/overrides/learnings
    /// Upserts learning-level overrides for a synthesis.
    /// </summary>
    private static async Task<IResult> UpsertSynthesisLearningOverridesAsync(
        Guid synthesisId,
        [FromBody] IReadOnlyList<SynthesisLearningOverrideDto> overrides,
        IResearchSynthesisRepository synthesisRepository,
        IResearchSynthesisOverridesRepository synthesisOverridesRepository,
        CancellationToken ct)
    {
        var syn = await synthesisRepository.GetSynthesisAsync(synthesisId, ct);
        if (syn is null)
            return Results.NotFound();

        var list = overrides?.ToList() ?? new();
        if (list.Count == 0)
            return Results.Ok(new UpsertOverridesResponse(synthesisId, 0));

        await synthesisOverridesRepository.AddOrUpdateSynthesisLearningOverridesAsync(synthesisId, list, ct);
        return Results.Ok(new UpsertOverridesResponse(synthesisId, list.Count));
    }
}
