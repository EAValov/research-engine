using Microsoft.AspNetCore.Mvc;
using ResearchApi.Domain;

public static class ResearchProtocolApi
{
    public static void MapResearchProtocolApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/research/protocol")
            .WithTags("Research Protocol API")
            .RequireAuthorization();

        api.MapPost("/clarifications", GenerateClarificationsAsync);
        api.MapPost("/parameters", SelectParametersAsync);
    }

    private static async Task<IResult> GenerateClarificationsAsync(
        [FromBody] ProtocolClarificationsRequest request,
        IResearchProtocolService protocolService,
        CancellationToken ct)
    {
        var questions = await protocolService.GenerateFeedbackQueriesAsync(request.Query, request.IncludeConfigureQuestions, ct);
        
        var response = new
        {
            questions
        };

        return Results.Ok(response);
    }

    private static async Task<IResult> SelectParametersAsync(
        [FromBody] ProtocolParametersRequest request,
        IResearchProtocolService protocolService,
        CancellationToken ct)
    {
        // Convert DTOs to domain models for the service
        var clarifications = request.Clarifications?.Select(c => new Clarification 
        { 
            Question = c.Question, 
            Answer = c.Answer 
        }).ToList() ?? [];

        // Apply overrides if provided
        int? breadth = null;
        int? depth = null;
        string? language = null;
        string? region = null;

        if (request.Overrides != null)
        {
            if (request.Overrides.TryGetValue("breadth", out var breadthValue) && breadthValue is int b)
                breadth = b;
            
            if (request.Overrides.TryGetValue("depth", out var depthValue) && depthValue is int d)
                depth = d;
                
            if (request.Overrides.TryGetValue("language", out var languageValue) && languageValue is string l)
                language = l;
                
            if (request.Overrides.TryGetValue("region", out var regionValue) && regionValue is string r)
                region = r;
        }

        // If overrides don't provide all values, compute them using the protocol service
        if (!breadth.HasValue || !depth.HasValue || string.IsNullOrEmpty(language))
        {
            if (!breadth.HasValue || !depth.HasValue)
            {
                (breadth, depth) = await protocolService.AutoSelectBreadthDepthAsync(request.Query, clarifications, ct);
            }

            if (string.IsNullOrEmpty(language))
            {
                (language, region) = await protocolService.AutoSelectLanguageRegionAsync(request.Query, clarifications, ct);
            }
        }

        // Clamp values as required by specification
        breadth = breadth.HasValue ? Math.Clamp(breadth.Value, 1, 8) : null;
        depth = depth.HasValue ? Math.Clamp(depth.Value, 1, 4) : null;
        
        // Normalize language to 2-letter lowercase
        if (!string.IsNullOrEmpty(language))
        {
            if (language.Length != 2)
            {
                language = "en"; // fallback
            }
            else
            {
                language = language.ToLowerInvariant();
            }
        }

        var response = new
        {
            breadth = breadth ?? 2,
            depth = depth ?? 2,
            language = language ?? "en",
            region = string.IsNullOrEmpty(region) ? null : region
        };

        return Results.Ok(response);
    }
}
