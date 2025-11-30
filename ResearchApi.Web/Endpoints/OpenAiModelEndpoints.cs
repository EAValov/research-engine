public static class OpenAiModelEndpoints
{
    public static void MapDeepResearchModel(this WebApplication app)
    {
        app.MapGet("/v1/models", OpenAiModelListEndpoint.HandleGetModels);
        app.MapPost("/v1/chat/completions", DeepResearchChatHandler.HandleChatCompletionsAsync);
    }
}
