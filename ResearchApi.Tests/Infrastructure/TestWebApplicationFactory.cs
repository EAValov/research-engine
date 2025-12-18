using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ResearchApi.Domain;

namespace ResearchApi.Tests.Infrastructure;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Replace the real implementations with test doubles
            // We'll mock external services that are used by endpoints
            services.AddSingleton<IResearchJobStore, TestResearchJobStore>();
            services.AddSingleton<IWebhookSubscriptionStore, TestWebhookSubscriptionStore>();
            services.AddSingleton<IWebhookDispatcher, TestWebhookDispatcher>();
            services.AddSingleton<IResearchEventBus, TestResearchEventBus>();
            
            // Mock search and crawl clients with fake implementations
            services.AddSingleton<ISearchClient, FakeSearchClient>();
            services.AddSingleton<ICrawlClient, FakeCrawlClient>();
            
            // Mock external services that are used by the orchestrator
            services.AddSingleton<IChatModel, FakeChatModel>();
            services.AddSingleton<IEmbeddingModel, FakeEmbeddingModel>();
            services.AddSingleton<ITokenizer, FakeTokenizer>();
            services.AddSingleton<IResearchProtocolService, FakeResearchProtocolService>();
            services.AddSingleton<IQueryPlanningService, FakeQueryPlanningService>();
            services.AddSingleton<ILearningExtractionService, FakeLearningExtractionService>();
            services.AddSingleton<IReportSynthesisService, FakeReportSynthesisService>();
            services.AddSingleton<ILearningEmbeddingService, FakeLearningEmbeddingService>();
            services.AddSingleton<IResearchContentStore, FakeResearchContentStore>();
        });
    }
}

// Test doubles for external dependencies
public class TestResearchJobStore : IResearchJobStore
{
    public Task<int> AppendEventAsync(Guid jobId, ResearchEvent ev, CancellationToken ct = default)
    {
        return Task.FromResult(1);
    }

    public Task<ResearchJob> CreateJobAsync(string query, IEnumerable<Clarification> clarifications, int breadth, int depth, string language, string? region, CancellationToken ct = default)
    {
        var job = new ResearchJob
        {
            Id = Guid.NewGuid(),
            Query = query,
            Breadth = breadth,
            Depth = depth,
            Status = ResearchJobStatus.Pending,
            TargetLanguage = language,
            Region = region,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Clarifications = clarifications.ToList()
        };
        return Task.FromResult(job);
    }

    public Task<IReadOnlyList<ResearchEvent>> GetEventsAsync(Guid jobId, CancellationToken ct = default)
    {
        return Task.FromResult((IReadOnlyList<ResearchEvent>)Array.Empty<ResearchEvent>());
    }

    public Task<ResearchJob?> GetJobAsync(Guid id, CancellationToken ct = default)
    {
        return Task.FromResult<ResearchJob?>(null);
    }

    public Task<int> UpdateJobAsync(ResearchJob job, CancellationToken ct = default)
    {
        return Task.FromResult(1);
    }
}

public class TestWebhookSubscriptionStore : IWebhookSubscriptionStore
{
    public Task SaveAsync(WebhookSubscription sub, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public Task<WebhookSubscription?> GetAsync(Guid jobId, CancellationToken ct)
    {
        return Task.FromResult<WebhookSubscription?>(null);
    }

    public Task DeleteAsync(Guid jobId, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}

public class TestWebhookDispatcher : IWebhookDispatcher
{
    public Task EnqueueAsync(WebhookDeliveryRequest request, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}

public class TestResearchEventBus : IResearchEventBus
{
    public Task PublishAsync(Guid jobId, ResearchEvent ev, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public Task<IAsyncDisposable> SubscribeAsync(Guid jobId, Func<ResearchEvent, CancellationToken, Task> onEvent, CancellationToken ct)
    {
        // Return a dummy disposable
        return Task.FromResult<IAsyncDisposable>(new DummyDisposable());
    }
    
    private class DummyDisposable : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}

public class FakeSearchClient : ISearchClient
{
    public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int limit, string? location = null, CancellationToken ct = default)
    {
        // Return a fake result for testing
        var results = new List<SearchResult>
        {
            new SearchResult("https://example.com", "Example Title", "Example content")
        };
        return Task.FromResult((IReadOnlyList<SearchResult>)results);
    }
}

public class FakeCrawlClient : ICrawlClient
{
    public Task<string> FetchContentAsync(string url, CancellationToken ct = default)
    {
        // Return fake content for testing
        return Task.FromResult("This is fake content from " + url);
    }
}

public class FakeChatModel : IChatModel
{
    public string ModelId => "fake-model";

    public Task<ChatResponse> ChatAsync(Prompt prompt, IEnumerable<AITool>? tools = null, Microsoft.Extensions.AI.ChatResponseFormat? responseFormat = null, float? temperature = null, CancellationToken cancellationToken = default)
    {
        // Return a fake chat response
        var response = new ChatResponse(new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, "This is a fake response to: " + prompt.userPrompt));
        return Task.FromResult(response);
    }

    public string StripThinkBlock(string text)
    {
        return text;
    }

    public AITool CreateTool<TDelegate>(TDelegate function, string? name = null, string? description = null) where TDelegate : Delegate
    {
         return AIFunctionFactory.Create(
            function,
            name: name,
            description: description);
    }
}

public class FakeEmbeddingModel : IEmbeddingModel
{
    public string ModelId => "fake-embedding-model";

    public Task<IReadOnlyList<Embedding<float>>> GenerateEmbeddingsAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken = default)
    {
        // Return fake embeddings
        var embeddings = new List<Embedding<float>>();
        foreach (var input in inputs)
        {
            embeddings.Add(new Embedding<float>(new float[] { 0.1f, 0.2f, 0.3f }));
        }
        return Task.FromResult((IReadOnlyList<Embedding<float>>)embeddings);
    }

    public Task<Embedding<float>> GenerateEmbeddingAsync(string input, CancellationToken cancellationToken = default)
    {
        // Return a fake embedding
        return Task.FromResult(new Embedding<float>(new float[] { 0.1f, 0.2f, 0.3f }));
    }
}

public class FakeTokenizer : ITokenizer
{
    public Task<TokenizeResult> TokenizePromptAsync(Prompt prompt, CancellationToken cancellationToken = default)
    {
        // Return fake tokenization result
        return Task.FromResult(new TokenizeResult() {Count = 10, MaxModelLen = 1000 });
    }

