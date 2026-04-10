
using System.Text;
using System.Text.Json;
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
    IResearchSourceRepository sourceRepository,
    IResearchLearningRepository learningRepository,
    IResearchLearningGroupRepository learningGroupRepository,
    IResearchSynthesisOverridesRepository synthesisOverridesRepository,
    IRuntimeSettingsAccessor runtimeSettings,
    ILogger<LearningIntelService> logger)
    : ILearningIntelService
{
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

        const int maxSegmentSplitDepth = 8;
        const int promptBudgetSafetyMarginTokens = 512;

        var settings = await runtimeSettings.GetCurrentAsync(ct);
        var options = settings.LearningSimilarityOptions;
        var pending = new Queue<(string Segment, int SplitDepth)>();
        pending.Enqueue((sourceContent, 0));

        var allLearnings = new List<Learning>();
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var (segment, splitDepth) = pending.Dequeue();
            if (string.IsNullOrWhiteSpace(segment))
                continue;

            var adaptiveMax = Math.Clamp(
                ComputeAdaptiveMaxLearnings(segment.Length, options.MaxLearningsPerSegment),
                options.MinLearningsPerSegment,
                options.MaxLearningsPerSegment);

            var prompt = LearningExtractionPromptFactory.Build(
                query,
                segment,
                clarificationsText: clarificationsText,
                maxLearnings: adaptiveMax,
                targetLanguage: targetLanguage);

            var tok = await tokenizer.TokenizePromptAsync(prompt, cancellationToken: ct);
            var reservedOutputTokens = ComputeReservedOutputTokens(
                tok.MaxModelLen,
                settings.ChatConfig.MaxOutputTokens);
            var promptTokenBudget = ComputePromptTokenBudget(
                tok.MaxModelLen,
                reservedOutputTokens,
                promptBudgetSafetyMarginTokens);

            logger.LogDebug(
                "Token count for learning-extraction segment (URL={Url}, length={Length}): {Tokens}/{Max}. PromptBudget={PromptBudget}, ReservedOutputTokens={ReservedOutputTokens}, SafetyMarginTokens={SafetyMarginTokens}",
                sourceUrl,
                segment.Length,
                tok.Count,
                tok.MaxModelLen,
                promptTokenBudget,
                reservedOutputTokens,
                promptBudgetSafetyMarginTokens);

            if (tok.Count > promptTokenBudget)
            {
                TryEnqueueSplitSegments(
                    pending,
                    segment,
                    splitDepth,
                    maxSegmentSplitDepth,
                    sourceUrl,
                    reason: $"prompt token count {tok.Count} exceeded effective prompt budget {promptTokenBudget} within model limit {tok.MaxModelLen} (reserved output tokens {reservedOutputTokens}, safety margin {promptBudgetSafetyMarginTokens})");
                continue;
            }

            var responseFormat = LearningExtractionResponse.JsonResponseSchema(jsonOptions);
            var raw = await chatModel.ChatAsync(
                prompt,
                tools: null,
                responseFormat: responseFormat,
                cancellationToken: ct);

            var withoutThink = chatModel.StripThinkBlock(raw.Text).Trim();

            logger.LogDebug(
                "Learning-extraction response metadata for URL {Url}: FinishReason={FinishReason}, ResponseChars={ResponseChars}, SegmentLength={SegmentLength}, SplitDepth={SplitDepth}",
                sourceUrl,
                raw.FinishReason,
                withoutThink.Length,
                segment.Length,
                splitDepth);

            LearningExtractionResponse? parsed = null;
            try
            {
                parsed = JsonSerializer.Deserialize<LearningExtractionResponse>(withoutThink, jsonOptions);
            }
            catch (Exception ex)
            {
                var recovered = TryEnqueueSplitSegments(
                    pending,
                    segment,
                    splitDepth,
                    maxSegmentSplitDepth,
                    sourceUrl,
                    reason: raw.FinishReason == ChatFinishReason.Length
                        ? "response hit the output length limit and returned incomplete JSON"
                        : "response returned malformed JSON");

                if (recovered)
                {
                    logger.LogWarning(
                        ex,
                        "Recovering malformed learning extraction response for URL {Url} by splitting the segment. FinishReason={FinishReason}, ResponseChars={ResponseChars}, SplitDepth={SplitDepth}",
                        sourceUrl,
                        raw.FinishReason,
                        withoutThink.Length,
                        splitDepth);
                    continue;
                }

                logger.LogError(
                    ex,
                    "Failed to deserialize learning extraction JSON for URL {Url}. FinishReason={FinishReason}. JSON: {Json}",
                    sourceUrl,
                    raw.FinishReason,
                    TruncateForLog(withoutThink, 4000));
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
                var text = it.Text.Trim();
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                allLearnings.Add(new Learning
                {
                    Id = Guid.NewGuid(),
                    JobId = jobId,
                    SourceId = sourceId,
                    LearningGroupId = Guid.Empty, // assigned later
                    Text = text,
                    StatementType = ParseStatementType(it.StatementType),
                    ImportanceScore = it.Importance,
                    EvidenceText = segment.Length > options.MaxEvidenceLength
                        ? segment[..options.MaxEvidenceLength]
                        : segment,
                    CreatedAt = now,
                    IsUserProvided = false
                });
            }
        }

        if (!computeEmbeddings || allLearnings.Count == 0)
            return allLearnings;

        // 1) Embeddings
        await PopulateEmbeddingsAsync(allLearnings, ct);

        // 2) Assign groups (nearest-group by embedding; create group if none above threshold)
        await AssignGroupsAsync(jobId, allLearnings, ct);

        // 3) Persist
        await learningRepository.AddLearningsAsync(jobId, sourceId, query, allLearnings, ct);

        return allLearnings;
    }

    /// <summary>
    /// Adds a user-provided learning to a job. Creates/uses a Source depending on reference:
    /// - reference null/empty: uses a per-job "user:manual" Source (Kind=User)
    /// - reference provided: upserts a Web Source with placeholder content (crawl can fill later)
    ///
    /// Embedding is computed, group assignment is done (nearest group), and the learning is stored like extracted learnings.
    /// </summary>
    public async Task<Learning> AddUserLearningAsync(
        Guid jobId,
        string text,
        float importanceScore = 1.0f,
        string? reference = null,
        string? evidenceText = null,
        string? language = null,
        string? region = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Learning text is required.", nameof(text));

        var settings = await runtimeSettings.GetCurrentAsync(ct);
        var options = settings.LearningSimilarityOptions;
        var trimmedText = text.Trim();
        if (trimmedText.Length == 0)
            throw new ArgumentException("Learning text is required.", nameof(text));

        // Clamp importance to sane range
        var score = float.IsNaN(importanceScore) ? 1.0f : importanceScore;
        score = Math.Clamp(score <= 0 ? 1.0f : score, 0.0f, 1.0f);

        Source src;
        if (string.IsNullOrWhiteSpace(reference))
        {
            src = await sourceRepository.GetOrCreateUserSourceAsync(jobId, ct);
        }
        else
        {
            // Placeholder until crawl populates content.
            const string placeholderContent =
                "Placeholder content for user-provided reference. A crawl job may populate this later.";

            src = await sourceRepository.UpsertSourceAsync(
                jobId: jobId,
                reference: reference.Trim(),
                content: placeholderContent,
                title: null,
                language: language,
                region: region,
                kind: SourceKind.Web,
                ct: ct);
        }

        var now = DateTimeOffset.UtcNow;

        var learning = new Learning
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            SourceId = src.Id,
            LearningGroupId = Guid.Empty, // assigned below
            Text = trimmedText,
            StatementType = LearningStatementType.Finding,
            ImportanceScore = score,
            EvidenceText = (evidenceText ?? string.Empty).Length > options.MaxEvidenceLength
                ? (evidenceText ?? string.Empty)[..options.MaxEvidenceLength]
                : (evidenceText ?? string.Empty),
            CreatedAt = now,
            IsUserProvided = true
        };

        // Embedding (single)
        var emb = await embeddingModel.GenerateEmbeddingAsync(learning.Text, ct);
        if (emb?.Vector is null)
            throw new InvalidOperationException("Failed to generate embedding for user learning.");

        learning.Embedding = new LearningEmbedding
        {
            Id = Guid.NewGuid(),
            LearningId = learning.Id,
            Vector = new Pgvector.Vector(emb.Vector),
            CreatedAt = now
        };

        // Group assignment
        await AssignGroupsAsync(jobId, new List<Learning> { learning }, ct);

        // Persist (use stable "user" query placeholder)
        await learningRepository.AddLearningsAsync(jobId, src.Id, query: "user", learnings: new[] { learning }, ct);

        // Preserve the authoritative source reference for the API response without reattaching it during persistence.
        learning.Source = src;

        return learning;
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
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < count; i++)
        {
            toEmbed[i].Embedding = new LearningEmbedding
            {
                Id = Guid.NewGuid(),
                LearningId = toEmbed[i].Id,
                Vector = new Pgvector.Vector(vectors[i].Vector),
                CreatedAt = now
            };
        }

        logger.LogDebug("Generated embeddings for {Count} learnings.", count);
        return toEmbed;
    }

    /// <summary>
    /// Assigns each learning to a learning group using embedding similarity.
    ///
    /// Grouping strategy:
    /// - Uses cosine similarity between the learning embedding and existing group embeddings.
    /// - Only assigns to an existing group if similarity >= GroupAssignSimilarityThreshold.
    ///
    /// Design rationale:
    /// - Grouping is used for *deduplication*, not semantic clustering.
    /// - Only near-duplicate learnings should be auto-merged.
    /// - Semantically similar but non-identical claims are intentionally kept
    ///   in separate groups to avoid collapsing distinct evidence.
    ///
    /// Behavioral guarantees:
    /// - Identical or trivially rewritten learnings are grouped together.
    /// - Paraphrases that change meaning, scope, or emphasis will typically
    ///   form new groups.
    /// - If no sufficiently similar group exists, a new group is created
    ///   using the learning as the canonical representative.
    ///
    /// NOTE:
    /// - Canonical group text and score are updated only if a stronger
    ///   (higher-importance) learning is added to the group.
    /// - This method does NOT perform "soft clustering" or relatedness detection.
    ///   Those should be implemented separately if needed.
    /// </summary>
    private async Task AssignGroupsAsync(Guid jobId, IList<Learning> learnings, CancellationToken ct)
    {
        var settings = await runtimeSettings.GetCurrentAsync(ct);
        var options = settings.LearningSimilarityOptions;

        foreach (var l in learnings)
        {
            ct.ThrowIfCancellationRequested();

            if (l.Embedding is null)
                throw new InvalidOperationException("Learning embedding must be generated before grouping.");

            // Find nearest groups (with distance)
            var hits = await learningGroupRepository.VectorSearchLearningGroupsWithDistanceAsync(
                queryVector: l.Embedding.Vector,
                jobId: jobId,
                topK: options.GroupSearchTopK,
                ct: ct);

            var best = hits.FirstOrDefault();
            var bestSim = best is null ? 0f : 1f - best.CosineDistance;

            // logger.LogDebug("Group assign: bestDist={Dist} bestSim={Sim}", best?.CosineDistance, bestSim);

            if (best is not null && bestSim >= options.GroupAssignSimilarityThreshold)
            {
                l.LearningGroupId = best.Group.Id;

                // Update canonical if this learning is stronger
                if (l.ImportanceScore > best.Group.CanonicalImportanceScore)
                {
                    var emb = await embeddingModel.GenerateEmbeddingAsync(l.Text, ct);
                    if (emb?.Vector is not null)
                    {
                        await learningGroupRepository.UpdateLearningGroupCanonicalAsync(
                            groupId: best.Group.Id,
                            canonicalText: l.Text,
                            canonicalImportanceScore: l.ImportanceScore,
                            embeddingVector: emb.Vector,
                            ct: ct);
                    }
                }
            }
            else
            {
                // Create new group using this learning as canonical
                var emb = await embeddingModel.GenerateEmbeddingAsync(l.Text, ct);
                if (emb?.Vector is null)
                    throw new InvalidOperationException("Failed to generate embedding for learning group.");

                var g = await learningGroupRepository.CreateLearningGroupAsync(
                    jobId: jobId,
                    canonicalText: l.Text,
                    canonicalImportanceScore: l.ImportanceScore,
                    embeddingVector: emb.Vector,
                    ct: ct);

                l.LearningGroupId = g.Id;
            }
        }
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
        if (emb?.Vector is null)
            return Array.Empty<Learning>();

        var queryVector = new Pgvector.Vector(emb.Vector);

        var maxK = Math.Clamp(topK, 1, 200);
        var settings = await runtimeSettings.GetCurrentAsync(ct);
        var localMinImportance = settings.LearningSimilarityOptions.MinImportance;

        // Load overrides once
        var snapshot = await synthesisOverridesRepository.GetSynthesisOverridesAsync(synthesisId, ct);
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

        // Job-scoped only (global removed)
        var localRaw = new List<Learning>();

        localRaw.AddRange(await learningRepository.VectorSearchLearningsAsync(
            queryVector, jobId, language, region, localMinImportance, maxK, ct));

        if (localRaw.Count < maxK && !string.IsNullOrWhiteSpace(region))
        {
            localRaw.AddRange(await learningRepository.VectorSearchLearningsAsync(
                queryVector, jobId, language, region: null, localMinImportance, maxK - localRaw.Count, ct));
        }

        if (localRaw.Count < maxK && !string.IsNullOrWhiteSpace(language))
        {
            localRaw.AddRange(await learningRepository.VectorSearchLearningsAsync(
                queryVector, jobId, language: null, region: null, localMinImportance, maxK - localRaw.Count, ct));
        }

        var ordered = localRaw
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

        // NEW: dedupe by group (choose representative per group)
        var dedupedByGroup = ordered
            .Where(l => l.LearningGroupId != Guid.Empty)
            .GroupBy(l => l.LearningGroupId)
            .Select(g =>
            {
                var rep = g
                    .Select(l => new
                    {
                        Learning = l,
                        Pinned = IsPinnedLearning(l.Id) || IsPinnedSource(l.SourceId),
                        Score = EffectiveLearningScore(l)
                    })
                    .OrderByDescending(x => x.Pinned)
                    .ThenByDescending(x => x.Score)
                    .Select(x => x.Learning)
                    .First();

                return rep;
            })
            .ToList();

        // If some learnings somehow have no group (shouldn't happen after migration), append them last.
        var ungrouped = ordered.Where(l => l.LearningGroupId == Guid.Empty).ToList();
        dedupedByGroup.AddRange(ungrouped);

        return ApplyDiversityFilter(
            dedupedByGroup,
            topK,
            settings.LearningSimilarityOptions.DiversityMaxPerUrl,
            settings.LearningSimilarityOptions.DiversityMaxTextSimilarity);
    }

    private static IReadOnlyList<Learning> ApplyDiversityFilter(
        IReadOnlyList<Learning> orderedCandidates,
        int topK,
        int maxPerRef,
        double maxTextSimilarity)
    {
        if (orderedCandidates.Count == 0)
            return orderedCandidates;

        var selected = new List<Learning>(capacity: topK);
        var perRef = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var seenTexts = new List<string>();

        foreach (var c in orderedCandidates)
        {
            if (selected.Count >= topK)
                break;

            var reference = c.Source?.Reference ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(reference) &&
                perRef.TryGetValue(reference, out var count) && count >= maxPerRef)
                continue;

            var norm = NormalizeForSimilarity(c.Text);
            if (string.IsNullOrWhiteSpace(norm))
                continue;

            if (seenTexts.Any(t => ComputeJaccardSimilarity(t, norm) >= maxTextSimilarity))
                continue;

            selected.Add(c);
            seenTexts.Add(norm);

            if (!string.IsNullOrWhiteSpace(reference))
                perRef[reference] = perRef.TryGetValue(reference, out var n) ? n + 1 : 1;
        }

        return selected;
    }

    private static int ComputeAdaptiveMaxLearnings(int segmentLengthChars, int maxLearningsPerSegment)
    {
        if (segmentLengthChars <= 2000) return 5;
        if (segmentLengthChars <= 6000) return 8;
        if (segmentLengthChars <= 15000) return 12;
        return maxLearningsPerSegment;
    }

    private static int ComputeReservedOutputTokens(int maxModelLen, int? configuredMaxOutputTokens)
    {
        if (configuredMaxOutputTokens is > 0)
            return configuredMaxOutputTokens.Value;

        // Reserve some completion room even when no explicit cap is configured.
        return Math.Clamp(maxModelLen / 5, 512, 2048);
    }

    private static int ComputePromptTokenBudget(
        int maxModelLen,
        int reservedOutputTokens,
        int promptBudgetSafetyMarginTokens)
    {
        return Math.Max(1, maxModelLen - reservedOutputTokens - promptBudgetSafetyMarginTokens);
    }

    private bool TryEnqueueSplitSegments(
        Queue<(string Segment, int SplitDepth)> pending,
        string segment,
        int splitDepth,
        int maxSegmentSplitDepth,
        string sourceUrl,
        string reason)
    {
        if (string.IsNullOrWhiteSpace(segment) || segment.Length < 2)
        {
            logger.LogWarning(
                "Cannot split learning-extraction segment for URL {Url}. Reason={Reason}, SegmentLength={SegmentLength}, SplitDepth={SplitDepth}",
                sourceUrl,
                reason,
                segment?.Length ?? 0,
                splitDepth);
            return false;
        }

        if (splitDepth >= maxSegmentSplitDepth)
        {
            logger.LogWarning(
                "Reached max split depth while recovering learning extraction for URL {Url}. Reason={Reason}, SegmentLength={SegmentLength}, SplitDepth={SplitDepth}",
                sourceUrl,
                reason,
                segment.Length,
                splitDepth);
            return false;
        }

        var (left, right) = SplitSegmentOnBoundary(segment);
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            var mid = segment.Length / 2;
            if (mid <= 0 || mid >= segment.Length)
            {
                logger.LogWarning(
                    "Failed to compute a valid split for learning-extraction segment for URL {Url}. Reason={Reason}, SegmentLength={SegmentLength}, SplitDepth={SplitDepth}",
                    sourceUrl,
                    reason,
                    segment.Length,
                    splitDepth);
                return false;
            }

            left = segment[..mid];
            right = segment[mid..];
        }

        pending.Enqueue((left, splitDepth + 1));
        pending.Enqueue((right, splitDepth + 1));

        logger.LogWarning(
            "Splitting learning-extraction segment for URL {Url}. Reason={Reason}, SegmentLength={SegmentLength}, SplitDepth={SplitDepth}, LeftLength={LeftLength}, RightLength={RightLength}",
            sourceUrl,
            reason,
            segment.Length,
            splitDepth,
            left.Length,
            right.Length);

        return true;
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

    private static string TruncateForLog(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
            return value;

        return value[..maxChars] + "...";
    }

    private sealed class ExtractedLearningItem
    {
        [Description("Single, self-contained learning text in the target language, highly relevant to the user's query.")]
        public required string Text { get; init; }

        [Description("Statement type. Must be one of: Finding, Requirement, Forecast, Claim, Commentary, Contested.")]
        public required string StatementType { get; init; }

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

    private static LearningStatementType ParseStatementType(string? value)
        => LearningStatementTypeExtensions.TryParse(value, out var parsed)
            ? parsed
            : LearningStatementType.Finding;
}
