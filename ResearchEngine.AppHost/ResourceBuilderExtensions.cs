using Aspire.Hosting.ApplicationModel;

static class ResourceBuilderExtensions
{
    public static IResourceBuilder<T> WithOptionalImageSHA256<T>(
        this IResourceBuilder<T> resource,
        string? sha256)
        where T : ContainerResource
    {
        if (!string.IsNullOrWhiteSpace(sha256))
        {
            resource.WithImageSHA256(sha256);
        }

        return resource;
    }

    public static IResourceBuilder<T> WithImageReference<T>(
        this IResourceBuilder<T> resource,
        ContainerImageReference image)
        where T : ContainerResource
    {
        resource.WithImage(image.Repository, image.Tag);
        if (!string.IsNullOrWhiteSpace(image.Registry))
        {
            resource.WithImageRegistry(image.Registry);
        }

        resource.WithOptionalImageSHA256(image.Sha256);
        return resource;
    }
}
