using ResearchEngine.WebUI.Api;

namespace ResearchEngine.WebUI.Services;

public sealed class SynthesisIterationFacade
{
    private readonly IResearchApiClient _api;

    public SynthesisIterationFacade(IResearchApiClient api)
    {
        _api = api;
    }

    public async Task<ApiResult<RunSynthesisTerminalResponse>> CreateAndRunAsync(
        Guid jobId,
        string? instructions,
        string? outline,
        bool applyOverrides,
        OverridesSnapshot? overrides,
        Guid? parentSynthesisId,
        bool useLatestAsParent,
        CancellationToken ct = default)
    {
        try
        {
            var req = new StartSynthesisRequest
            {
                UseLatestAsParent = useLatestAsParent
            };

            if (parentSynthesisId is not null)
            {
                req.ParentSynthesisId = parentSynthesisId;
                req.UseLatestAsParent = false;
            }

            instructions = (instructions ?? string.Empty).Trim();
            outline = (outline ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(instructions))
                req.Instructions = instructions;

            if (!string.IsNullOrWhiteSpace(outline))
                req.Outline = outline;

            if (applyOverrides && overrides is not null)
            {
                req.SourceOverrides = BuildSourceOverrides(overrides);
                req.LearningOverrides = BuildLearningOverrides(overrides);
            }

            var created = await _api.SynthesesPOSTAsync(jobId, req, ct);

            // explicit run exists
            var terminal = await _api.RunAsync(created.SynthesisId, ct);

            return ApiResult<RunSynthesisTerminalResponse>.Ok(terminal);
        }
        catch (ApiException<RunSynthesisAcceptedResponse> acceptedEx) when (acceptedEx.StatusCode == 202)
        {
            var accepted = acceptedEx.Result;

            var pseudoTerminal = new RunSynthesisTerminalResponse
            {
                JobId = accepted.JobId,
                SynthesisId = accepted.SynthesisId,
                Status = accepted.Status,
                CreatedAt = accepted.CreatedAt,
                CompletedAt = null,
                Message = "Synthesis run accepted and queued."
            };

            return ApiResult<RunSynthesisTerminalResponse>.Ok(pseudoTerminal);
        }
        catch (Exception ex)
        {
            return ApiResult<RunSynthesisTerminalResponse>.Fail(ApiErrorMapper.Map(ex));
        }
    }

    private static ICollection<SynthesisSourceOverrideDto> BuildSourceOverrides(OverridesSnapshot o)
    {
        var list = new List<SynthesisSourceOverrideDto>();

        foreach (var id in o.PinnedSources)
            list.Add(new SynthesisSourceOverrideDto { SourceId = id, Pinned = true, Excluded = false });

        foreach (var id in o.ExcludedSources)
            list.Add(new SynthesisSourceOverrideDto { SourceId = id, Excluded = true, Pinned = false });

        return list;
    }

    private static ICollection<SynthesisLearningOverrideDto> BuildLearningOverrides(OverridesSnapshot o)
    {
        var list = new List<SynthesisLearningOverrideDto>();

        foreach (var id in o.PinnedLearnings)
            list.Add(new SynthesisLearningOverrideDto { LearningId = id, Pinned = true, Excluded = false });

        foreach (var id in o.ExcludedLearnings)
            list.Add(new SynthesisLearningOverrideDto { LearningId = id, Excluded = true, Pinned = false });

        return list;
    }
}
