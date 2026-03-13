using ResearchEngine.WebUI.Api;

namespace ResearchEngine.WebUI.Services;

public sealed class EvidenceFacade
{
    private readonly IResearchApiClient _api;

    public EvidenceFacade(IResearchApiClient api)
    {
        _api = api;
    }

    // ------------------------
    // Sources
    // ------------------------

    public async Task<ApiResult<IReadOnlyList<SourceListItemDto>>> GetSourcesAsync(
        Guid jobId,
        CancellationToken ct = default)
    {
        try
        {
            // NSwag: Task<ListSourcesResponse> SourcesGETAsync(Guid jobId, CancellationToken ct = default)
            var resp = await _api.SourcesGETAsync(jobId, ct);
            var items = (resp.Sources ?? Array.Empty<SourceListItemDto>()).ToList();
            return ApiResult<IReadOnlyList<SourceListItemDto>>.Ok(items);
        }
        catch (Exception ex)
        {
            return ApiResult<IReadOnlyList<SourceListItemDto>>.Fail(ApiErrorMapper.Map(ex));
        }
    }

    public async Task<ApiResult<bool>> DeleteSourceAsync(
        Guid jobId,
        Guid sourceId,
        CancellationToken ct = default)
    {
        try
        {
            // NSwag: Task SourcesDELETEAsync(Guid jobId, Guid sourceId, CancellationToken ct = default)
            await _api.SourcesDELETEAsync(jobId, sourceId, ct);
            return ApiResult<bool>.Ok(true);
        }
        catch (Exception ex)
        {
            return ApiResult<bool>.Fail(ApiErrorMapper.Map(ex));
        }
    }

    // ------------------------
    // Learnings
    // ------------------------

    public async Task<ApiResult<IReadOnlyList<LearningListItemDto>>> GetLearningsAsync(
        Guid jobId,
        int skip,
        int take,
        string? sourceReference = null,
        Guid? promoteLearningId = null,
        CancellationToken ct = default)
    {
        try
        {
            // NSwag: Task<ListLearningsResponse> LearningsGETAsync(Guid jobId, int? skip = null, int? take = null, string? sourceReference = null, Guid? promoteLearningId = null, CancellationToken ct = default)
            var resp = await _api.LearningsGETAsync(jobId, skip, take, sourceReference, promoteLearningId, ct);
            var items = (resp.Learnings ?? Array.Empty<LearningListItemDto>()).ToList();
            return ApiResult<IReadOnlyList<LearningListItemDto>>.Ok(items);
        }
        catch (Exception ex)
        {
            return ApiResult<IReadOnlyList<LearningListItemDto>>.Fail(ApiErrorMapper.Map(ex));
        }
    }

    public async Task<ApiResult<LearningListItemDto>> AddLearningAsync(
        Guid jobId,
        AddLearningRequest request,
        CancellationToken ct = default)
    {
        try
        {
            if (request is null)
                return ApiResult<LearningListItemDto>.Fail(new ApiError(ApiErrorKind.Validation, "Add learning payload is required."));

            // NSwag: Task<AddLearningResponse> LearningsPOSTAsync(Guid jobId, AddLearningRequest body, CancellationToken ct = default)
            var resp = await _api.LearningsPOSTAsync(jobId, request, ct);

            // AddLearningResponse returns AddedLearningDto (no SourceReference field),
            // but the UI expects LearningListItemDto.SourceReference. Best-effort mapping:
            var added = resp.Learning;
            var mapped = new LearningListItemDto
            {
                LearningId = added.LearningId,
                SourceId = added.SourceId,
                SourceReference = request.Reference ?? "(manual)",
                ImportanceScore = added.ImportanceScore,
                CreatedAt = added.CreatedAt,
                Text = added.Text
            };

            return ApiResult<LearningListItemDto>.Ok(mapped);
        }
        catch (Exception ex)
        {
            return ApiResult<LearningListItemDto>.Fail(ApiErrorMapper.Map(ex));
        }
    }

    public async Task<ApiResult<bool>> DeleteLearningAsync(
        Guid jobId,
        Guid learningId,
        CancellationToken ct = default)
    {
        try
        {
            // NSwag: Task LearningsDELETEAsync(Guid jobId, Guid learningId, CancellationToken ct = default)
            await _api.LearningsDELETEAsync(jobId, learningId, ct);
            return ApiResult<bool>.Ok(true);
        }
        catch (Exception ex)
        {
            return ApiResult<bool>.Fail(ApiErrorMapper.Map(ex));
        }
    }
}
