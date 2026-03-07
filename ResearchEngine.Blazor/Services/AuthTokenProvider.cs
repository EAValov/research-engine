using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;

namespace ResearchEngine.Blazor.Services;

public sealed class AuthTokenProvider(IConfiguration configuration)
{
    public string? Token { get; set; } = configuration["ApiAuth:BearerToken"];
}

public sealed class AuthHeaderHandler(AuthTokenProvider tokenProvider) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = tokenProvider.Token;
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return base.SendAsync(request, cancellationToken);
    }
}