    public void Dispose()
    {
        // Nothing to dispose
    }
}

public class FakeResearchProtocolService : IResearchProtocolService
{
    public Task<IReadOnlyList<string>> GenerateFeedbackQueriesAsync(string query, bool includeBreadthDepthQuestions, CancellationToken ct = default)
    {
        // Return fake feedback queries
        return Task.FromResult((IReadOnlyList<string>)new List<string> { "Fake question 1", "Fake question 2" });
    }

    public Task<(int breadth, int depth)> AutoSelectBreadthDepthAsync(string query, IReadOnlyList<Clarification> clarifications, CancellationToken ct = default)
    {
        // Return fake breadth and depth
        return Task.FromResult((3, 2));
    }

    public Task<(string language, string? region)> AutoSelectLanguageRegionAsync(string query, IReadOnlyList<Clarification> clarifications, CancellationToken ct = default)
    {
        // Return fake language and region
        return Task.FromResult(("en", (string?)("US")));
    }
}

public class FakeQueryPlanningService : IQueryPlanningService
{
    public Task<IReadOnlyList<string>> GenerateSerpQueriesAsync(string query, string clarificationsText, int depth, int breadth, string targetLanguage, CancellationToken ct = default)
    {
        // Return fake SERP queries
        return Task.FromResult((IReadOnlyList<string>)new List<string> { "Fake SERP query 1", "Fake SERP query 2" });
    }
}

public class FakeLearningExtractionService : ILearningExtractionService
{
    public Task<IReadOnlyList<ExtractedLearningItem>> ExtractLearningsAsync(string query, string clarificationsText, ScrapedPage page, string sourceUrl, string targetLanguage, CancellationToken ct = default)
    {
        // Return fake extracted learnings
        var learnings = new List<ExtractedLearningItem>
        {
            new ExtractedLearningItem { Text = "Fake learning 1", Importance = 0.8f },
            new ExtractedLearningItem { Text = "Fake learning 2", Importance = 0.6f }
        };
        return Task.FromResult((IReadOnlyList<ExtractedLearningItem>)learnings);
    }
}

public class FakeReportSynthesisService : IReportSynthesisService
{
    public Task<string> WriteFinalReportAsync(ResearchJob job, string clarificationsText, IEnumerable<Learning> learnings, CancellationToken ct)
    {
        // Return a fake report
        return Task.FromResult("This is a fake final report");
    }
}

public class FakeLearningEmbeddingService : ILearningEmbeddingService
{
    public Task<IReadOnlyList<Learning>> PopulateEmbeddingsAsync(IEnumerable<Learning> learnings, CancellationToken ct = default)
    {
        // Return the same learnings (no changes)
        return Task.FromResult((IReadOnlyList<Learning>)learnings.ToList());
    }

    public Task<IReadOnlyList<Learning>> GetSimilarLearningsAsync(string queryText, Guid? jobId = null, string? queryHash = null, string? language = null, string? region = null, int topK = 20, CancellationToken ct = default)
    {
        // Return fake similar learnings
        return Task.FromResult((IReadOnlyList<Learning>)new List<Learning>());
    }
}

public class FakeResearchContentStore : IResearchContentStore
{
    public Task<ScrapedPage> UpsertScrapedPageAsync(string url, string content, string? language, string? region, CancellationToken ct = default)
    {
        // Return a fake scraped page
        var page = new ScrapedPage
        {
            Id = Guid.NewGuid(),
            Url = url,
            Language = language,
            Region = region,
            Content = content,
            ContentHash = "fake-hash",
            CreatedAt = DateTimeOffset.UtcNow
        };
        return Task.FromResult(page);
    }

    public Task<IReadOnlyList<Learning>> GetLearningsForPageAndQueryAsync(Guid pageId, string queryHash, CancellationToken ct = default)
    {
        // Return fake learnings
        return Task.FromResult((IReadOnlyList<Learning>)new List<Learning>());
    }

    public Task AddLearningsAsync(Guid jobId, Guid pageId, IEnumerable<Learning> learnings, CancellationToken ct)
    {
        // Do nothing for test
        return Task.CompletedTask;
    }

    public string ComputeQueryHash(string query)
    {
        // Return a fake hash
        return "fake-query-hash";
    }
}
