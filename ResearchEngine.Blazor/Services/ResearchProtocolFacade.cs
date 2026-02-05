using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ResearchEngine.Blazor.Api;

namespace ResearchEngine.Blazor.Services;

public sealed class ResearchProtocolFacade
{
    private readonly IResearchApiClient _api;

    public ResearchProtocolFacade(IResearchApiClient api)
    {
        _api = api;
    }

    public async Task<ApiResult<IReadOnlyList<string>>> GetClarificationQuestionsAsync(
        string query,
        bool includeConfigureQuestions,
        CancellationToken ct)
    {
        try
        {
            var resp = await _api.ClarificationsAsync(new ProtocolClarificationsRequest
            {
                Query = query,
                IncludeConfigureQuestions = includeConfigureQuestions
            }, ct);

            var q = (resp?.Questions ?? Array.Empty<string>())
                .Select(x => (x ?? "").Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .ToList();

           return ApiResult<IReadOnlyList<string>>.Ok(q);
        }
        catch (Exception ex)
        {
            return ApiResult<IReadOnlyList<string>>.Fail(ApiErrorMapper.Map(ex));
        }
    }

    public async Task<ApiResult<(int breadth, int depth, string language, string? region)>> GetParametersAsync(
        string query,
        IReadOnlyList<(string question, string answer)> clarifications,
        Dictionary<string, object>? overrides,
        CancellationToken ct)
    {
        try
        {
            var req = new ProtocolParametersRequest
            {
                Query = query,
                Clarifications = clarifications?
                    .Select(x => new ClarificationDto { Question = x.question, Answer = x.answer })
                    .ToList(),
                Overrides = overrides
            };

            var resp = await _api.ParametersAsync(req, ct);

            var tuple = (resp.Breadth, resp.Depth, resp.Language, resp.Region);
            return ApiResult<(int breadth, int depth, string language, string? region)>.Ok(tuple);
        }
        catch (Exception ex)
        {
            return ApiResult<(int breadth, int depth, string language, string? region)>.Fail(ApiErrorMapper.Map(ex));
        }
    }

    // Used by the component; keep it here so the "shape guess" is centralized.
    public static Dictionary<string, object>? BuildOverrides(bool userTouched, int breadth, int depth, string language, string? region)
    {
        if (!userTouched) return null;

        // Assumption: backend accepts these keys in overrides.
        var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["breadth"] = breadth,
            ["depth"] = depth,
            ["language"] = language
        };

        if (!string.IsNullOrWhiteSpace(region))
            d["region"] = region;

        return d;
    }
}
