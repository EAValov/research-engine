using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using ResearchEngine.Domain;

namespace ResearchEngine.Infrastructure;

public sealed record SseTicketClaims(Guid JobId, string Subject, DateTimeOffset ExpiresAtUtc);

internal sealed record SseTicketPayload(
    Guid JobId,
    string Sub,
    DateTimeOffset ExpUtc,
    string Nonce);

public sealed class JobSseTicketService : IJobSseTicketService
{
    private const string ProtectorPurpose = "ResearchEngine.Web.SSE.JobEventsTicket.v1";
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(45);

    private readonly IDataProtector _protector;
    private readonly TimeProvider _time;

    public JobSseTicketService(IDataProtectionProvider dp, TimeProvider timeProvider)
    {
        _protector = dp.CreateProtector(ProtectorPurpose);
        _time = timeProvider;
    }

    public string Create(Guid jobId, ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? user.FindFirstValue("sub")
                  ?? "unknown";

        var now = _time.GetUtcNow();
        var payload = new SseTicketPayload(
            JobId: jobId,
            Sub: sub,
            ExpUtc: now.Add(DefaultTtl),
            Nonce: Guid.NewGuid().ToString("N"));

        var json = JsonSerializer.Serialize(payload);
        return _protector.Protect(json);
    }

    public bool TryValidate(Guid jobId, string ticket, out SseTicketClaims claims)
    {
        claims = default!;

        string json;
        try
        {
            json = _protector.Unprotect(ticket);
        }
        catch
        {
            return false;
        }

        SseTicketPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<SseTicketPayload>(json);
        }
        catch
        {
            return false;
        }

        if (payload is null)
            return false;

        // Must match job
        if (payload.JobId != jobId)
            return false;

        // Must not be expired
        var now = _time.GetUtcNow();
        if (payload.ExpUtc <= now)
            return false;

        claims = new SseTicketClaims(payload.JobId, payload.Sub, payload.ExpUtc);
        return true;
    }

    public DateTimeOffset GetExpiryUtc(string ticket)
    {
        // Best-effort; if invalid, return "now"
        try
        {
            var json = _protector.Unprotect(ticket);
            var payload = JsonSerializer.Deserialize<SseTicketPayload>(json);
            return payload?.ExpUtc ?? _time.GetUtcNow();
        }
        catch
        {
            return _time.GetUtcNow();
        }
    }
}