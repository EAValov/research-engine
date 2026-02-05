using ResearchEngine.Blazor.Api;

namespace ResearchEngine.Blazor.Services;

public sealed class LearningGroupFacade
{
    private readonly IResearchApiClient _api;
    private readonly Dictionary<Guid, LearningGroupCardDto?> _cache = new();

    public LearningGroupFacade(IResearchApiClient api)
    {
        _api = api;
    }

    public bool TryGetCached(Guid learningId, out LearningGroupCardDto? group)
        => _cache.TryGetValue(learningId, out group);

    public async Task<ApiResult<LearningGroupCardDto?>> GetGroupAsync(Guid learningId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(learningId, out var cached))
            return ApiResult<LearningGroupCardDto?>.Ok(cached);

        // Fallback: resolve via batch endpoint with a single id (NSwag GroupAsync() returns void)
        var prefetchErr = await PrefetchGroupsAsync(new[] { learningId }, ct);
        if (prefetchErr is not null)
            return ApiResult<LearningGroupCardDto?>.Fail(prefetchErr);

        _cache.TryGetValue(learningId, out var group);
        return ApiResult<LearningGroupCardDto?>.Ok(group);
    }

    public async Task<ApiError?> PrefetchGroupsAsync(IEnumerable<Guid> learningIds, CancellationToken ct = default)
    {
        var ids = learningIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Where(id => !_cache.ContainsKey(id))
            .ToList();

        if (ids.Count == 0)
            return null;

        try
        {
            var req = new BatchResolveLearningGroupsRequest();
            foreach (var id in ids)
                req.LearningIds.Add(id);

            var resp = await _api.ResolveAsync(req, ct);

            // Cache partial results
            var returned = new HashSet<Guid>();
            foreach (var item in resp.Items ?? Array.Empty<ResolvedLearningGroupDto>())
            {
                returned.Add(item.LearningId);
                _cache[item.LearningId] = item.Group;
            }

            // Mark missing as null so we don't hammer the backend
            foreach (var id in ids)
            {
                if (!returned.Contains(id))
                    _cache[id] = null;
            }

            return null;
        }
        catch (Exception ex)
        {
            return ApiErrorMapper.Map(ex);
        }
    }
}