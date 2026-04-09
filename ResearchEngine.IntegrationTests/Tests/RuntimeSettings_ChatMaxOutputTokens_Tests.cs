using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResearchEngine.API;
using ResearchEngine.Domain;
using ResearchEngine.IntegrationTests.Infrastructure;
using Xunit;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class RuntimeSettings_ChatMaxOutputTokens_Tests : IntegrationTestBase
{
    public RuntimeSettings_ChatMaxOutputTokens_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task UpdateRuntimeSettings_RoundTrips_ChatMaxOutputTokens()
    {
        await using var factory = CreateRuntimeSettingsValidationFactory();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();

        var runtimeSettingsRepository = scope.ServiceProvider.GetRequiredService<IRuntimeSettingsRepository>();
        var original = await runtimeSettingsRepository.GetCurrentAsync();

        try
        {
            var updateRequest = BuildRequest(original, maxOutputTokens: 2048);

            var putResponse = await client.PutAsJsonAsync("/api/settings/runtime", updateRequest);
            putResponse.EnsureSuccessStatusCode();

            var putJson = await putResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(2048, putJson.GetProperty("chatConfig").GetProperty("maxOutputTokens").GetInt32());

            var getResponse = await client.GetAsync("/api/settings/runtime");
            getResponse.EnsureSuccessStatusCode();

            var getJson = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(2048, getJson.GetProperty("chatConfig").GetProperty("maxOutputTokens").GetInt32());
        }
        finally
        {
            await runtimeSettingsRepository.UpdateAsync(original);
        }
    }

    [Fact]
    public async Task UpdateRuntimeSettings_WithNonPositiveChatMaxOutputTokens_Returns400()
    {
        await using var factory = CreateRuntimeSettingsValidationFactory();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();

        var runtimeSettingsRepository = scope.ServiceProvider.GetRequiredService<IRuntimeSettingsRepository>();
        var original = await runtimeSettingsRepository.GetCurrentAsync();
        var invalidRequest = BuildRequest(original, maxOutputTokens: 0);

        var response = await client.PutAsJsonAsync("/api/settings/runtime", invalidRequest);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var errors = json.GetProperty("errors");
        Assert.True(errors.TryGetProperty("ChatConfig.MaxOutputTokens", out var maxOutputTokensError));
        Assert.Contains("greater than zero", maxOutputTokensError[0].GetString(), StringComparison.OrdinalIgnoreCase);
    }

    private static UpdateRuntimeSettingsRequest BuildRequest(RuntimeSettingsSnapshot snapshot, int? maxOutputTokens)
        => new(
            snapshot.ResearchOrchestratorConfig,
            snapshot.LearningSimilarityOptions,
            new UpdateChatConfigRequest(
                snapshot.ChatConfig.Endpoint,
                snapshot.ChatConfig.ModelId,
                ApiKey: string.Empty,
                MaxContextLength: snapshot.ChatConfig.MaxContextLength,
                MaxOutputTokens: maxOutputTokens),
            new UpdateCrawlConfigRequest(
                snapshot.CrawlConfig.BaseUrl,
                ApiKey: string.Empty));

    private WebApplicationFactory<Program> CreateRuntimeSettingsValidationFactory()
        => Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHttpClientFactory>();
                services.AddSingleton<IHttpClientFactory, FakeRuntimeSettingsHttpClientFactory>();
            });
        });

    private sealed class FakeRuntimeSettingsHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(new FakeRuntimeSettingsHttpMessageHandler())
            {
                Timeout = TimeSpan.FromSeconds(8)
            };
    }

    private sealed class FakeRuntimeSettingsHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is null)
                throw new InvalidOperationException("RequestUri is required.");

            var path = request.RequestUri.AbsolutePath;

            if (request.Method == HttpMethod.Get &&
                path.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse("""{"data":[{"id":"nvidia/Qwen3-30B-A3B-NVFP4"}]}"""));
            }

            if (request.Method == HttpMethod.Post &&
                path.EndsWith("/tokenize", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse("""{"count":2,"maxModelLen":32768}"""));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage JsonResponse(string json)
            => new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
    }
}
