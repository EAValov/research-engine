using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using ResearchEngine.Domain;

namespace ResearchEngine.IntegrationTests.Infrastructure;

public sealed class FakeSearchClient : ISearchClient
{
    public Task<IReadOnlyList<SearchResult>> SearchAsync(SearchRequest request, CancellationToken ct = default)
    {
        // Deterministic: always return 2 results.
        IReadOnlyList<SearchResult> results =
        [
            new("https://example.test/a", "Example A", "snippet a", Domain: "example.test", Position: 1),
            new("https://example.test/b", "Example B", "snippet b", Domain: "example.test", Position: 2)
        ];
        return Task.FromResult(results);
    }
}

public sealed class FakeCrawlClient : ICrawlClient
{
    public Task<string> FetchContentAsync(string url, CancellationToken ct = default)
    {
        // Deterministic “page content”
        var content = url switch
        {
            "https://example.test/a" => "Alpha content about the topic. Key fact: A.",
            "https://example.test/b" => "Beta content about the topic. Key fact: B.",
            _ => "Generic content."
        };

        return Task.FromResult(content);
    }
}

/// <summary>
/// Embeddings must match EmbeddingConfig.Dimension (1024).
/// We'll generate deterministic pseudo-vectors from the input string.
/// </summary>
public sealed class FakeEmbeddingModel : IEmbeddingModel
{
    public string ModelId => "fake-embedding-1024";

    public Task<Embedding<float>> GenerateEmbeddingAsync(string input, CancellationToken cancellationToken = default)
        => Task.FromResult(new Embedding<float>(MakeVector(input)));

    public Task<IReadOnlyList<Embedding<float>>> GenerateEmbeddingsAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Embedding<float>>>(inputs.Select(i => new Embedding<float>(MakeVector(i))).ToList());

    private static float[] MakeVector(string input)
    {
        const int dim = 1024;
        var bytes = Encoding.UTF8.GetBytes(input ?? string.Empty);

        // A simple deterministic hash expansion into dim floats in [-1..1]
        var vec = new float[dim];
        uint h = 2166136261;
        for (int i = 0; i < bytes.Length; i++)
            h = (h ^ bytes[i]) * 16777619;

        for (int i = 0; i < dim; i++)
        {
            h = (h ^ (uint)i) * 16777619;
            // map to [-1..1]
            vec[i] = ((h % 20001) - 10000) / 10000f;
        }

        return vec;
    }
}

public sealed class FakeTokenizer : ITokenizer
{
    public Task<TokenizeResult> TokenizePromptAsync(Prompt prompt, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TokenizeResult
        {
            Count = 100,
            MaxModelLen = 8000,
            Tokens = null
        });
    }
}

/// <summary>
/// Fake chat model that can satisfy your protocol + planning + section writing prompts.
/// If your production code relies on responseFormat JSON schema, return valid JSON
/// in ChatResponse.Text.
/// </summary>
public sealed class FakeChatModel : IChatModel
{
    public string ModelId => "fake-chat";

    // Store created tools so ChatAsync can call them
    private readonly Dictionary<string, Func<string, CancellationToken, Task<string>>> _tools =
        new(StringComparer.OrdinalIgnoreCase);

