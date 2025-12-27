
using System.Text.Json;
using Microsoft.Extensions.AI;
using ResearchApi.Domain;

namespace ResearchApi.IntegrationTests.Helpers;

public static class GenericHelpers
{
    public static string GetStageName(JsonElement el)
    {
        // If el is already the stage value (number/string), interpret it directly.
        if (el.ValueKind == JsonValueKind.Number)
            return ((ResearchEventStage)el.GetInt32()).ToString();

        if (el.ValueKind == JsonValueKind.String)
            return el.GetString()!;

        // Otherwise expect an event object with a "stage" property.
        if (el.ValueKind == JsonValueKind.Object)
        {
            var stageEl = el.GetProperty("stage");
            return stageEl.ValueKind switch
            {
                JsonValueKind.String => stageEl.GetString()!,
                JsonValueKind.Number => ((ResearchEventStage)stageEl.GetInt32()).ToString(),
                _ => throw new InvalidOperationException($"Unexpected JSON kind for stage: {stageEl.ValueKind}")
            };
        }

        throw new InvalidOperationException($"Unexpected JSON kind for event/stage: {el.ValueKind}");
    }
}