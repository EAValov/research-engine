using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ResearchEngine.Blazor.Api;

namespace ResearchEngine.Blazor.Services;

public sealed class JobCreationFacade
{
    private readonly IResearchApiClient _api;

    public JobCreationFacade(IResearchApiClient api)
    {
        _api = api;
    }

    public async Task<ApiResult<Guid>> CreateJobAsync(
        string query,
        IReadOnlyList<(string question, string answer)> clarifications,
        int breadth,
        int depth,
        string language,
        string? region,
        CancellationToken ct)
    {
        try
        {
            var req = new CreateResearchJobRequest
            {
                Query = query,
                Clarifications = (clarifications ?? Array.Empty<(string question, string answer)>())
                    .Select(x => new ClarificationDto { Question = x.question, Answer = x.answer })
                    .ToList(),
                Breadth = breadth,
                Depth = depth,
                Language = language,
                Region = region
            };

            var resp = await _api.JobsPOSTAsync(req, ct);
            return ApiResult<Guid>.Ok(resp.JobId);
        }
        catch (Exception ex)
        {
            return ApiResult<Guid>.Fail(ApiErrorMapper.Map(ex));
        }
    }
}
