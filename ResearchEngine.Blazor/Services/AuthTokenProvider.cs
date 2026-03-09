using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;

namespace ResearchEngine.Blazor.Services;

public sealed class AuthTokenProvider(IConfiguration configuration)
{
    public string? Token { get; set; } = configuration["ApiAuth:BearerToken"];
    public bool Enabled { get; set; } = true;
}

public sealed class AuthHeaderHandler(
    AuthTokenProvider tokenProvider,
    ApiConnectionSettings apiConnection) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Keep API host dynamic without mutating HttpClient.BaseAddress after first request.
        if (ApiConnectionSettings.NormalizeBaseUrl(apiConnection.ApiBaseUrl) is { } baseUrl
            && request.RequestUri is not null)
        {
            var apiBase = new Uri(baseUrl, UriKind.Absolute);

            request.RequestUri = request.RequestUri.IsAbsoluteUri
                ? new Uri(apiBase, request.RequestUri.PathAndQuery)
                : new Uri(apiBase, request.RequestUri);
        }

        var token = tokenProvider.Token;
        if (tokenProvider.Enabled && !string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return base.SendAsync(request, cancellationToken);
    }
}
