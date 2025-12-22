
using System.Text.Json;
using ResearchApi.Domain;
using ResearchApi.Prompts;

namespace ResearchApi.Infrastructure;


public class LearningExtractionService(
    IChatModel chatModel,
    ITokenizer tokenizer,
    ILogger<LearningExtractionService> logger
) : ILearningExtractionService
{
    private const int MaxLearningsPerSegment = 20;
    private const int MinLearningsPerSegment = 5;

    public async Task<IReadOnlyList<ExtractedLearningItemWithEvidence>> ExtractLearningsAsync(
        string query,
        string clarificationsText,
        string sourceContent,
        string sourceUrl,
        string targetLanguage,
        CancellationToken ct)
    {
        var pending = new Queue<string>();
        pending.Enqueue(sourceContent ?? string.Empty);

        var allLearnings = new List<ExtractedLearningItemWithEvidence>();

        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var segment = pending.Dequeue();
            if (string.IsNullOrWhiteSpace(segment))
                continue;

            var adaptiveMaxLearnings = Math.Clamp(
                ComputeAdaptiveMaxLearnings(segment.Length),
                MinLearningsPerSegment,
                MaxLearningsPerSegment
            );

            var prompt = LearningExtractionPromptFactory.Build(
                query,
                segment,
                clarificationsText: clarificationsText,
                maxLearnings: adaptiveMaxLearnings,
                targetLanguage: targetLanguage);

            var tokenizeResult = await tokenizer.TokenizePromptAsync(prompt, cancellationToken: ct);

            logger.LogDebug(
                "Token count for learning-extraction segment (URL={Url}, length={Length} chars): {Tokens} / {MaxLen}",
                sourceUrl, segment.Length, tokenizeResult.Count, tokenizeResult.MaxModelLen);

            if (tokenizeResult.Count <= tokenizeResult.MaxModelLen)
            {
                var responseFormat = LearningExtractionResponse.JsonResponseSchema(jsonOptions);
                var rawResponse = await chatModel.ChatAsync(
                    prompt,
                    tools: null,
                    responseFormat: responseFormat,
                    cancellationToken: ct);

                var withoutThink = chatModel.StripThinkBlock(rawResponse.Text).Trim();

                LearningExtractionResponse? parsed = null;
                try
                {
                    parsed = JsonSerializer.Deserialize<LearningExtractionResponse>(withoutThink, jsonOptions);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "Failed to deserialize learning extraction JSON for URL {Url}. Raw response: {Response}",
                        sourceUrl,
                        withoutThink);
                }

                var learnings = parsed?.Learnings
                    ?.Where(l => !string.IsNullOrWhiteSpace(l.Text))
                    .Select(l => new ExtractedLearningItemWithEvidence(
                        Text: l.Text.Trim(),
                        Importance: l.Importance,
                        EvidenceText: segment))
                    .ToList()
                    ?? new List<ExtractedLearningItemWithEvidence>();

                logger.LogInformation(
                    "Extracted {Count} learnings from segment length {Length} for URL {Url}",
                    learnings.Count, segment.Length, sourceUrl);

                var finalLearnings = learnings
                    .OrderByDescending(l => l.Importance)
                    .Take(adaptiveMaxLearnings)
                    .ToList();

                allLearnings.AddRange(finalLearnings);
            }
            else
            {
                if (segment.Length < 2000)
                {
                    logger.LogWarning(
                        "Segment for URL {Url} still over token limit ({Tokens}) even though length={Length}; truncating.",
                        sourceUrl, tokenizeResult.Count, segment.Length);

                    pending.Enqueue(segment[..(segment.Length / 2)]);
                    continue;
                }

                var (left, right) = SplitSegmentOnBoundary(segment);

                logger.LogInformation(
                    "Split segment for URL {Url} into two parts: leftLength={Left}, rightLength={Right}",
                    sourceUrl, left.Length, right.Length);

                if (!string.IsNullOrWhiteSpace(left)) pending.Enqueue(left);
                if (!string.IsNullOrWhiteSpace(right)) pending.Enqueue(right);
            }
        }

        return allLearnings;
    }

    static int ComputeAdaptiveMaxLearnings(int segmentLengthChars)
    {
        if (segmentLengthChars <= 2000) return 5;
        if (segmentLengthChars <= 6000) return 8;
        if (segmentLengthChars <= 15000) return 12;
        return MaxLearningsPerSegment;
    }

    (string left, string right) SplitSegmentOnBoundary(string segment)
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
}

