sealed record ContainerImageReference(string Name, string Tag, string? Sha256)
{
    public string? Registry { get; } = SplitRegistry(Name).Registry;

    public string Repository { get; } = SplitRegistry(Name).Repository;

    public static ContainerImageReference Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Container image reference cannot be empty.");
        }

        var image = value.Trim();
        string? sha256 = null;

        var digestIndex = image.IndexOf("@sha256:", StringComparison.OrdinalIgnoreCase);
        if (digestIndex >= 0)
        {
            sha256 = image[(digestIndex + "@sha256:".Length)..];
            image = image[..digestIndex];
        }

        var lastSlash = image.LastIndexOf('/');
        var lastColon = image.LastIndexOf(':');
        if (lastColon > lastSlash)
        {
            return new ContainerImageReference(
                image[..lastColon],
                image[(lastColon + 1)..],
                sha256);
        }

        return new ContainerImageReference(image, "latest", sha256);
    }

    private static (string? Registry, string Repository) SplitRegistry(string imageName)
    {
        var firstSlash = imageName.IndexOf('/');
        if (firstSlash <= 0)
        {
            return (null, imageName);
        }

        var firstSegment = imageName[..firstSlash];
        var hasRegistry =
            firstSegment.Contains('.', StringComparison.Ordinal) ||
            firstSegment.Contains(':', StringComparison.Ordinal) ||
            string.Equals(firstSegment, "localhost", StringComparison.OrdinalIgnoreCase);

        return hasRegistry
            ? (firstSegment, imageName[(firstSlash + 1)..])
            : (null, imageName);
    }
}
