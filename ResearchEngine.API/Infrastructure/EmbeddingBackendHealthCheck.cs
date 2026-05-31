using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using ResearchEngine.Configuration;

namespace ResearchEngine.Infrastructure;

public sealed class EmbeddingBackendHealthCheck(
    IOptions<EmbeddingConfig> embeddingOptions,
    IHttpClientFactory httpClientFactory)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var endpoint = embeddingOptions.Value.Endpoint.TrimEnd('/');
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(8);

            using var response = await client.GetAsync($"{endpoint}/models", cancellationToken);
            if (response.IsSuccessStatusCode)
                return HealthCheckResult.Healthy();

            return new HealthCheckResult(
                context.Registration.FailureStatus,
                description: $"Embedding backend /models returned HTTP {(int)response.StatusCode} {response.StatusCode}.");
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                description: "Embedding backend health check failed.",
                exception: ex);
        }
    }
}