    public Task<ChatResponse> ChatAsync(
        Prompt prompt,
        IEnumerable<AITool>? tools = null,
        ChatResponseFormat? responseFormat = null,
        float? temperature = null,
        CancellationToken cancellationToken = default)
    {
        var user = prompt.userPrompt ?? string.Empty;
        var sys  = prompt.systemPrompt ?? string.Empty;

        // Register tools passed for this call (if any)
        if (tools is not null)
            RegisterTools(tools);

        // Routing (same idea as before)
        string text;

        var schemaRaw = TryGetSchemaRaw(responseFormat);
        if (!string.IsNullOrWhiteSpace(schemaRaw))
        {
            // LearningExtractionResponse
            if (SchemaContains(schemaRaw, "\"learnings\"", "\"importance\"", "\"text\""))
                return Task.FromResult(MakeResponse(MakeLearningExtractionJson(count: 5)));

            // SectionPlanningResponse
            if (SchemaContains(schemaRaw, "\"sections\"", "\"isConclusion\"", "\"index\"", "\"title\"", "\"description\""))
                return Task.FromResult(MakeResponse(MakeSectionPlanningJson()));

            // SerpQueryPlan
            if (SchemaContains(schemaRaw, "\"queries\"") && !schemaRaw.Contains("\"breadth\"", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(MakeResponse(MakeSerpQueryPlanJson()));

            // BreadthDepthSelection
            if (SchemaContains(schemaRaw, "\"breadth\"", "\"depth\""))
                return Task.FromResult(MakeResponse(MakeBreadthDepthJson()));

            // LanguageRegionSelection
            if (SchemaContains(schemaRaw, "\"language\"", "\"region\""))
                return Task.FromResult(MakeResponse(MakeLanguageRegionJson()));

            // DiscoveryModeSelection
            if (SchemaContains(schemaRaw, "\"discoveryMode\""))
                return Task.FromResult(MakeResponse(MakeDiscoveryModeJson()));

            // ClarificationQuestionsResponse
            if (SchemaContains(schemaRaw, "\"queries\"") && schemaRaw.Contains("clarification", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(MakeResponse(MakeClarificationsJson()));

            // If unknown schema: return something valid-ish instead of plain text
            return Task.FromResult(MakeResponse("{}"));
        }

        // JSON-schema-ish responses for protocol/planning/extraction
        if (sys.Contains("breadth", StringComparison.OrdinalIgnoreCase) && sys.Contains("depth", StringComparison.OrdinalIgnoreCase))
        {
            text = """{"breadth":2,"depth":2}""";
            return Task.FromResult(MakeResponse(text));
        }

        if (sys.Contains("ISO 639-1", StringComparison.OrdinalIgnoreCase) ||
            (sys.Contains("language", StringComparison.OrdinalIgnoreCase) && sys.Contains("region", StringComparison.OrdinalIgnoreCase)))
        {
            text = """{"language":"en","region":null}""";
            return Task.FromResult(MakeResponse(text));
        }

        if (sys.Contains("clarification", StringComparison.OrdinalIgnoreCase))
        {
            text = """{"queries":["What exactly should the report optimize for (speed vs depth)?","Any constraints on sources (academic only, recency, region)?"]}""";
            return Task.FromResult(MakeResponse(text));
        }

        if (sys.Contains("source discovery mode", StringComparison.OrdinalIgnoreCase)
            || sys.Contains("ReliableOnly", StringComparison.OrdinalIgnoreCase))
        {
            text = """{"discoveryMode":"Balanced"}""";
            return Task.FromResult(MakeResponse(text));
        }

        if (sys.Contains("SERP", StringComparison.OrdinalIgnoreCase) || user.Contains("serp", StringComparison.OrdinalIgnoreCase))
        {
            text = """{"queries":["overview of topic","recent developments topic","key risks and limitations topic"]}""";
            return Task.FromResult(MakeResponse(text));
        }

        if (sys.Contains("importance", StringComparison.OrdinalIgnoreCase) && sys.Contains("learnings", StringComparison.OrdinalIgnoreCase))
        {
            text = """
            {
              "learnings": [
                { "text": "Key learning A.", "statementType": "Finding", "importance": 0.9 },
                { "text": "Key learning B.", "statementType": "Commentary", "importance": 0.7 }
              ]
            }
            """;
            return Task.FromResult(MakeResponse(text));
        }

        if (sys.Contains("planned sections", StringComparison.OrdinalIgnoreCase) || sys.Contains("section planning", StringComparison.OrdinalIgnoreCase))
        {
            text = """
            {
              "sections": [
                { "index": 1, "title": "Background", "description": "Context and definitions.", "isConclusion": false },
                { "index": 2, "title": "Findings", "description": "Key findings with evidence.", "isConclusion": false },
                { "index": 3, "title": "Conclusion", "description": "Summary and next steps.", "isConclusion": true }
              ]
            }
            """;
            return Task.FromResult(MakeResponse(text));
        }

        // --- Section writing / synthesis writing path ---
        // If get_similar_learnings exists, call it once to get citations.
        // We don’t need a true “tool call” protocol; we just call the delegate and stitch a plausible answer.
        if (_tools.TryGetValue("get_similar_learnings", out var toolFn))
        {
            return ToolBackedSectionAsync(toolFn, cancellationToken);
        }

        // Fallback: still produce something that resembles a section body
        text = "This is a test section body.\n";
        return Task.FromResult(MakeResponse(text));
    }

    public string StripThinkBlock(string text) => text;

    public AITool CreateTool<TDelegate>(TDelegate function, string? name = null, string? description = null)
        where TDelegate : Delegate
    {
        if (function is null) throw new ArgumentNullException(nameof(function));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Tool name is required.", nameof(name));

        // We implement a minimal “tool wrapper” that captures the delegate.
        // We only support the signature: (string queryText, CancellationToken ct) => Task<T>
        // and we serialize T to JSON for consumption inside FakeChatModel.
        _tools[name] = async (queryText, ct) =>
        {
            object? result = await InvokeToolDelegateAsync(function, queryText, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        };

        // Return *some* AITool instance. The actual type varies by Microsoft.Extensions.AI version.
        // We don't rely on it being executed by the library; we execute via our captured delegate above.
        
          return AIFunctionFactory.Create(_tools[name], name, description ?? $"Fake tool {name}");
    }

    // ---------------- internals ----------------

    private void RegisterTools(IEnumerable<AITool> tools)
    {
        // If you pass pre-created tools, we can’t introspect their delegates reliably across versions.
        // But in your pipeline you create them via this FakeChatModel.CreateTool, so _tools is already populated.
        // This method is here just in case you later pass tools created elsewhere.
        _ = tools;
    }

    private static async Task<object?> InvokeToolDelegateAsync(Delegate del, string queryText, CancellationToken ct)
    {
        var method = del.Method;
        var ps = method.GetParameters();

        object?[] args;

        if (ps.Length == 2 &&
            ps[0].ParameterType == typeof(string) &&
            ps[1].ParameterType == typeof(CancellationToken))
        {
            args = new object?[] { queryText, ct };
        }
        else if (ps.Length == 1 && ps[0].ParameterType == typeof(string))
        {
            args = new object?[] { queryText };
        }
        else
        {
            throw new NotSupportedException($"Tool delegate signature not supported: {method}");
        }

        var invokeResult = del.DynamicInvoke(args);

        // Handle Task / Task<T>
        if (invokeResult is Task t)
        {
            await t.ConfigureAwait(false);

            var taskType = t.GetType();
            if (taskType.IsGenericType)
            {
                // Task<T>.Result
                return taskType.GetProperty("Result", BindingFlags.Public | BindingFlags.Instance)!.GetValue(t);
            }

            return null;
        }

        return invokeResult;
    }

    private static ChatResponse MakeResponse(string text)
        => new ChatResponse(new[] { new ChatMessage(ChatRole.Assistant, text) });

    private static async Task<ChatResponse> ToolBackedSectionAsync(
        Func<string, CancellationToken, Task<string>> toolFn,
        CancellationToken ct)
    {
        // Call retrieval tool
        var json = await toolFn("test query", ct).ConfigureAwait(false);

        // Extract the first learning's text (already contains [lrn:...] appended by your handler)
        string? firstLearningText = null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("learnings", out var arr) &&
                arr.ValueKind == JsonValueKind.Array &&
                arr.GetArrayLength() > 0)
            {
                var item = arr[0];
                if (item.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                    firstLearningText = t.GetString();
            }
        }
        catch
        {
            // ignore parsing problems; still produce section text
        }

        var body = "This is a synthesized section body based on retrieved evidence.\n\n";
        if (!string.IsNullOrWhiteSpace(firstLearningText))
            body += $"- Evidence: {firstLearningText}\n";
        else
            body += "- Evidence: [lrn:0123456789abcdef0123456789abcdef]\n";

        return MakeResponse(body);
    }

    private static bool SchemaContains(string schemaRaw, params string[] needles)
    {
        foreach (var n in needles)
            if (!schemaRaw.Contains(n, StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }

    private static string MakeSectionPlanningJson()
    {
        // Must match SectionPlanningResponse: { "sections": [ { index,title,description,isConclusion }, ... ] }
        return """
        {
        "sections": [
            { "index": 1, "title": "Background", "description": "Context and definitions.", "isConclusion": false },
            { "index": 2, "title": "Key Findings", "description": "Most important evidence-backed points.", "isConclusion": false },
            { "index": 3, "title": "Conclusion", "description": "Summary and next steps.", "isConclusion": true }
        ]
        }
        """;
    }

    private static string MakeSerpQueryPlanJson()
    {
        return """
        {
        "queries": [
            "topic overview",
            "recent developments topic",
            "risks and limitations topic"
        ]
        }
        """;
    }

    private static string MakeBreadthDepthJson() => """{ "breadth": 2, "depth": 2 }""";
    private static string MakeLanguageRegionJson() => """{ "language": "en", "region": null }""";
    private static string MakeDiscoveryModeJson() => """{ "discoveryMode": "Balanced" }""";

    private static string MakeClarificationsJson()
    {
        return """
        {
        "queries": [
            "What scope should the report cover?",
            "Any constraints on sources (recency/region/academic-only)?"
        ]
        }
        """;
    }

    private static string MakeLearningExtractionJson(int count)
    {
        // count is adaptiveMaxLearnings; we can return min(count, 5) items quickly.
        var n = Math.Clamp(count, 2, 5);

        // IMPORTANT: property names must match your DTO ("learnings", "text", "importance")
        var items = new List<string>();
        for (int i = 1; i <= n; i++)
        {
            var imp = i == 1 ? 0.95 : 0.70 - (i * 0.05);
            if (imp < 0.1) imp = 0.1;
            var statementType = i switch
            {
                1 => "Finding",
                2 => "Requirement",
                3 => "Forecast",
                4 => "Claim",
                _ => "Commentary"
            };
            items.Add($$"""{"text":"Learning {{i}} extracted from segment.","statementType":"{{statementType}}","importance":{{imp.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}""");
        }

        return $$"""{"learnings":[{{string.Join(",", items)}}]}""";
    }

    private static string? TryGetSchemaRaw(ChatResponseFormat? responseFormat)
    {
        if (responseFormat is null) return null;

        var t = responseFormat.GetType();
        foreach (var p in t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (p.PropertyType == typeof(System.Text.Json.JsonElement?))
            {
                var schema = (System.Text.Json.JsonElement)p.GetValue(responseFormat)!;
                return schema.GetRawText();
            }
        }

        return null;
    }
}
