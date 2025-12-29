using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace ResearchEngine.Web.Authentication;

public sealed class BearerAuthenticationHandler : AuthenticationHandler<BearerAuthenticationOptions>
{
    public BearerAuthenticationHandler(
        IOptionsMonitor<BearerAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // If auth is disabled, don't authenticate anyone (treat as "no auth needed")
        if (!Options.Enabled)
            return Task.FromResult(AuthenticateResult.NoResult());

        if (!Request.Headers.TryGetValue(HeaderNames.Authorization, out var values))
            return Task.FromResult(AuthenticateResult.NoResult());

        var header = values.ToString();
        if (string.IsNullOrWhiteSpace(header))
            return Task.FromResult(AuthenticateResult.NoResult());

        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());

        var token = header.Substring(prefix.Length).Trim();
        if (string.IsNullOrEmpty(token))
            return Task.FromResult(AuthenticateResult.Fail("Missing bearer token"));

        if (!IsTokenAllowed(token, Options.BearerTokens))
            return Task.FromResult(AuthenticateResult.Fail("Invalid token"));

        // Authenticated identity with no claims
        var identity = new ClaimsIdentity(authenticationType: Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers[HeaderNames.WWWAuthenticate] = "Bearer";
        return base.HandleChallengeAsync(properties);
    }

    private static bool IsTokenAllowed(string token, IReadOnlyCollection<string> allowed)
    {
        if (allowed.Count == 0) return false;

        var tokenBytes = Encoding.UTF8.GetBytes(token);
        foreach (var candidate in allowed)
        {
            if (string.IsNullOrEmpty(candidate)) continue;

            var candidateBytes = Encoding.UTF8.GetBytes(candidate);
            if (candidateBytes.Length != tokenBytes.Length) continue;

            if (CryptographicOperations.FixedTimeEquals(candidateBytes, tokenBytes))
                return true;
        }

        return false;
    }
}


public sealed class BearerAuthenticationOptions : AuthenticationSchemeOptions
{
    public bool Enabled { get; set; } = true;

    public IReadOnlyCollection<string> BearerTokens { get; set; } = Array.Empty<string>();
}
