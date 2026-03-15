using Microsoft.EntityFrameworkCore;
using ResearchEngine.Domain;

namespace ResearchEngine.Infrastructure;

public sealed class PostgresRuntimeSettingsRepository(
    IDbContextFactory<ResearchDbContext> dbContextFactory,
    RuntimeSettingsSnapshot bootstrapSettings)
    : IRuntimeSettingsRepository
{
    public async Task<RuntimeSettingsSnapshot> GetCurrentAsync(CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var record = await GetOrCreateRecordAsync(db, ct);
        return record.ToSnapshot();
    }

    public async Task<RuntimeSettingsSnapshot> UpdateAsync(
        RuntimeSettingsSnapshot snapshot,
        CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var record = await GetOrCreateRecordAsync(db, ct);
        record.Apply(snapshot);
        await db.SaveChangesAsync(ct);
        return record.ToSnapshot();
    }

    public async Task EnsureInitializedAsync(CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        _ = await GetOrCreateRecordAsync(db, ct);
    }

    private async Task<RuntimeSettingsRecord> GetOrCreateRecordAsync(
        ResearchDbContext db,
        CancellationToken ct)
    {
        var existing = await db.RuntimeSettings
            .FirstOrDefaultAsync(x => x.Id == RuntimeSettingsRecord.SingletonId, ct);

        if (existing is not null)
            return existing;

        var created = RuntimeSettingsRecord.FromSnapshot(bootstrapSettings);
        db.RuntimeSettings.Add(created);

        try
        {
            await db.SaveChangesAsync(ct);
            return created;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();

            var concurrent = await db.RuntimeSettings
                .FirstOrDefaultAsync(x => x.Id == RuntimeSettingsRecord.SingletonId, ct);

            if (concurrent is not null)
                return concurrent;

            throw;
        }
    }
}
