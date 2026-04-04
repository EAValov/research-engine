using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;

namespace ResearchEngine.WebUI.Services;

public sealed class ApiConnectionSettings
{
    private readonly AuthTokenProvider _authTokenProvider;
    private readonly string _configuredBaseUrl;
    private string _apiBaseUrl;

    public ApiConnectionSettings(
        AuthTokenProvider authTokenProvider,
        IConfiguration configuration,
        NavigationManager navigationManager)
    {
        _authTokenProvider = authTokenProvider;

        _configuredBaseUrl = ResolveConfiguredBaseUrl(
                configuration["ApiBaseUrl"],
                navigationManager.BaseUri)
            ?? throw new InvalidOperationException("Missing or invalid Web UI configuration value: ApiBaseUrl.");
        _apiBaseUrl = _configuredBaseUrl;

        var defaultApiKey = (configuration["ApiAuth:ApiKey"] ?? string.Empty).Trim();

        _authTokenProvider.ApiKey = defaultApiKey;
        _authTokenProvider.Enabled = true;
    }

    public string ApiBaseUrl => TrimTrailingSlash(_apiBaseUrl);

    public string ApiKey => _authTokenProvider.ApiKey ?? string.Empty;

    public bool AuthEnabled => _authTokenProvider.Enabled;

    public string ConfiguredApiBaseUrl => TrimTrailingSlash(_configuredBaseUrl);

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

    public static string? ResolveConfiguredBaseUrl(string? configuredBaseUrl, string? currentPageBaseUrl)
    {
        var configured = configuredBaseUrl?.Trim();
        if (string.IsNullOrWhiteSpace(configured))
            return null;

        if (string.Equals(configured, "same-origin", StringComparison.OrdinalIgnoreCase))
            return NormalizeBaseUrl(currentPageBaseUrl);

        return NormalizeBaseUrl(configured);
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
