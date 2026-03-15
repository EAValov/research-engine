using ResearchEngine.Configuration;
using ResearchEngine.Domain;

namespace ResearchEngine.Infrastructure;

public abstract class TokenizerBase : ITokenizer
{
    public const int MinimumContextLength = 10_000;

    private const double EstimatedCharsPerToken = 4.0;
    private const double SafetyBufferMultiplier = 1.2;

    private readonly IRuntimeSettingsAccessor _runtimeSettings;

    protected TokenizerBase(IRuntimeSettingsAccessor runtimeSettings)
    {
        _runtimeSettings = runtimeSettings ?? throw new ArgumentNullException(nameof(runtimeSettings));
    }

    public async Task<TokenizeResult> TokenizePromptAsync(
        Prompt prompt,
        CancellationToken cancellationToken = default)
    {
        if (prompt is null) throw new ArgumentNullException(nameof(prompt));
        if (prompt.systemPrompt is null)
            throw new ArgumentNullException(nameof(prompt.systemPrompt));
        if (prompt.userPrompt is null)
            throw new ArgumentNullException(nameof(prompt.userPrompt));

        var snapshot = await _runtimeSettings.GetCurrentAsync(cancellationToken);
        var config = snapshot.ChatConfig ?? throw new InvalidOperationException("ChatConfig is not configured.");

        if (config.MaxContextLength is int configuredMaxContextLength &&
            configuredMaxContextLength < MinimumContextLength)
        {
            throw new InvalidOperationException(
                $"ChatConfig.MaxContextLength must be at least {MinimumContextLength}.");
        }

        var model = config.ModelId
                    ?? throw new InvalidOperationException(
                        "ChatConfig.ModelId must be set to use TokenizePromptAsync.");

        if (config.MaxContextLength is int maxContextLength)
            return EstimateTokenCount(prompt, maxContextLength);

        var payload = BuildPayload(model, prompt);
        return await TokenizeCoreAsync(config, payload, cancellationToken);
    }

    protected abstract Task<TokenizeResult> TokenizeCoreAsync(
        ChatConfig config,
        object payload,
        CancellationToken cancellationToken = default);

    protected static object BuildPayload(string model, Prompt prompt) =>
        new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = prompt.systemPrompt },
                new { role = "user", content = prompt.userPrompt }
            }
        };

    private static TokenizeResult EstimateTokenCount(Prompt prompt, int maxContextLength)
    {
        var characterCount = prompt.systemPrompt.Length + prompt.userPrompt.Length;
        var estimatedTokens = EstimateTokens(characterCount);

        return new TokenizeResult
        {
            Count = estimatedTokens,
            MaxModelLen = maxContextLength
        };
    }

    private static int EstimateTokens(int characterCount)
    {
        var rawEstimate = Math.Ceiling(characterCount / EstimatedCharsPerToken);
        var conservativeEstimate = Math.Ceiling(rawEstimate * SafetyBufferMultiplier);
        return Math.Max(1, (int)conservativeEstimate);
    }
}
