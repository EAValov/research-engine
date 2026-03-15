using System.Net.Http.Headers;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ResearchEngine.Domain;

namespace ResearchEngine.Infrastructure;

public sealed class ChatBackendHealthCheck(
    IRuntimeSettingsRepository runtimeSettingsRepository,
    IHttpClientFactory httpClientFactory)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = await runtimeSettingsRepository.GetCurrentAsync(cancellationToken);
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(8);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                OpenAiEndpointUri.AppendV1Path(settings.ChatConfig.Endpoint, "models"));

            if (!string.IsNullOrWhiteSpace(settings.ChatConfig.ApiKey))
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", settings.ChatConfig.ApiKey);
            }

            using var response = await client.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
                return HealthCheckResult.Healthy();

            return new HealthCheckResult(
                context.Registration.FailureStatus,
                description: $"Chat backend /models returned HTTP {(int)response.StatusCode} {response.StatusCode}.");
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                description: "Chat backend health check failed.",
                exception: ex);
        }
    }
}
