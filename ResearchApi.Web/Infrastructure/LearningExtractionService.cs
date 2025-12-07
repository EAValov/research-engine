
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

    public async Task<IReadOnlyList<ExtractedLearningItem>> ExtractLearningsAsync(
        string query,
        string clarificationsText,
        ScrapedPage page,
        string sourceUrl,
        string targetLanguage,
        CancellationToken ct)
    {
        var pending = new Queue<string>();
        pending.Enqueue(page.Content ?? string.Empty);

        var allLearnings = new List<ExtractedLearningItem>();

        // we'll reuse Json options for deserialization
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var segment = pending.Dequeue();
            if (string.IsNullOrWhiteSpace(segment))
                continue;

            // --- adaptive max learnings for THIS segment ---
            var adaptiveMaxLearnings = Math.Clamp(
                ComputeAdaptiveMaxLearnings(segment.Length),
                MinLearningsPerSegment,
                MaxLearningsPerSegment
            );

            // Build prompt for this segment
            var prompt = LearningExtractionPromptFactory.Build(
                query,
                segment,
                clarificationsText: clarificationsText,
                maxLearnings: adaptiveMaxLearnings,
                targetLanguage: targetLanguage);

            var tokenizeResult = await tokenizer.TokenizePromptAsync(prompt, cancellationToken: ct);

            logger.LogDebug(
                "Token count for learning-extraction segment (URL={Url}, length={Length} chars): {Tokens} / {MaxLenght}",
                sourceUrl, segment.Length, tokenizeResult.Count, tokenizeResult.MaxModelLen);

            if (tokenizeResult.Count <= tokenizeResult.MaxModelLen)
            {
                // Safe to call LLM with structured JSON output
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
                    .ToList()
                    ?? new List<ExtractedLearningItem>();

                if (page.Content is null)
                    continue;

                logger.LogInformation(
                    "Extracted {Count} structured learnings from chunk of length {Length} for URL {Url}",
                    learnings.Count,
                    page.Content.Length,
                    sourceUrl);

                if (learnings.Count == 0)
                {
                    logger.LogWarning(
                        "No learnings extracted for chunk of URL {Url}; content length={Length}, query={Query}",
                        sourceUrl,
                        page.Content.Length,
                        query);
                }

                // Optionally sort by importance descending and enforce adaptiveMaxLearnings again
                var finalLearnings = learnings
                    .OrderByDescending(l => l.Importance)
                    .Take(adaptiveMaxLearnings)
                    .ToList();

                allLearnings.AddRange(finalLearnings);
            }
            else
            {
                // Segment too big -> split it further
                if (segment.Length < 2000) // safety threshold to avoid endless splitting
                {
                    logger.LogWarning(
                        "Segment for URL {Url} still over token limit ({Tokens}) " +
                        "even though length={Length}; truncating.",
                        sourceUrl, tokenizeResult.Count, segment.Length);

                    var truncated = segment[..(segment.Length / 2)];
                    pending.Enqueue(truncated);
                    continue;
                }

                var (left, right) = SplitSegmentOnBoundary(segment);

                logger.LogInformation(
                    "Split segment for URL {Url} into two parts: leftLength={Left}, rightLength={Right}",
                    sourceUrl, left.Length, right.Length);

                if (!string.IsNullOrWhiteSpace(left))
                    pending.Enqueue(left);
                if (!string.IsNullOrWhiteSpace(right))
                    pending.Enqueue(right);
            }
        }

        return allLearnings;
    }

    /// <summary>
    /// Simple heuristic: more content → slightly more allowed learnings,
    /// but always clamped between Min and Max.
    /// </summary>
    static int ComputeAdaptiveMaxLearnings(int segmentLengthChars)
    {
        // very short chunk → few learnings
        if (segmentLengthChars <= 2000) return 5;
        if (segmentLengthChars <= 6000) return 8;
        if (segmentLengthChars <= 15000) return 12;

        // really long segments hit the cap
        return MaxLearningsPerSegment;
    }

    /// <summary>
    /// Splits a segment into two parts on a "nice" boundary (paragraph / sentence),
    /// roughly in the middle. If no good boundary is found, falls back to a hard split.
    /// </summary>
    (string left, string right) SplitSegmentOnBoundary(string segment)
    {
        if (string.IsNullOrEmpty(segment))
            return (string.Empty, string.Empty);

        var length = segment.Length;

        // Short segments – just hard split
        if (length <= 2000)
        {
            var mid = length / 2;
            return (segment[..mid], segment[mid..]);
        }

        var target = length / 2;

        // 1) Try paragraph boundary near the middle: "\n\n"
        var leftSearchLimit  = Math.Max(0, target - 2000);
        var rightSearchLimit = Math.Min(length, target + 2000);

        var window = segment[leftSearchLimit..rightSearchLimit];
        var relParagraphIdx = window.LastIndexOf("\n\n", StringComparison.Ordinal);

        if (relParagraphIdx >= 0)
        {
            var splitIndex = leftSearchLimit + relParagraphIdx + 2; // include the line break
            var left  = segment[..splitIndex];
            var right = segment[splitIndex..];
            return (left, right);
        }

        // 2) Try sentence boundary (period + space) near middle
        var relSentenceIdx = window.LastIndexOf(". ", StringComparison.Ordinal);
        if (relSentenceIdx >= 0)
        {
            var splitIndex = leftSearchLimit + relSentenceIdx + 2; // after ". "
            var left  = segment[..splitIndex];
            var right = segment[splitIndex..];
            return (left, right);
        }

        // 3) Fallback: hard split in the middle
        var midIndex = length / 2;
        return (segment[..midIndex], segment[midIndex..]);
    }
};

