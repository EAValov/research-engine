using Microsoft.Extensions.Options;

namespace ResearchEngine.Infrastructure;

public sealed class EndpointAliasOptions
{
    public List<EndpointAlias> Aliases { get; init; } = [];
}

public sealed class EndpointAlias
{
    public string Host { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
}

public interface IEndpointAliasResolver
{
    Uri Resolve(Uri uri);
}

public sealed class EndpointAliasResolver : IEndpointAliasResolver
{
    private readonly Dictionary<string, Uri> _aliases;

    public EndpointAliasResolver(IOptions<EndpointAliasOptions> options)
    {
        _aliases = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);

        foreach (var alias in options.Value.Aliases)
        {
            if (string.IsNullOrWhiteSpace(alias.Host)
                || string.IsNullOrWhiteSpace(alias.Target)
                || !Uri.TryCreate(alias.Target.Trim(), UriKind.Absolute, out var target))
            {
                continue;
            }

            _aliases[alias.Host.Trim()] = target;
        }
    }

    public Uri Resolve(Uri uri)
    {
        if (!_aliases.TryGetValue(uri.Host, out var target))
        {
            return uri;
        }

        var targetPath = target.AbsolutePath.TrimEnd('/');
        var originalPath = uri.AbsolutePath;
        var combinedPath = string.IsNullOrWhiteSpace(targetPath) || targetPath == "/"
            ? originalPath
            : string.Concat(targetPath, originalPath);

        return new UriBuilder(uri)
        {
            Scheme = target.Scheme,
            Host = target.Host,
            Port = target.IsDefaultPort ? -1 : target.Port,
            Path = combinedPath,
            Query = uri.Query.TrimStart('?')
        }.Uri;
    }
}

public sealed class EndpointAliasHandler(IEndpointAliasResolver resolver) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri is { IsAbsoluteUri: true } uri)
        {
            request.RequestUri = resolver.Resolve(uri);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
