using ResearchEngine.Domain;

namespace ResearchEngine.Web;

public static partial class ResearchApi
{
    public static void MapResearchApi(this WebApplication app)
    {
        MapRoutes(app.MapGroup("/api")
            .WithTags("Research API")
            .RequireAuthorization());

        MapRoutes(app.MapGroup("/api/research")
            .WithTags("Research Jobs API")
            .RequireAuthorization()
            .ExcludeFromDescription());
    }

    private static void MapRoutes(RouteGroupBuilder api)
    {
        // Jobs
        api.MapGet("/jobs", ListJobsAsync)
            .Produces<ListResearchJobsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        api.MapGet("/jobs/{jobId:guid}", GetJobAsync)
            .Produces<GetResearchJobResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        api.MapPost("/jobs", CreateJobAsync)
            .Accepts<CreateResearchJobRequest>("application/json")
            .Produces<CreateResearchJobResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        api.MapPost("/jobs/{jobId:guid}/cancel", CancelJobAsync)
            .Accepts<CancelJobRequest>("application/json")
            .Produces<CancelJobResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        api.MapDelete("/jobs/{jobId:guid}", SoftDeleteJobAsync)
            .Accepts<DeleteJobRequest>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // Evidence
        api.MapGet("/jobs/{jobId:guid}/sources", ListSourcesAsync)
            .Produces<ListSourcesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        api.MapDelete("/jobs/{jobId:guid}/sources/{sourceId:guid}", SoftDeleteSourceAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        api.MapGet("/jobs/{jobId:guid}/learnings", ListLearningsAsync)
            .Produces<ListLearningsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        api.MapPost("/jobs/{jobId:guid}/learnings", AddLearningAsync)
            .Accepts<AddLearningRequest>("application/json")
            .Produces<AddLearningResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        api.MapDelete("/jobs/{jobId:guid}/learnings/{learningId:guid}", SoftDeleteLearningAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        api.MapGet("/learnings/{learningId:guid}/group", GetLearningGroupByLearningIdAsync)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        api.MapPost("/learnings/groups/resolve", ResolveLearningGroupsBatchAsync)
            .Accepts<BatchResolveLearningGroupsRequest>("application/json")
            .Produces<BatchResolveLearningGroupsResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // Syntheses
        api.MapGet("/jobs/{jobId:guid}/syntheses", ListSynthesesAsync)
            .Produces<ListSynthesesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        api.MapGet("/jobs/{jobId:guid}/syntheses/latest", GetLatestSynthesisAsync)
            .Produces<LatestSynthesisResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        api.MapGet("/syntheses/{synthesisId:guid}", GetSynthesisAsync)
            .Produces<SynthesisDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        api.MapDelete("/syntheses/{synthesisId:guid}", DeleteSynthesisAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        api.MapPost("/jobs/{jobId:guid}/syntheses", CreateSynthesisAsync)
            .Accepts<StartSynthesisRequest>("application/json")
            .Produces<CreateSynthesisResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        api.MapPost("/syntheses/{synthesisId:guid}/run", RunSynthesisAsync)
            .Produces<RunSynthesisTerminalResponse>(StatusCodes.Status200OK)
            .Produces<RunSynthesisAcceptedResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        api.MapPut("/syntheses/{synthesisId:guid}/overrides/sources", UpsertSynthesisSourceOverridesAsync)
            .Accepts<IReadOnlyList<SynthesisSourceOverrideDto>>("application/json")
            .Produces<UpsertOverridesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        api.MapPut("/syntheses/{synthesisId:guid}/overrides/learnings", UpsertSynthesisLearningOverridesAsync)
            .Accepts<IReadOnlyList<SynthesisLearningOverrideDto>>("application/json")
            .Produces<UpsertOverridesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // Events
        api.MapGet("/jobs/{jobId:guid}/events", ListEventsAsync)
            .Produces<IReadOnlyList<ResearchEventDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        api.MapPost("/jobs/{jobId:guid}/events/stream-token", CreateEventsStreamTokenAsync)
            .Produces<CreateSseTokenResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        api.MapGet("/jobs/{jobId:guid}/events/stream", StreamEventsAsync)
            .AllowAnonymous()
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
