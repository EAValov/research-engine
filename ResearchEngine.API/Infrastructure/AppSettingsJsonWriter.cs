using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ResearchEngine.Configuration;

namespace ResearchEngine.Infrastructure;

public sealed class AppSettingsJsonWriter(
    IHostEnvironment hostEnvironment,
    IConfiguration configuration,
    RuntimeSettingsOverrideProvider runtimeOverrides,
    ILogger<AppSettingsJsonWriter> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly string _appSettingsPath = Path.Combine(hostEnvironment.ContentRootPath, "appsettings.json");

    public async Task WriteRuntimeSettingsAsync(
        ResearchOrchestratorConfig researchOrchestratorConfig,
        LearningSimilarityOptions learningSimilarityOptions,
        ChatConfig chatConfig,
        CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            if (!File.Exists(_appSettingsPath))
                throw new FileNotFoundException("Could not find appsettings.json.", _appSettingsPath);

            var json = await File.ReadAllTextAsync(_appSettingsPath, ct);
            var root = JsonNode.Parse(json) as JsonObject
                ?? throw new InvalidOperationException("appsettings.json does not contain a JSON object.");

            root[nameof(ResearchOrchestratorConfig)] =
                JsonSerializer.SerializeToNode(researchOrchestratorConfig, SerializerOptions);
            root[nameof(LearningSimilarityOptions)] =
                JsonSerializer.SerializeToNode(learningSimilarityOptions, SerializerOptions);
            root[nameof(ChatConfig)] =
                JsonSerializer.SerializeToNode(chatConfig, SerializerOptions);

            var updated = root.ToJsonString(SerializerOptions);
            var tempPath = _appSettingsPath + ".tmp";

            await File.WriteAllTextAsync(tempPath, updated, ct);
            File.Move(tempPath, _appSettingsPath, overwrite: true);

            if (configuration is IConfigurationRoot configurationRoot)
                configurationRoot.Reload();

            runtimeOverrides.SetValues(BuildOverrides(
                researchOrchestratorConfig,
                learningSimilarityOptions,
                chatConfig));
        }
        catch
        {
            logger.LogError(
                "Failed to update runtime settings in {Path}.",
                _appSettingsPath);
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static IEnumerable<KeyValuePair<string, string?>> BuildOverrides(
        ResearchOrchestratorConfig researchOrchestratorConfig,
        LearningSimilarityOptions learningSimilarityOptions,
        ChatConfig chatConfig)
    {
        return
        [
            Pair("ResearchOrchestratorConfig:LimitSearches", researchOrchestratorConfig.LimitSearches),
            Pair("ResearchOrchestratorConfig:MaxUrlParallelism", researchOrchestratorConfig.MaxUrlParallelism),
            Pair("ResearchOrchestratorConfig:MaxUrlsPerSerpQuery", researchOrchestratorConfig.MaxUrlsPerSerpQuery),
            Pair("LearningSimilarityOptions:MinImportance", learningSimilarityOptions.MinImportance),
            Pair("LearningSimilarityOptions:DiversityMaxPerUrl", learningSimilarityOptions.DiversityMaxPerUrl),
            Pair("LearningSimilarityOptions:DiversityMaxTextSimilarity", learningSimilarityOptions.DiversityMaxTextSimilarity),
            Pair("LearningSimilarityOptions:MaxLearningsPerSegment", learningSimilarityOptions.MaxLearningsPerSegment),
            Pair("LearningSimilarityOptions:MinLearningsPerSegment", learningSimilarityOptions.MinLearningsPerSegment),
            Pair("LearningSimilarityOptions:GroupAssignSimilarityThreshold", learningSimilarityOptions.GroupAssignSimilarityThreshold),
            Pair("LearningSimilarityOptions:GroupSearchTopK", learningSimilarityOptions.GroupSearchTopK),
            Pair("LearningSimilarityOptions:MaxEvidenceLength", learningSimilarityOptions.MaxEvidenceLength),
            Pair("ChatConfig:Endpoint", chatConfig.Endpoint),
            Pair("ChatConfig:ApiKey", chatConfig.ApiKey),
            Pair("ChatConfig:ModelId", chatConfig.ModelId),
            Pair("ChatConfig:MaxContextLength", chatConfig.MaxContextLength)
        ];
    }

    private static KeyValuePair<string, string?> Pair(string key, string? value)
        => new(key, value);

    private static KeyValuePair<string, string?> Pair(string key, int value)
        => new(key, value.ToString(CultureInfo.InvariantCulture));

    private static KeyValuePair<string, string?> Pair(string key, int? value)
        => new(key, value?.ToString(CultureInfo.InvariantCulture));

    private static KeyValuePair<string, string?> Pair(string key, float value)
        => new(key, value.ToString(CultureInfo.InvariantCulture));

    private static KeyValuePair<string, string?> Pair(string key, double value)
        => new(key, value.ToString(CultureInfo.InvariantCulture));
}
