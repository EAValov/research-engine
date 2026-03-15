using ResearchEngine.Domain;

namespace ResearchEngine.Infrastructure;

public sealed class ScopedRuntimeSettingsAccessor(IRuntimeSettingsRepository repository)
    : IRuntimeSettingsAccessor
{
    private RuntimeSettingsSnapshot? _snapshot;

    public async Task<RuntimeSettingsSnapshot> GetCurrentAsync(CancellationToken ct = default)
    {
        if (_snapshot is not null)
            return _snapshot;

        _snapshot = await repository.GetCurrentAsync(ct);
        return _snapshot;
    }

    public void SetCurrent(RuntimeSettingsSnapshot snapshot)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    }
}
