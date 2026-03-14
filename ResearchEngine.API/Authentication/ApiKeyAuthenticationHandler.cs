using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Serilog;

namespace ResearchEngine.API.Authentication;

public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationOptions>
{
    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationOptions> options,
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

        var apiKey = header.Substring(prefix.Length).Trim();
        if (string.IsNullOrEmpty(apiKey))
            return Task.FromResult(AuthenticateResult.Fail("Missing API key"));

        if (!IsApiKeyAllowed(apiKey, Options.ApiKeys))
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));

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

    private static bool IsApiKeyAllowed(string apiKey, IReadOnlyCollection<string> allowed)
    {
        if (allowed.Count == 0) return false;

        var apiKeyBytes = Encoding.UTF8.GetBytes(apiKey);
        foreach (var candidate in allowed)
        {
            if (string.IsNullOrEmpty(candidate)) continue;

            var candidateBytes = Encoding.UTF8.GetBytes(candidate);
            if (candidateBytes.Length != apiKeyBytes.Length) continue;

            if (CryptographicOperations.FixedTimeEquals(candidateBytes, apiKeyBytes))
                return true;
        }

        return false;
    }
}

public sealed class AuthenticationOptions : AuthenticationSchemeOptions
{
    public bool Enabled { get; set; } = true;

    public IReadOnlyCollection<string> ApiKeys { get; set; } = Array.Empty<string>();
}
