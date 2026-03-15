namespace ResearchEngine.Domain;

public interface IRuntimeSettingsRepository
{
    Task<RuntimeSettingsSnapshot> GetCurrentAsync(CancellationToken ct = default);
    Task<RuntimeSettingsSnapshot> UpdateAsync(RuntimeSettingsSnapshot snapshot, CancellationToken ct = default);
    Task EnsureInitializedAsync(CancellationToken ct = default);
}
