using System.Security.Claims;
using ResearchEngine.Infrastructure;

namespace ResearchEngine.Domain;

public interface IJobSseTicketService
{
    string Create(Guid jobId, ClaimsPrincipal user);
    bool TryValidate(Guid jobId, string ticket, out SseTicketClaims claims);
    DateTimeOffset GetExpiryUtc(string ticket);
}
