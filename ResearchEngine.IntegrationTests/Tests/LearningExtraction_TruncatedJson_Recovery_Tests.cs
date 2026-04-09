using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResearchEngine.Domain;
using ResearchEngine.IntegrationTests.Infrastructure;
using Xunit;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class LearningExtraction_TruncatedJson_Recovery_Tests : IntegrationTestBase
{
    public LearningExtraction_TruncatedJson_Recovery_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task ExtractAndSaveLearningsAsync_WhenExtractionJsonIsTruncated_SplitsAndRecovers()
    {
        await using var factory = Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IChatModel>();
                services.AddSingleton<IChatModel>(sp =>
                    new TruncatingLearningExtractionChatModel(sp.GetRequiredService<FakeChatModel>()));
            });
        });

        using var scope = factory.Services.CreateScope();

        var jobRepository = scope.ServiceProvider.GetRequiredService<IResearchJobRepository>();
        var sourceRepository = scope.ServiceProvider.GetRequiredService<IResearchSourceRepository>();
        var learningRepository = scope.ServiceProvider.GetRequiredService<IResearchLearningRepository>();
        var learningIntelService = scope.ServiceProvider.GetRequiredService<ILearningIntelService>();

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
            reference: "https://example.test/truncated-learning-json",
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

        var stored = await learningRepository.GetLearningsForSourceAndQueryAsync(
            source.Id,
            "Bitcoin adoption in digital payments",
            default);

        Assert.NotEmpty(stored);
    }

    private static string BuildLongContent()
    {
        return string.Join(
            "\n\n",
            Enumerable.Range(1, 120).Select(i =>
                $"Section {i}. Bitcoin adoption in digital payments remains limited in mainstream commerce, while merchants prioritize stable payment rails, fraud reduction, regulatory clarity, and predictable settlement costs. Data point {i}: {i + 5}% of surveyed merchants cited volatility as a blocker."));
    }

    private sealed class TruncatingLearningExtractionChatModel(IChatModel inner) : IChatModel
    {
        private int _remainingTruncatedResponses = 1;

        public string ModelId => inner.ModelId;

        public Task<ChatResponse> ChatAsync(
            Prompt prompt,
            IEnumerable<AITool>? tools = null,
            ChatResponseFormat? responseFormat = null,
            float? temperature = null,
            CancellationToken cancellationToken = default)
        {
            if (LooksLikeLearningExtraction(prompt) &&
                prompt.userPrompt.Length > 3500 &&
                Interlocked.CompareExchange(ref _remainingTruncatedResponses, 0, 1) == 1)
            {
                var response = new ChatResponse(new[]
                {
                    new ChatMessage(ChatRole.Assistant, "{\"learnings\":[{\"text\":\"Bitcoin adoption in consumer payments remains niche")
                })
                {
                    FinishReason = ChatFinishReason.Length
                };

                return Task.FromResult(response);
            }

            return inner.ChatAsync(prompt, tools, responseFormat, temperature, cancellationToken);
        }

        public string StripThinkBlock(string text) => inner.StripThinkBlock(text);

        public AITool CreateTool<TDelegate>(TDelegate function, string? name = null, string? description = null)
            where TDelegate : Delegate
            => inner.CreateTool(function, name, description);

        private static bool LooksLikeLearningExtraction(Prompt prompt)
            => prompt.userPrompt.Contains("<contents>", StringComparison.Ordinal) &&
               prompt.userPrompt.Contains("learning", StringComparison.OrdinalIgnoreCase);
    }
}
