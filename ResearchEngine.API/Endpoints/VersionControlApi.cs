namespace ResearchEngine.API;

public interface IReleaseUpdateService
{
    Task<UpdateStatusResponse> GetStatusAsync(CancellationToken ct = default);
}

public static class VersionControlApi
{
    public static void MapVersionControlApi(this WebApplication app)
    {
        app.MapGet("/version-control/update-status", GetUpdateStatusAsync)
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
