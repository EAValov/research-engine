using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;

namespace ResearchEngine.WebUI.Services;

public sealed class AuthTokenProvider(IConfiguration configuration)
{
    public string? ApiKey { get; set; } = configuration["ApiAuth:ApiKey"];
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

        var apiKey = tokenProvider.ApiKey;
        if (tokenProvider.Enabled && !string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        return base.SendAsync(request, cancellationToken);
    }
}
