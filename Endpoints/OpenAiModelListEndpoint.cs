using Microsoft.AspNetCore.Http.HttpResults;
using ResearchApi.Domain;

public static class OpenAiModelListEndpoint
{
    public static IResult HandleGetModels()
    {
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var response = new
        {
            // OpenAI-style list wrapper
            @object = "list",
            data = new[]
            {
                new
                {
                    id      = "local-deep-research", 
                    @object = "model",
                    created = created,
                    owned_by = "local",
                    description = "Local Deep Research wrapper model"
                }
            }
        };

        return Results.Json(response);
    }
}
