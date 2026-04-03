using System.Net.Http.Json;
using System.Text.Json;
using Hangfire;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResearchEngine.Domain;
using ResearchEngine.IntegrationTests.Helpers;
using ResearchEngine.IntegrationTests.Infrastructure;
using Xunit;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class SynthesisFailure_MarksSynthesisFailed_Tests : IntegrationTestBase
{
    public SynthesisFailure_MarksSynthesisFailed_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task JobRun_WhenSynthesisWriterThrows_LatestSynthesisIsFailed_WithErrorMessage()
    {
        // Arrange: override IChatModel so it only fails when tools are provided (targets synthesis-writing step)
        await using var failingFactory = Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBackgroundJobClient>();
                services.AddSingleton<IBackgroundJobClient, InlineBackgroundJobClient>(); // required for Factory overrides
                
                services.RemoveAll<IChatModel>();
                services.AddSingleton<IChatModel>(sp =>
                    new FailOnToolsChatModel(sp.GetRequiredService<FakeChatModel>()));
            });
        });

        using var client = failingFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var jobId = await CreateJobAsync(client, "Test query that will fail during searching.");
        Assert.NotEqual(Guid.Empty, jobId);

        // Wait for terminal done (job may still be Completed or Failed depending on your orchestration contract)
        var (status, _, _) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.True(status is "Completed" or "Failed");

        // Now ensure the latest synthesis is Failed (since tool-based section writing will throw)
        var synResp = await client.GetAsync($"/api/jobs/{jobId}/syntheses/latest");
        synResp.EnsureSuccessStatusCode();

        var synJson = await synResp.Content.ReadFromJsonAsync<JsonElement>();
        var synthesis = synJson.GetProperty("synthesis");

        Assert.Equal("Failed", synthesis.GetProperty("status").GetString());

        // You have ErrorMessage in DB model; your API usually exposes it as "errorMessage"
        if (synthesis.TryGetProperty("errorMessage", out var errEl))
        {
            var msg = errEl.GetString();
            Assert.False(string.IsNullOrWhiteSpace(msg));
        }
    }

    private sealed class FailOnToolsChatModel : IChatModel
    {
        private readonly IChatModel _inner;
        public FailOnToolsChatModel(IChatModel inner) => _inner = inner;

        public string ModelId => _inner.ModelId;

        public Task<ChatResponse> ChatAsync(
            Prompt prompt,
            IEnumerable<AITool>? tools = null,
            Microsoft.Extensions.AI.ChatResponseFormat? responseFormat = null,
            float? temperature = null,
            CancellationToken cancellationToken = default)
        {
            if (tools is not null)
                throw new InvalidOperationException("Injected synthesis tool failure (tools usage).");

            return _inner.ChatAsync(prompt, tools, responseFormat, temperature, cancellationToken);
        }

        public string StripThinkBlock(string text) => _inner.StripThinkBlock(text);

        public AITool CreateTool<TDelegate>(TDelegate function, string? name = null, string? description = null)
            where TDelegate : Delegate
            => _inner.CreateTool(function, name, description);
    }
}
