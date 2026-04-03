namespace ResearchEngine.API;

public static class AppMetaApi
{
    public static void MapAppMetaApi(this WebApplication app)
    {
        app.MapGet("/meta/update-status", GetUpdateStatusAsync)
            .AllowAnonymous()
            .ExcludeFromDescription()
            .Produces<UpdateStatusResponse>(StatusCodes.Status200OK);
    }

    private static async Task<IResult> GetUpdateStatusAsync(
        IReleaseUpdateService releaseUpdateService,
        CancellationToken ct)
    {
        var status = await releaseUpdateService.GetStatusAsync(ct);
        return Results.Ok(status);
    }
}
