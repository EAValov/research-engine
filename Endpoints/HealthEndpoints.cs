
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ResearchApi.Configuration;
using ResearchApi.Domain;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this WebApplication app)
    {
         app.MapGet("/api/health", async (
            [FromServices] IOptions<LlmOptions> llmOptions,
            [FromServices] IOptions<FirecrawlOptions> firecrawlOptions,
            [FromServices] ILlmClient llmClient,
            [FromServices] ISearchClient searchClient,
            CancellationToken ct) =>
        {
            var llm = llmOptions.Value;
            var fc  = firecrawlOptions.Value;

            var llmHealth  = new DependencyHealth(false, "Not checked");
            var fcHealth   = new DependencyHealth(false, "Not checked");

            // 1. Check config presence
            if (string.IsNullOrWhiteSpace(llm.Endpoint) ||
                string.IsNullOrWhiteSpace(llm.Model))
            {
                llmHealth = new DependencyHealth(false, "LLM configuration is incomplete.");
            }
            else
            {
                // Optional: do a very cheap round-trip to LLM
                try
                {
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    linkedCts.CancelAfter(TimeSpan.FromSeconds(5));

                    var systemPrompt = "You are a health check for a research API.";
                    var userPrompt   = "Reply with a single word: OK.";

                    var reply = await llmClient.CompleteAsync(
                        systemPrompt,
                        userPrompt,
                        linkedCts.Token);

                    var ok = !string.IsNullOrWhiteSpace(reply)
                             && reply.Contains("OK", StringComparison.OrdinalIgnoreCase);

                    llmHealth = new DependencyHealth(ok,
                        ok ? "LLM responded successfully." : "LLM responded, but unexpected content.");
                }
                catch (Exception ex)
                {
                    llmHealth = new DependencyHealth(false, $"LLM check failed: {ex.Message}");
                }
            }

            if (string.IsNullOrWhiteSpace(fc.BaseUrl))
            {
                fcHealth = new DependencyHealth(false, "Firecrawl configuration is incomplete.");
            }
            else
            {
                // Optional: do a very cheap Firecrawl call
                try
                {
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    linkedCts.CancelAfter(TimeSpan.FromSeconds(5));

                    // A tiny search just to see if it responds at all.
                    var results = await searchClient.SearchAsync("health check", limit: 1, ct: linkedCts.Token);
                    var ok = results != null;

                    fcHealth = new DependencyHealth(ok,
                        ok ? "Firecrawl responded successfully." : "Firecrawl returned an empty response.");
                }
                catch (Exception ex)
                {
                    fcHealth = new DependencyHealth(false, $"Firecrawl check failed: {ex.Message}");
                }
            }

            var overallStatus = (llmHealth.IsHealthy && fcHealth.IsHealthy)
                ? "Healthy"
                : "Degraded";

            var response = new ResearchHealthResponse(
                Status: overallStatus,
                Llm: llmHealth,
                Firecrawl: fcHealth
            );

            // 200 OK for now; if you want strict semantics you can change to 503 when degraded
            return Results.Ok(response);
        });
    }
}