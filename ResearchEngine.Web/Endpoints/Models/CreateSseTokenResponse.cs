namespace ResearchEngine.Web;

public sealed record CreateSseTokenResponse(
    Guid JobId,
    string Ticket,
    string StreamUrl,
    DateTimeOffset ExpiresAtUtc);
