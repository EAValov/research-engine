
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ResearchEngine.Domain;
using ResearchEngine.Configuration;
using ResearchEngine.Prompts;
using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace ResearchEngine.Infrastructure;

public sealed class LearningIntelService(
    IChatModel chatModel,
    ITokenizer tokenizer,
    IEmbeddingModel embeddingModel,
    IResearchJobStore jobStore,
    IOptions<LearningSimilarityOptions> similarityOptions,
    ILogger<LearningIntelService> logger)
    : ILearningIntelService
{
    private readonly LearningSimilarityOptions _options = similarityOptions.Value;

    private const int MaxLearningsPerSegment = 20;
    private const int MinLearningsPerSegment = 5;

    public async Task<IReadOnlyList<Learning>> ExtractAndSaveLearningsAsync(
        Guid jobId,
        Guid sourceId,
        string query,
        string clarificationsText,
        string sourceUrl,
        string sourceContent,
        string targetLanguage,
        bool computeEmbeddings,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sourceContent))
            return Array.Empty<Learning>();

        var pending = new Queue<string>();
        pending.Enqueue(sourceContent);

        var allLearnings = new List<Learning>();
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var segment = pending.Dequeue();
            if (string.IsNullOrWhiteSpace(segment))
                continue;

            var adaptiveMax = Math.Clamp(
                ComputeAdaptiveMaxLearnings(segment.Length),
                MinLearningsPerSegment,
                MaxLearningsPerSegment);

            var prompt = LearningExtractionPromptFactory.Build(
                query,
                segment,
                clarificationsText: clarificationsText,
                maxLearnings: adaptiveMax,
                targetLanguage: targetLanguage);

            var tok = await tokenizer.TokenizePromptAsync(prompt, cancellationToken: ct);

            logger.LogDebug(
                "Token count for learning-extraction segment (URL={Url}, length={Length}): {Tokens}/{Max}",
                sourceUrl, segment.Length, tok.Count, tok.MaxModelLen);

            if (tok.Count > tok.MaxModelLen)
            {
                if (segment.Length < 2000)
                {
                    pending.Enqueue(segment[..(segment.Length / 2)]);
                    continue;
                }

                var (left, right) = SplitSegmentOnBoundary(segment);
                if (!string.IsNullOrWhiteSpace(left)) pending.Enqueue(left);
                if (!string.IsNullOrWhiteSpace(right)) pending.Enqueue(right);
                continue;
            }

            var responseFormat = LearningExtractionResponse.JsonResponseSchema(jsonOptions);
            var raw = await chatModel.ChatAsync(
                prompt,
                tools: null,
                responseFormat: responseFormat,
                cancellationToken: ct);

            var withoutThink = chatModel.StripThinkBlock(raw.Text).Trim();

            LearningExtractionResponse? parsed = null;
            try
            {
                parsed = JsonSerializer.Deserialize<LearningExtractionResponse>(withoutThink, jsonOptions);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to deserialize learning extraction JSON for URL {Url}.", sourceUrl);
            }

            var items = parsed?.Learnings
                ?.Where(l => !string.IsNullOrWhiteSpace(l.Text))
                .OrderByDescending(l => l.Importance)
                .Take(adaptiveMax)
                .ToList();

            if (items is null || items.Count == 0)
                continue;
         
            var now = DateTimeOffset.UtcNow;

            foreach (var it in items)
            {
                allLearnings.Add(new Learning
                {
                    Id = Guid.NewGuid(),
                    JobId = jobId,
                    SourceId = sourceId,
                    Text = it.Text.Trim(),
                    ImportanceScore = it.Importance,
                    EvidenceText = segment,
                    CreatedAt = now
                });
            }
        }

        if (!computeEmbeddings || allLearnings.Count == 0)
            return allLearnings;

        // Attach embeddings in-memory
        await PopulateEmbeddingsAsync(allLearnings, ct);
        await jobStore.AddLearningsAsync(jobId, sourceId, query, allLearnings, ct);
        return allLearnings;
    }

    private async Task<IReadOnlyList<Learning>> PopulateEmbeddingsAsync(
        IEnumerable<Learning> learnings,
        CancellationToken ct = default)
    {
        if (learnings is null)
            return Array.Empty<Learning>();

        var list = learnings as IList<Learning> ?? learnings.ToList();
        if (list.Count == 0)
            return Array.Empty<Learning>();

        var toEmbed = list.Where(l => l.Embedding is null).ToList();
        if (toEmbed.Count == 0)
            return Array.Empty<Learning>();

        var texts = toEmbed.Select(l => l.Text).ToList();
        var vectors = await embeddingModel.GenerateEmbeddingsAsync(texts, ct);

        var count = Math.Min(toEmbed.Count, vectors.Count);
        for (var i = 0; i < count; i++)
        {
            // attach navigation; store will persist to LearningEmbeddings table
            toEmbed[i].Embedding = new LearningEmbedding
            {
                Id = Guid.NewGuid(),
                LearningId = toEmbed[i].Id,
                Vector = new Pgvector.Vector(vectors[i].Vector),
                CreatedAt = DateTimeOffset.UtcNow
            };
        }

        logger.LogInformation("Generated embeddings for {Count} learnings.", count);
        return toEmbed;
    }

    public async Task<IReadOnlyList<Learning>> GetSimilarLearningsAsync(
        string queryText,
        Guid synthesisId,
        string? language = null,
        string? region = null,
        int topK = 20,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(queryText))
            return Array.Empty<Learning>();

        var emb = await embeddingModel.GenerateEmbeddingAsync(queryText, ct);
        if (emb is null)
            return Array.Empty<Learning>();

        var queryVector = new Pgvector.Vector(emb.Vector);

        var maxK = Math.Clamp(topK, 1, 200);
        var localMinImportance = _options.LocalMinImportance;
        var globalMinImportance = _options.GlobalMinImportance;
        var minLocalFractionForNoGlobal = _options.MinLocalFractionForNoGlobal;

        // Load overrides once
        var snapshot = await jobStore.GetSynthesisOverridesAsync(synthesisId, ct);
        var jobId = snapshot.JobId;

        bool IsSourceExcluded(Guid sourceId) =>
            snapshot.SourceOverridesBySourceId.TryGetValue(sourceId, out var so) && so.Excluded == true;

        bool IsLearningExcluded(Guid learningId) =>
            snapshot.LearningOverridesByLearningId.TryGetValue(learningId, out var lo) && lo.Excluded == true;

        bool IsPinnedSource(Guid sourceId) =>
            snapshot.SourceOverridesBySourceId.TryGetValue(sourceId, out var so) && so.Pinned == true;

        bool IsPinnedLearning(Guid learningId) =>
            snapshot.LearningOverridesByLearningId.TryGetValue(learningId, out var lo) && lo.Pinned == true;

        float EffectiveLearningScore(Learning l)
        {
            if (snapshot.LearningOverridesByLearningId.TryGetValue(l.Id, out var lo) && lo.ScoreOverride.HasValue)
                return lo.ScoreOverride.Value;

            return l.ImportanceScore;
        }

        // 1) local (job-scoped) first
        var localRaw = new List<Learning>();

        localRaw.AddRange(await jobStore.VectorSearchLearningsAsync(
            queryVector, jobId, language, region, localMinImportance, maxK, ct));

        if (localRaw.Count < maxK && !string.IsNullOrWhiteSpace(region))
        {
            localRaw.AddRange(await jobStore.VectorSearchLearningsAsync(
                queryVector, jobId, language, region: null, localMinImportance, maxK - localRaw.Count, ct));
        }

        if (localRaw.Count < maxK && !string.IsNullOrWhiteSpace(language))
        {
            localRaw.AddRange(await jobStore.VectorSearchLearningsAsync(
                queryVector, jobId, language: null, region: null, localMinImportance, maxK - localRaw.Count, ct));
        }

        var local = localRaw
            .GroupBy(l => l.Id)
            .Select(g => g.First())
            .Where(l => !IsLearningExcluded(l.Id))
            .Where(l => !IsSourceExcluded(l.SourceId))
            .Select(l => new
            {
                Learning = l,
                Pinned = IsPinnedLearning(l.Id) || IsPinnedSource(l.SourceId),
                Score = EffectiveLearningScore(l)
            })
            .OrderByDescending(x => x.Pinned)
            .ThenByDescending(x => x.Score)
            .Select(x => x.Learning)
            .ToList();

        var minLocalNeeded = (int)Math.Ceiling(maxK * minLocalFractionForNoGlobal);
        if (local.Count >= minLocalNeeded)
            return ApplyDiversityFilter(local, topK);

        // 2) global fallback (still apply local synthesis overrides)
        var remaining = maxK - local.Count;
        if (remaining <= 0)
            return ApplyDiversityFilter(local, topK);

        var globalRaw = await jobStore.VectorSearchLearningsAsync(
            queryVector, jobId: null, language, region: null, globalMinImportance, remaining, ct);

        var global = globalRaw
            .GroupBy(l => l.Id)
            .Select(g => g.First())
            .Where(l => !IsLearningExcluded(l.Id))
            .Where(l => !IsSourceExcluded(l.SourceId))
            .Select(l => new
            {
                Learning = l,
                Pinned = IsPinnedLearning(l.Id) || IsPinnedSource(l.SourceId),
                Score = EffectiveLearningScore(l)
            })
            .OrderByDescending(x => x.Pinned)
            .ThenByDescending(x => x.Score)
            .Select(x => x.Learning)
            .ToList();

        local.AddRange(global);
        local = local.GroupBy(l => l.Id).Select(g => g.First()).ToList();

        return ApplyDiversityFilter(local, topK);
    }

    private IReadOnlyList<Learning> ApplyDiversityFilter(IReadOnlyList<Learning> orderedCandidates, int topK)
    {
        if (orderedCandidates.Count == 0)
            return orderedCandidates;

        var maxPerUrl = _options.DiversityMaxPerUrl;
        var maxTextSimilarity = _options.DiversityMaxTextSimilarity;

        var selected = new List<Learning>(capacity: topK);
        var perUrl = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var seenTexts = new List<string>();

        foreach (var c in orderedCandidates)
        {
            if (selected.Count >= topK)
                break;

            var url = c.Source?.Url ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(url) &&
                perUrl.TryGetValue(url, out var count) && count >= maxPerUrl)
                continue;

            var norm = NormalizeForSimilarity(c.Text);
            if (string.IsNullOrWhiteSpace(norm))
                continue;

            if (seenTexts.Any(t => ComputeJaccardSimilarity(t, norm) >= maxTextSimilarity))
                continue;

            selected.Add(c);
            seenTexts.Add(norm);

            if (!string.IsNullOrWhiteSpace(url))
                perUrl[url] = perUrl.TryGetValue(url, out var n) ? n + 1 : 1;
        }

        return selected;
    }

    private static int ComputeAdaptiveMaxLearnings(int segmentLengthChars)
    {
        if (segmentLengthChars <= 2000) return 5;
        if (segmentLengthChars <= 6000) return 8;
        if (segmentLengthChars <= 15000) return 12;
        return MaxLearningsPerSegment;
    }

    private static (string left, string right) SplitSegmentOnBoundary(string segment)
    {
        if (string.IsNullOrEmpty(segment))
            return (string.Empty, string.Empty);

        var length = segment.Length;
        if (length <= 2000)
        {
            var mid = length / 2;
            return (segment[..mid], segment[mid..]);
        }

        var target = length / 2;
        var leftSearchLimit = Math.Max(0, target - 2000);
        var rightSearchLimit = Math.Min(length, target + 2000);

        var window = segment[leftSearchLimit..rightSearchLimit];
        var relParagraphIdx = window.LastIndexOf("\n\n", StringComparison.Ordinal);
        if (relParagraphIdx >= 0)
        {
            var splitIndex = leftSearchLimit + relParagraphIdx + 2;
            return (segment[..splitIndex], segment[splitIndex..]);
        }

        var relSentenceIdx = window.LastIndexOf(". ", StringComparison.Ordinal);
        if (relSentenceIdx >= 0)
        {
            var splitIndex = leftSearchLimit + relSentenceIdx + 2;
            return (segment[..splitIndex], segment[splitIndex..]);
        }

        var midIndex = length / 2;
        return (segment[..midIndex], segment[midIndex..]);
    }

    private static string NormalizeForSimilarity(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch))
                sb.Append(char.ToLowerInvariant(ch));
            else if (ch is '-' or '_' or '%')
                sb.Append(ch);
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static double ComputeJaccardSimilarity(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return 0.0;

        var setA = a.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var setB = b.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);

        if (setA.Count == 0 || setB.Count == 0)
            return 0.0;

        var intersection = setA.Intersect(setB).Count();
        var union = setA.Union(setB).Count();
        return union == 0 ? 0.0 : (double)intersection / union;
    }

    private sealed class ExtractedLearningItem
    {
        [Description("Single, self-contained learning text in the target language, highly relevant to the user's query.")]
        public required string Text { get; init; }

        [Description("Importance score between 0.0 (barely relevant) and 1.0 (critical for answering the query).")]
        public required float Importance { get; init; }
    }

    private sealed class LearningExtractionResponse
    {
        [Description("Array of extracted learnings.")]
        public required List<ExtractedLearningItem> Learnings { get; init; }

        public static ChatResponseFormat JsonResponseSchema(JsonSerializerOptions? jsonSerializerOptions = default)
        {
            var jsonElement = AIJsonUtilities.CreateJsonSchema(
                typeof(LearningExtractionResponse),
                description: "Structured learnings extraction result",
                serializerOptions: jsonSerializerOptions);

            return new ChatResponseFormatJson(jsonElement);
        }
    }
}