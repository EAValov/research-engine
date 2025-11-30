
using Microsoft.Extensions.Options;
using ResearchApi.Configuration;
using ResearchApi.Domain;
using ResearchApi.Prompts;

namespace ResearchApi.Infrastructure;
public interface ILearningExtractionService
{
    Task<IReadOnlyList<string>> ExtractLearningsAsync(
        string query,
        string clarificationsText,
        ScrapedPage page,
        string sourceUrl,
        string targetLanguage,
        CancellationToken ct);
}

public class LearningExtractionService(
    ILlmService llmService,
    IOptions<LlmChunkingOptions> options,
    ILogger<LearningExtractionService> logger
) : ILearningExtractionService
{
     const int MaxLearningsPerSearchResult = 20;

    public async Task<IReadOnlyList<string>> ExtractLearningsAsync(
        string query,
        string clarificationsText,
        ScrapedPage page,
        string sourceUrl,
        string targetLanguage,
        CancellationToken ct)
    {
        var maxPromptTokens = options.Value.MaxPromptTokens;

        var pending = new Queue<string>();
        
        pending.Enqueue(page.Content ?? string.Empty);

        var allLearnings = new List<string>();

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var segment = pending.Dequeue();
            if (string.IsNullOrWhiteSpace(segment))
                continue;

            // Build prompt for this segment
            var prompt = LearningExtractionPromptFactory.Build(
                query,
                segment,
                clarificationsText: clarificationsText,
                maxLearnings: MaxLearningsPerSearchResult,
                targetLanguage: targetLanguage);

            var tokenCount = await llmService.TokenizePromptAsync(prompt, cancellationToken:ct);

            logger.LogDebug(
                "Token count for learning-extraction segment (URL={Url}, length={Length} chars): {Tokens}",
                sourceUrl, segment.Length, tokenCount);

            if (tokenCount.Count <= maxPromptTokens)
            {
                // Safe to call LLM
                var rawResponse = await llmService.ChatAsync(prompt, cancellationToken:ct);

                var withoutThink = llmService.StripThinkBlock(rawResponse.Text);

                var learnings = withoutThink
                    .Split('\n')
                    .Select(line => line.Trim())
                    .Where(line => !string.IsNullOrEmpty(line))
                    .ToList();

                if(page.Content is null)
                    continue;

                logger.LogInformation("Extracted {Count} learnings from chunk of length {Length} for URL {Url}", 
                    learnings.Count, page.Content.Length, sourceUrl);

                if (learnings.Count == 0)
                {
                    logger.LogWarning("No learnings extracted for chunk of URL {Url}; content length={Length}, query={Query}", 
                        sourceUrl, page.Content.Length, query);
                }                   

                allLearnings.AddRange(learnings);
            }
            else
            {
                // Segment too big -> split it further
                if (segment.Length < 2000) // safety threshold to avoid endless splitting
                {
                    logger.LogWarning(
                        "Segment for URL {Url} still over token limit ({Tokens}) " +
                        "even though length={Length}; truncating.",
                        sourceUrl, tokenCount, segment.Length);

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
    /// Splits a segment into two parts on a "nice" boundary (paragraph / sentence),
    /// roughly in the middle. If no good boundary is found, falls back to a hard split.
    /// </summary>
    private (string left, string right) SplitSegmentOnBoundary(string segment)
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