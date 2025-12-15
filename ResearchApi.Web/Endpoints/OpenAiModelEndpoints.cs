public static class OpenAiModelEndpoints
{
    public static void MapDeepResearchModel(this WebApplication app)
    {
        app.MapGet("/v1/models", HandleGetModels);
        app.MapPost("/v1/chat/completions", DeepResearchChatHandler.HandleChatCompletionsAsync);
    }

    private static IResult HandleGetModels()
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
                    id      = "Open Deep Research", 
                    @object = "model",
                    created = created,
                    owned_by = "local",
                    description = "Open Deep Research wrapper model"
                }
            }
        };

        return Results.Json(response);
    }
}
