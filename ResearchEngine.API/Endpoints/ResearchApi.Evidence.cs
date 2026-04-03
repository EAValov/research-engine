using Microsoft.AspNetCore.Mvc;
using ResearchEngine.Domain;

namespace ResearchEngine.API;

public static partial class ResearchApi
{
    /// <summary>
    /// GET /api/jobs/{jobId}/sources
    /// Lists sources for a job.
    /// </summary>
    private static async Task<IResult> ListSourcesAsync(
        Guid jobId,
        IResearchJobRepository jobRepository,
        IResearchSourceRepository sourceRepository,
        CancellationToken ct)
    {
        var job = await jobRepository.GetJobAsync(jobId, ct);
        if (job is null)
            return Results.NotFound();

        var sources = await sourceRepository.ListSourcesAsync(jobId, ct);

        var items = sources.Select(s => new SourceListItemDto(
            s.SourceId,
            s.Reference,
            s.Title,
            s.Domain,
            s.Language,
            s.Region,
            s.Classification,
            s.ReliabilityTier,
            s.ReliabilityScore,
            s.IsPrimarySource,
            s.ReliabilityRationale,
            s.CreatedAt,
            s.LearningCount
        )).ToList();

        return Results.Ok(new ListSourcesResponse(jobId, items.Count, items));
    }

    /// <summary>
    /// GET /api/jobs/{jobId}/learnings
    /// Lists learnings for a job (paged).
    /// </summary>
    private static async Task<IResult> ListLearningsAsync(
        Guid jobId,
        [AsParameters] ListLearningsRequest req,
        IResearchJobRepository jobRepository,
        IResearchLearningRepository learningRepository,
        CancellationToken ct)
    {
        var job = await jobRepository.GetJobAsync(jobId, ct);
        if (job is null)
            return Results.NotFound();

        var result = await learningRepository.ListLearningsAsync(
            jobId,
            req.SkipValue,
            req.TakeValue,
            req.SourceReferenceValue,
            req.PromoteLearningId,
            ct);

        var items = result.Items.Select(l => new LearningListItemDto(
            l.LearningId,
            l.SourceId,
            l.SourceReference,
            l.ImportanceScore,
            l.CreatedAt,
            l.Text
        )).ToList();

        return Results.Ok(new ListLearningsResponse(
            jobId,
            req.SkipValue,
            req.TakeValue,
            result.Total,
            result.Page,
            items));
    }

    /// <summary>
    /// POST /api/jobs/{jobId}/learnings
    /// Adds a user learning (creates/uses user source, computes embedding, assigns a group).
    /// </summary>
    private static async Task<IResult> AddLearningAsync(
        Guid jobId,
        [FromBody] AddLearningRequest request,
        IResearchJobRepository jobRepository,
        ILearningIntelService learningIntelService,
        CancellationToken ct)
    {
        var job = await jobRepository.GetJobAsync(jobId, ct);
        if (job is null)
            return Results.NotFound();

        if (string.IsNullOrWhiteSpace(request.Text))
            return Results.BadRequest(new ErrorResponse("Text is required."));

        var score = Math.Clamp(request.ImportanceScore ?? 1.0f, 0f, 1f);

        var learning = await learningIntelService.AddUserLearningAsync(
            jobId,
            request.Text,
            score,
            request.Reference,
            request.EvidenceText,
            request.Language,
            request.Region,
            ct);

        var dto = new AddedLearningDto(
            learning.Id,
            learning.SourceId,
            learning.LearningGroupId,
            learning.ImportanceScore,
            learning.CreatedAt,
            learning.Text);

        return Results.Created(
            $"/api/learnings/{learning.Id}",
            new AddLearningResponse(jobId, dto));
    }

    /// <summary>
    /// DELETE /api/jobs/{jobId}/learnings/{learningId}
    /// Soft-deletes a learning.
    /// </summary>
    private static async Task<IResult> SoftDeleteLearningAsync(
        Guid jobId,
        Guid learningId,
        IResearchJobRepository jobRepository,
        IResearchLearningRepository learningRepository,
        CancellationToken ct)
    {
        var job = await jobRepository.GetJobAsync(jobId, ct);
        if (job is null)
            return Results.NotFound();

        var ok = await learningRepository.SoftDeleteLearningAsync(jobId, learningId, ct);
        return ok ? Results.NoContent() : Results.NotFound();
    }

    /// <summary>
    /// GET /api/learnings/{learningId}/group
    /// Returns a group card for the learning group.
    /// </summary>
    private static async Task<IResult> GetLearningGroupByLearningIdAsync(
        Guid learningId,
        IResearchLearningRepository learningRepository,
        CancellationToken ct)
    {
        var card = await learningRepository.GetLearningGroupCardByLearningIdAsync(learningId, ct);
        return card is null ? Results.NotFound() : Results.Ok(card);
    }

    /// <summary>
    /// POST /api/learnings/groups/resolve
    /// Resolves groups for multiple learning IDs in one request.
    /// </summary>
    private static async Task<IResult> ResolveLearningGroupsBatchAsync(
        [FromBody] BatchResolveLearningGroupsRequest request,
        IResearchLearningRepository learningRepository,
        CancellationToken ct)
    {
        if (request.LearningIds.Count == 0)
            return Results.BadRequest(new ErrorResponse("learningIds is required."));

        var items = await learningRepository.ResolveLearningGroupsBatchAsync(request.LearningIds, ct);
        return Results.Ok(new BatchResolveLearningGroupsResponse(items));
    }

    /// <summary>
    /// DELETE /api/jobs/{jobId}/sources/{sourceId}
    /// Soft-deletes a source.
    /// </summary>
    private static async Task<IResult> SoftDeleteSourceAsync(
        Guid jobId,
        Guid sourceId,
        IResearchJobRepository jobRepository,
        IResearchSourceRepository sourceRepository,
        CancellationToken ct)
    {
        var job = await jobRepository.GetJobAsync(jobId, ct);
        if (job is null)
            return Results.NotFound();

        var ok = await sourceRepository.SoftDeleteSourceAsync(jobId, sourceId, ct);
        return ok ? Results.NoContent() : Results.NotFound();
    }
}
