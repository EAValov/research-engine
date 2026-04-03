namespace ResearchEngine.WebUI.Services;

public sealed class RuntimeSettingsResponseModel
{
    public ResearchOrchestratorConfigModel ResearchOrchestratorConfig { get; set; } = new();
    public LearningSimilarityOptionsModel LearningSimilarityOptions { get; set; } = new();
    public RuntimeChatConfigModel ChatConfig { get; set; } = new();
    public RuntimeCrawlConfigModel CrawlConfig { get; set; } = new();
    public RuntimeModelInfoModel Models { get; set; } = new();
}

public sealed class UpdateRuntimeSettingsRequestModel
{
    public ResearchOrchestratorConfigModel ResearchOrchestratorConfig { get; set; } = new();
    public LearningSimilarityOptionsModel LearningSimilarityOptions { get; set; } = new();
    public UpdateChatConfigModel ChatConfig { get; set; } = new();
    public UpdateCrawlConfigModel CrawlConfig { get; set; } = new();
}

public sealed class ChatModelCatalogRequestModel
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public bool UseStoredApiKey { get; set; } = true;
}

public sealed class ChatModelCatalogResponseModel
{
    public List<string> ModelIds { get; set; } = new();
}

public sealed class ResearchOrchestratorConfigModel
{
    public int LimitSearches { get; set; }
    public int MaxUrlParallelism { get; set; }
    public int MaxUrlsPerSerpQuery { get; set; }
    public string DefaultDiscoveryMode { get; set; } = "Auto";
}

public sealed class LearningSimilarityOptionsModel
{
    public float MinImportance { get; set; }
    public int DiversityMaxPerUrl { get; set; }
    public double DiversityMaxTextSimilarity { get; set; }
    public int MaxLearningsPerSegment { get; set; }
    public int MinLearningsPerSegment { get; set; }
    public float GroupAssignSimilarityThreshold { get; set; }
    public int GroupSearchTopK { get; set; }
    public int MaxEvidenceLength { get; set; }
}

public sealed class RuntimeModelInfoModel
{
    public string ChatModelId { get; set; } = string.Empty;
    public string EmbeddingModelId { get; set; } = string.Empty;
}

public sealed class RuntimeChatConfigModel
{
    public string Endpoint { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public int? MaxContextLength { get; set; }
    public bool HasApiKey { get; set; }
}

public sealed class UpdateChatConfigModel
{
    public string Endpoint { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public int? MaxContextLength { get; set; }
}

public sealed class RuntimeCrawlConfigModel
{
    public string Endpoint { get; set; } = string.Empty;
    public bool HasApiKey { get; set; }
}

public sealed class UpdateCrawlConfigModel
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}

public sealed class CrawlApiProbeRequestModel
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public bool UseStoredApiKey { get; set; } = true;
}
