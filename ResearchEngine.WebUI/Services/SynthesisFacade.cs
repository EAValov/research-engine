using ResearchEngine.WebUI.Api;

namespace ResearchEngine.WebUI.Services;

public sealed class SynthesisFacade
{
    private readonly IResearchApiClient _api;

    public SynthesisFacade(IResearchApiClient api)
    {
        _api = api;
    }

    public async Task<ApiResult<SynthesisDto?>> GetLatestSynthesisAsync(Guid jobId, CancellationToken ct = default)
    {
        try
        {
            var resp = await _api.LatestAsync(jobId, ct);
            return ApiResult<SynthesisDto?>.Ok(resp.Synthesis);
        }
        catch (ApiException apiEx) when (apiEx.StatusCode == 404)
        {
            // “latest” not found => no synthesis available yet (not an error)
            return ApiResult<SynthesisDto?>.Ok(null);
        }
        catch (Exception ex)
        {
            return ApiResult<SynthesisDto?>.Fail(ApiErrorMapper.Map(ex));
        }
    }
}

/// <summary>
/// UI input for creating/running a synthesis.
/// Kept in Services to avoid component-local type drift.
/// </summary>
public sealed record CreateSynthesisInput(
    string Instructions,
    string Outline,
    bool ApplyOverrides,
    Guid? ParentSynthesisId,
    bool UseLatestAsParent);

