using ResearchEngine.WebUI.Api;

namespace ResearchEngine.WebUI.Services;

public sealed class JobFacade
{
    private readonly IResearchApiClient _api;

    public JobFacade(IResearchApiClient api)
    {
        _api = api;
    }

    public async Task<ApiResult<IReadOnlyList<ResearchEventDto>>> GetEventsAsync(Guid jobId, CancellationToken ct = default)
    {
        try
        {
            var events = await _api.EventsAsync(jobId, ct);
            var list = (events ?? Array.Empty<ResearchEventDto>()).ToList();
            return ApiResult<IReadOnlyList<ResearchEventDto>>.Ok(list);
        }
        catch (Exception ex)
        {
            return ApiResult<IReadOnlyList<ResearchEventDto>>.Fail(ApiErrorMapper.Map(ex));
        }
    }

    public async Task<ApiResult<CancelJobResponse>> CancelJobAsync(Guid jobId, CancellationToken ct = default)
    {
        try
        {
            // Backend contract says no body; NSwag generated optional body param.
            var resp = await _api.CancelAsync(jobId, body: null, cancellationToken: ct);
            return ApiResult<CancelJobResponse>.Ok(resp);
        }
        catch (Exception ex)
        {
            return ApiResult<CancelJobResponse>.Fail(ApiErrorMapper.Map(ex));
        }
    }

    public async Task<ApiResult<bool>> ArchiveJobAsync(Guid jobId, CancellationToken ct = default)
    {
        try
        {
            await _api.ArchiveAsync(jobId, ct);
            return ApiResult<bool>.Ok(true);
        }
        catch (Exception ex)
        {
            return ApiResult<bool>.Fail(ApiErrorMapper.Map(ex));
        }
    }

    public async Task<ApiResult<bool>> UnarchiveJobAsync(Guid jobId, CancellationToken ct = default)
    {
        try
        {
            await _api.UnarchiveAsync(jobId, ct);
            return ApiResult<bool>.Ok(true);
        }
        catch (Exception ex)
        {
            return ApiResult<bool>.Fail(ApiErrorMapper.Map(ex));
        }
    }

    public async Task<ApiResult<bool>> SoftDeleteJobAsync(Guid jobId, CancellationToken ct = default)
    {
        try
        {
            await _api.JobsDELETEAsync(jobId, body: null, cancellationToken: ct);
            return ApiResult<bool>.Ok(true);
        }
        catch (Exception ex)
        {
            return ApiResult<bool>.Fail(ApiErrorMapper.Map(ex));
        }
    }
}
