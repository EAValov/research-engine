namespace ResearchEngine.WebUI.Services;

public sealed class RuntimeSettingsResponseModel
{
    public ResearchOrchestratorConfigModel ResearchOrchestratorConfig { get; set; } = new();
    public LearningSimilarityOptionsModel LearningSimilarityOptions { get; set; } = new();
    public RuntimeModelInfoModel Models { get; set; } = new();
}

public sealed class UpdateRuntimeSettingsRequestModel
{
    public ResearchOrchestratorConfigModel ResearchOrchestratorConfig { get; set; } = new();
    public LearningSimilarityOptionsModel LearningSimilarityOptions { get; set; } = new();
}

public sealed class ResearchOrchestratorConfigModel
{
    public int LimitSearches { get; set; }
    public int MaxUrlParallelism { get; set; }
    public int MaxUrlsPerSerpQuery { get; set; }
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
