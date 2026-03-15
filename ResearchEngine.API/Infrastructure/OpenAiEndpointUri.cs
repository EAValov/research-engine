namespace ResearchEngine.Infrastructure;

public static class OpenAiEndpointUri
{
    public static Uri AppendV1Path(string endpoint, string relativePath)
    {
        if (endpoint is null) throw new ArgumentNullException(nameof(endpoint));
        return AppendV1Path(new Uri(endpoint, UriKind.Absolute), relativePath);
    }

    public static Uri AppendV1Path(Uri endpoint, string relativePath)
    {
        if (endpoint is null) throw new ArgumentNullException(nameof(endpoint));
        if (relativePath is null) throw new ArgumentNullException(nameof(relativePath));

        return new Uri($"{endpoint.AbsoluteUri.TrimEnd('/')}/{relativePath.TrimStart('/')}");
    }

    public static Uri AppendServerPath(string endpoint, string relativePath)
    {
        if (endpoint is null) throw new ArgumentNullException(nameof(endpoint));
        return AppendServerPath(new Uri(endpoint, UriKind.Absolute), relativePath);
    }

    public static Uri AppendServerPath(Uri endpoint, string relativePath)
    {
        if (endpoint is null) throw new ArgumentNullException(nameof(endpoint));
        if (relativePath is null) throw new ArgumentNullException(nameof(relativePath));

        var baseUri = GetServerBaseUri(endpoint);
        return new Uri($"{baseUri.AbsoluteUri.TrimEnd('/')}/{relativePath.TrimStart('/')}");
    }

    private static Uri GetServerBaseUri(Uri endpoint)
    {
        var path = endpoint.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^3];
        }

        if (string.IsNullOrEmpty(path))
            path = "/";

        return new Uri(endpoint.GetLeftPart(UriPartial.Authority) + path);
    }
}
