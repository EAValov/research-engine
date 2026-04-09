using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResearchEngine.Configuration;
using ResearchEngine.Domain;
using ResearchEngine.IntegrationTests.Infrastructure;
using Xunit;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class LearningExtraction_OutputHeadroom_Tests : IntegrationTestBase
{
    public LearningExtraction_OutputHeadroom_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task ExtractAndSaveLearningsAsync_ReservesOutputHeadroom_BeforeCallingChat()
    {
        await using var factory = Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ITokenizer>();
                services.RemoveAll<IChatModel>();

                services.AddSingleton<ITokenizer, HeadroomAwareTokenizer>();
                services.AddSingleton<IChatModel>(sp =>
                    new RejectOversizedExtractionPromptChatModel(sp.GetRequiredService<FakeChatModel>()));
            });
        });

        using var scope = factory.Services.CreateScope();

        var runtimeSettingsRepository = scope.ServiceProvider.GetRequiredService<IRuntimeSettingsRepository>();
        var jobRepository = scope.ServiceProvider.GetRequiredService<IResearchJobRepository>();
        var sourceRepository = scope.ServiceProvider.GetRequiredService<IResearchSourceRepository>();
        var learningIntelService = scope.ServiceProvider.GetRequiredService<ILearningIntelService>();

        var original = await runtimeSettingsRepository.GetCurrentAsync();
        await runtimeSettingsRepository.UpdateAsync(new RuntimeSettingsSnapshot(
            original.ResearchOrchestratorConfig,
            original.LearningSimilarityOptions,
            new ChatConfig
            {
                Endpoint = original.ChatConfig.Endpoint,
                ApiKey = original.ChatConfig.ApiKey,
                ModelId = original.ChatConfig.ModelId,
                MaxContextLength = 10_240,
                MaxOutputTokens = 3_072
            },
            original.CrawlConfig));

        try
        {
            var job = await jobRepository.CreateJobAsync(
                query: "Bitcoin adoption in digital payments",
                clarifications: Array.Empty<Clarification>(),
                breadth: 1,
                depth: 1,
                discoveryMode: SourceDiscoveryMode.Balanced,
                language: "en",
                region: null,
                ct: default);

            var longContent = BuildLongContent();
            var source = await sourceRepository.UpsertSourceAsync(
                jobId: job.Id,
                reference: "https://example.test/output-headroom",
                content: longContent,
                title: null,
                language: "en",
                region: null,
                kind: SourceKind.Web,
                ct: default);

            var extracted = await learningIntelService.ExtractAndSaveLearningsAsync(
                jobId: job.Id,
                sourceId: source.Id,
                query: "Bitcoin adoption in digital payments",
                clarificationsText: "",
                sourceUrl: source.Reference,
                sourceContent: longContent,
                targetLanguage: "en",
                computeEmbeddings: true,
                ct: default);

            Assert.NotEmpty(extracted);
        }
        finally
        {
            await runtimeSettingsRepository.UpdateAsync(original);
        }
    }

    private static string BuildLongContent()
    {
        return string.Join(
            "\n\n",
            Enumerable.Range(1, 160).Select(i =>
                $"Section {i}. Bitcoin adoption in digital payments remains limited in mainstream commerce, while merchants prioritize stable payment rails, predictable settlement costs, fraud controls, interoperability, and regulatory clarity. Observation {i}: {10 + i}% of surveyed merchants cited volatility as a blocker to wider cryptocurrency payment acceptance."));
    }

    private sealed class HeadroomAwareTokenizer : ITokenizer
    {
        public Task<TokenizeResult> TokenizePromptAsync(Prompt prompt, CancellationToken cancellationToken = default)
        {
            var totalChars = (prompt.systemPrompt?.Length ?? 0) + (prompt.userPrompt?.Length ?? 0);

            return Task.FromResult(new TokenizeResult
            {
                Count = Math.Max(1, totalChars / 3),
                MaxModelLen = 10_240
            });
        }
    }

    private sealed class RejectOversizedExtractionPromptChatModel(IChatModel inner) : IChatModel
    {
        public string ModelId => inner.ModelId;

        public Task<ChatResponse> ChatAsync(
            Prompt prompt,
            IEnumerable<AITool>? tools = null,
            ChatResponseFormat? responseFormat = null,
            float? temperature = null,
            CancellationToken cancellationToken = default)
        {
            if (LooksLikeLearningExtraction(prompt) && (prompt.userPrompt?.Length ?? 0) > 20_000)
                throw new InvalidOperationException("Learning extraction prompt should have been split before calling the chat model.");

            return inner.ChatAsync(prompt, tools, responseFormat, temperature, cancellationToken);
        }

        public string StripThinkBlock(string text) => inner.StripThinkBlock(text);

        public AITool CreateTool<TDelegate>(TDelegate function, string? name = null, string? description = null)
            where TDelegate : Delegate
            => inner.CreateTool(function, name, description);

        private static bool LooksLikeLearningExtraction(Prompt prompt)
            => prompt.userPrompt?.Contains("<contents>", StringComparison.Ordinal) == true &&
               prompt.userPrompt.Contains("learning", StringComparison.OrdinalIgnoreCase);
    }
}
