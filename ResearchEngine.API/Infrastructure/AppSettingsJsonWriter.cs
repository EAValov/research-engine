using System.Text.Json;
using System.Text.Json.Nodes;
using ResearchEngine.Configuration;

namespace ResearchEngine.Infrastructure;

public sealed class AppSettingsJsonWriter(
    IHostEnvironment hostEnvironment,
    IConfiguration configuration,
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

            var updated = root.ToJsonString(SerializerOptions);
            var tempPath = _appSettingsPath + ".tmp";

            await File.WriteAllTextAsync(tempPath, updated, ct);
            File.Move(tempPath, _appSettingsPath, overwrite: true);

            if (configuration is IConfigurationRoot configurationRoot)
                configurationRoot.Reload();
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
}
