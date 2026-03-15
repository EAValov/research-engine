namespace ResearchEngine.Domain;

public interface IRuntimeSettingsAccessor
{
    Task<RuntimeSettingsSnapshot> GetCurrentAsync(CancellationToken ct = default);
    void SetCurrent(RuntimeSettingsSnapshot snapshot);
}
