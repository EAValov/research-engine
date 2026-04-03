using Microsoft.AspNetCore.Mvc;
using ResearchEngine.Domain;

namespace ResearchEngine.API;

public static class ResearchProtocolApi
{
    public static void MapResearchProtocolApi(this WebApplication app)
    {
        MapRoutes(app.MapGroup("/api/protocol")
            .WithTags("Research Protocol API")
            .RequireAuthorization());

        static void MapRoutes(RouteGroupBuilder api)
        {
            api.MapPost("/clarifications", GenerateClarificationsAsync)
                .Accepts<ProtocolClarificationsRequest>("application/json")
                .Produces<ProtocolClarificationsResponse>(StatusCodes.Status200OK)
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden);

            api.MapPost("/parameters", SelectParametersAsync)
                .Accepts<ProtocolParametersRequest>("application/json")
                .Produces<ProtocolParametersResponse>(StatusCodes.Status200OK)
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden);
        }
    }

    /// <summary>
    /// POST /api/protocol/clarifications
    /// Generates clarification questions for a research query.
    /// </summary>
    /// <param name="request">Clarification request payload.</param>
    /// <param name="protocolService">Protocol service.</param>
    /// <param name="ct">Request cancellation token.</param>
    private static async Task<IResult> GenerateClarificationsAsync(
        [FromBody] ProtocolClarificationsRequest request,
        IResearchProtocolService protocolService,
        CancellationToken ct)
    {
        IReadOnlyList<string> questions =
            await protocolService.GenerateFeedbackQueriesAsync(
                request.Query,
                ct);

        return Results.Ok(new ProtocolClarificationsResponse(questions));
    }

    /// <summary>
    /// POST /api/protocol/parameters
    /// Selects or auto-computes protocol parameters (breadth, depth, language, region).
    /// </summary>
    /// <param name="request">Protocol parameters request.</param>
    /// <param name="protocolService">Protocol service.</param>
    /// <param name="ct">Request cancellation token.</param>
    private static async Task<IResult> SelectParametersAsync(
        [FromBody] ProtocolParametersRequest request,
        IResearchProtocolService protocolService,
        CancellationToken ct)
    {
        var clarifications = request.Clarifications?.Select(c => new Clarification
        {
            Question = c.Question,
            Answer = c.Answer
        }).ToList() ?? [];

        int? breadth = null;
        int? depth = null;
        string? language = null;
        string? region = null;

        if (request.Overrides != null)
        {
            if (request.Overrides.TryGetValue("breadth", out var b) && b is int bi) breadth = bi;
            if (request.Overrides.TryGetValue("depth", out var d) && d is int di) depth = di;
            if (request.Overrides.TryGetValue("language", out var l) && l is string ls) language = ls;
            if (request.Overrides.TryGetValue("region", out var r) && r is string rs) region = rs;
        }

        if (!breadth.HasValue || !depth.HasValue)
            (breadth, depth) =
                await protocolService.AutoSelectBreadthDepthAsync(request.Query, clarifications, ct);

        if (string.IsNullOrEmpty(language))
            (language, region) =
                await protocolService.AutoSelectLanguageRegionAsync(request.Query, clarifications, ct);

        breadth = Math.Clamp(breadth ?? 2, 1, 8);
        depth = Math.Clamp(depth ?? 2, 1, 4);
        language = (language?.Length == 2 ? language.ToLowerInvariant() : "en");

        return Results.Ok(new ProtocolParametersResponse(
            Breadth: breadth.Value,
            Depth: depth.Value,
            Language: language,
            Region: region));
    }
}
