using ResearchEngine.Blazor.State;

namespace ResearchEngine.Blazor.Services;

public sealed class OverridesSnapshot
{
    public IReadOnlyCollection<Guid> PinnedSources { get; init; } = Array.Empty<Guid>();
    public IReadOnlyCollection<Guid> ExcludedSources { get; init; } = Array.Empty<Guid>();
    public IReadOnlyCollection<Guid> PinnedLearnings { get; init; } = Array.Empty<Guid>();
    public IReadOnlyCollection<Guid> ExcludedLearnings { get; init; } = Array.Empty<Guid>();
}

public sealed class OverridesSummary
{
    public int PinnedSources { get; init; }
    public int ExcludedSources { get; init; }
    public int PinnedLearnings { get; init; }
    public int ExcludedLearnings { get; init; }
}

public sealed class OverridesStore
{
    private readonly AppStateStore _state;

    /// <summary>Raised whenever draft overrides for a job change.</summary>
    public event Action<Guid>? Changed;

    public OverridesStore(AppStateStore state)
    {
        _state = state;
    }

    public bool HasAny(Guid jobId)
    {
        var o = _state.GetOrCreateJobUi(jobId).Overrides;
        return o.PinnedSourceIds.Count + o.ExcludedSourceIds.Count + o.PinnedLearningIds.Count + o.ExcludedLearningIds.Count > 0;
    }

    public OverridesSummary GetSummary(Guid jobId)
    {
        var o = _state.GetOrCreateJobUi(jobId).Overrides;
        return new OverridesSummary
        {
            PinnedSources = o.PinnedSourceIds.Count,
            ExcludedSources = o.ExcludedSourceIds.Count,
            PinnedLearnings = o.PinnedLearningIds.Count,
            ExcludedLearnings = o.ExcludedLearningIds.Count
        };
    }

    public OverridesSnapshot GetSnapshot(Guid jobId)
    {
        var o = _state.GetOrCreateJobUi(jobId).Overrides;
        return new OverridesSnapshot
        {
            PinnedSources = o.PinnedSourceIds.ToArray(),
            ExcludedSources = o.ExcludedSourceIds.ToArray(),
            PinnedLearnings = o.PinnedLearningIds.ToArray(),
            ExcludedLearnings = o.ExcludedLearningIds.ToArray()
        };
    }

    public void Clear(Guid jobId)
    {
        _state.UpdateJobUi(jobId, ui =>
        {
            ui.Overrides.PinnedSourceIds.Clear();
            ui.Overrides.ExcludedSourceIds.Clear();
            ui.Overrides.PinnedLearningIds.Clear();
            ui.Overrides.ExcludedLearningIds.Clear();
        });

        Changed?.Invoke(jobId);
    }

    public bool IsPinnedSource(Guid jobId, Guid sourceId)
    {
        var o = _state.GetOrCreateJobUi(jobId).Overrides;
        return o.PinnedSourceIds.Contains(sourceId);
    }

    public bool IsExcludedSource(Guid jobId, Guid sourceId)
    {
        var o = _state.GetOrCreateJobUi(jobId).Overrides;
        return o.ExcludedSourceIds.Contains(sourceId);
    }

    public bool IsPinnedLearning(Guid jobId, Guid learningId)
    {
        var o = _state.GetOrCreateJobUi(jobId).Overrides;
        return o.PinnedLearningIds.Contains(learningId);
    }

    public bool IsExcludedLearning(Guid jobId, Guid learningId)
    {
        var o = _state.GetOrCreateJobUi(jobId).Overrides;
        return o.ExcludedLearningIds.Contains(learningId);
    }

    public void TogglePinnedSource(Guid jobId, Guid sourceId)
    {
        _state.UpdateJobUi(jobId, ui =>
        {
            var o = ui.Overrides;
            o.ExcludedSourceIds.Remove(sourceId);

            if (!o.PinnedSourceIds.Remove(sourceId))
                o.PinnedSourceIds.Add(sourceId);
        });

        Changed?.Invoke(jobId);
    }

    public void ToggleExcludedSource(Guid jobId, Guid sourceId)
    {
        _state.UpdateJobUi(jobId, ui =>
        {
            var o = ui.Overrides;
            o.PinnedSourceIds.Remove(sourceId);

            if (!o.ExcludedSourceIds.Remove(sourceId))
                o.ExcludedSourceIds.Add(sourceId);
        });

        Changed?.Invoke(jobId);
    }

    public void TogglePinnedLearning(Guid jobId, Guid learningId)
    {
        _state.UpdateJobUi(jobId, ui =>
        {
            var o = ui.Overrides;
            o.ExcludedLearningIds.Remove(learningId);

            if (!o.PinnedLearningIds.Remove(learningId))
                o.PinnedLearningIds.Add(learningId);
        });

        Changed?.Invoke(jobId);
    }

    public void ToggleExcludedLearning(Guid jobId, Guid learningId)
    {
        _state.UpdateJobUi(jobId, ui =>
        {
            var o = ui.Overrides;
            o.PinnedLearningIds.Remove(learningId);

            if (!o.ExcludedLearningIds.Remove(learningId))
                o.ExcludedLearningIds.Add(learningId);
        });

        Changed?.Invoke(jobId);
    }
}