using ResearchEngine.Blazor.Api;

namespace ResearchEngine.Blazor.Services;

public sealed class SynthesisHistoryFacade
{
    private readonly IResearchApiClient _api;

    public SynthesisHistoryFacade(IResearchApiClient api)
    {
        _api = api;
    }

    public async Task<ApiResult<ListSynthesesResponse>> ListSynthesesAsync(
        Guid jobId,
        int skip,
        int take,
        CancellationToken ct = default)
    {
        try
        {
            // NSwag: Task<ListSynthesesResponse> SynthesesGETAsync(Guid jobId, int? skip = null, int? take = null, CancellationToken ct = default)
            var resp = await _api.SynthesesGETAsync(jobId, skip, take, ct);
            return ApiResult<ListSynthesesResponse>.Ok(resp);
        }
        catch (Exception ex)
        {
            return ApiResult<ListSynthesesResponse>.Fail(ApiErrorMapper.Map(ex));
        }
    }

    public async Task<ApiResult<SynthesisDto>> GetSynthesisAsync(
        Guid synthesisId,
        CancellationToken ct = default)
    {
        try
        {
            // NSwag: Task<SynthesisDto> SynthesesGET2Async(Guid synthesisId, CancellationToken ct = default)
            var resp = await _api.SynthesesGET2Async(synthesisId, ct);
            return ApiResult<SynthesisDto>.Ok(resp);
        }
        catch (ApiException apiEx) when (apiEx.StatusCode == 404)
        {
            return ApiResult<SynthesisDto>.Fail(new ApiError(ApiErrorKind.Http, "Synthesis not found (404)."));
        }
        catch (Exception ex)
        {
            return ApiResult<SynthesisDto>.Fail(ApiErrorMapper.Map(ex));
        }
    }

    public async Task<ApiResult<bool>> DeleteSynthesisAsync(
        Guid synthesisId,
        CancellationToken ct = default)
    {
        try
        {
            await _api.SynthesesDELETEAsync(synthesisId, ct);
            return ApiResult<bool>.Ok(true);
        }
        catch (ApiException apiEx) when (apiEx.StatusCode == 404)
        {
            return ApiResult<bool>.Fail(new ApiError(ApiErrorKind.Http, "Synthesis not found (404)."));
        }
        catch (Exception ex)
        {
            return ApiResult<bool>.Fail(ApiErrorMapper.Map(ex));
        }
    }
}
