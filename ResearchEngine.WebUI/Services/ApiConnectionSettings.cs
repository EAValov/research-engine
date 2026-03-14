using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Components;

namespace ResearchEngine.WebUI.Services;

public sealed class ApiConnectionSettings
{
    private readonly AuthTokenProvider _authTokenProvider;
    private readonly string _fallbackBaseUrl;
    private string _apiBaseUrl;

    public ApiConnectionSettings(
        AuthTokenProvider authTokenProvider,
        IConfiguration configuration,
        NavigationManager navigationManager)
    {
        _authTokenProvider = authTokenProvider;

        _fallbackBaseUrl = ResolveFallbackBaseUrl(
                configuration["ApiBaseUrl"],
                navigationManager.BaseUri)
            ?? "http://localhost:8090/";
        _apiBaseUrl = _fallbackBaseUrl;

        var defaultApiKey = (configuration["ApiAuth:ApiKey"] ?? string.Empty).Trim();

        _authTokenProvider.ApiKey = defaultApiKey;
        _authTokenProvider.Enabled = true;
    }

    public string ApiBaseUrl => TrimTrailingSlash(_apiBaseUrl);

    public string ApiKey => _authTokenProvider.ApiKey ?? string.Empty;

    public bool AuthEnabled => _authTokenProvider.Enabled;

    public string FallbackApiBaseUrl => TrimTrailingSlash(_fallbackBaseUrl);

    public bool TryApply(string? apiBaseUrl, string? apiKey, bool authEnabled)
    {
        var normalizedBaseUrl = NormalizeBaseUrl(apiBaseUrl);
        if (normalizedBaseUrl is null)
            return false;

        _apiBaseUrl = normalizedBaseUrl;
        _authTokenProvider.ApiKey = (apiKey ?? string.Empty).Trim();
        _authTokenProvider.Enabled = authEnabled;
        return true;
    }

    public static string? ResolveFallbackBaseUrl(string? configuredBaseUrl, string? currentPageBaseUrl)
    {
        var configured = NormalizeBaseUrl(configuredBaseUrl);
        var current = NormalizeBaseUrl(currentPageBaseUrl);

        if (configured is null)
            return current;

        if (current is null)
            return configured;

        if (Uri.TryCreate(configured, UriKind.Absolute, out var configuredUri)
            && Uri.TryCreate(current, UriKind.Absolute, out var currentUri)
            && IsLocalDeploymentUri(configuredUri)
            && IsLocalDeploymentUri(currentUri)
            && configuredUri.IsLoopback != currentUri.IsLoopback)
        {
            return current;
        }

        return configured;
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

    public static bool IsLocalDeploymentUrl(string? raw)
    {
        if (NormalizeBaseUrl(raw) is not { } normalized
            || !Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return IsLocalDeploymentUri(uri);
    }

    private static bool IsLocalDeploymentUri(Uri uri)
        => uri.IsLoopback || uri.Host.EndsWith(".llm.local", StringComparison.OrdinalIgnoreCase);
}
