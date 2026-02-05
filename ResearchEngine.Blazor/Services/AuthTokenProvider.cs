using System.Net.Http.Headers;

namespace ResearchEngine.Blazor.Services;

public sealed class AuthTokenProvider
{
    // TODO: replace with real auth later
    public string? Token { get; set; } = "9h32F2fNuwMQL0OUujpUoIF6l8S/lGPOM1ylNhH+MKQ=";
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