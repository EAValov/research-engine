using Microsoft.Extensions.Configuration;

namespace ResearchEngine.Blazor.Services;

public sealed class ApiConnectionSettings
{
    private readonly AuthTokenProvider _authTokenProvider;
    private readonly string _fallbackBaseUrl;
    private string _apiBaseUrl;

    public ApiConnectionSettings(
        AuthTokenProvider authTokenProvider,
        IConfiguration configuration)
    {
        _authTokenProvider = authTokenProvider;

        _fallbackBaseUrl = NormalizeBaseUrl(configuration["ApiBaseUrl"])
            ?? "http://localhost:8090/";
        _apiBaseUrl = _fallbackBaseUrl;

        var defaultToken = (configuration["ApiAuth:BearerToken"] ?? string.Empty).Trim();

        _authTokenProvider.Token = defaultToken;
        _authTokenProvider.Enabled = true;
    }

    public string ApiBaseUrl => TrimTrailingSlash(_apiBaseUrl);

    public string BearerToken => _authTokenProvider.Token ?? string.Empty;

    public bool AuthEnabled => _authTokenProvider.Enabled;

    public string FallbackApiBaseUrl => TrimTrailingSlash(_fallbackBaseUrl);

    public bool TryApply(string? apiBaseUrl, string? bearerToken, bool authEnabled)
    {
        var normalizedBaseUrl = NormalizeBaseUrl(apiBaseUrl);
        if (normalizedBaseUrl is null)
            return false;

        _apiBaseUrl = normalizedBaseUrl;
        _authTokenProvider.Token = (bearerToken ?? string.Empty).Trim();
        _authTokenProvider.Enabled = authEnabled;
        return true;
    }

    public static string? NormalizeBaseUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri))
            return null;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return null;

        return TrimTrailingSlash(uri.ToString()) + "/";
    }

    private static string TrimTrailingSlash(string value)
        => value.TrimEnd('/');
}
