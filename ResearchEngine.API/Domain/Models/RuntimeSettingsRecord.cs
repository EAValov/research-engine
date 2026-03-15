using ResearchEngine.Configuration;

namespace ResearchEngine.Domain;

public sealed class RuntimeSettingsRecord
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public int LimitSearches { get; set; }
    public int MaxUrlParallelism { get; set; }
    public int MaxUrlsPerSerpQuery { get; set; }

    public float MinImportance { get; set; }
    public int DiversityMaxPerUrl { get; set; }
    public double DiversityMaxTextSimilarity { get; set; }
    public int MaxLearningsPerSegment { get; set; }
    public int MinLearningsPerSegment { get; set; }
    public float GroupAssignSimilarityThreshold { get; set; }
    public int GroupSearchTopK { get; set; }
    public int MaxEvidenceLength { get; set; }

    public string ChatEndpoint { get; set; } = null!;
    public string ChatApiKey { get; set; } = null!;
    public string ChatModelId { get; set; } = null!;
    public int? ChatMaxContextLength { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public RuntimeSettingsSnapshot ToSnapshot()
        => new(
            new ResearchOrchestratorConfig
            {
                LimitSearches = LimitSearches,
                MaxUrlParallelism = MaxUrlParallelism,
                MaxUrlsPerSerpQuery = MaxUrlsPerSerpQuery
            },
            new LearningSimilarityOptions
            {
                MinImportance = MinImportance,
                DiversityMaxPerUrl = DiversityMaxPerUrl,
                DiversityMaxTextSimilarity = DiversityMaxTextSimilarity,
                MaxLearningsPerSegment = MaxLearningsPerSegment,
                MinLearningsPerSegment = MinLearningsPerSegment,
                GroupAssignSimilarityThreshold = GroupAssignSimilarityThreshold,
                GroupSearchTopK = GroupSearchTopK,
                MaxEvidenceLength = MaxEvidenceLength
            },
            new ChatConfig
            {
                Endpoint = ChatEndpoint,
                ApiKey = ChatApiKey,
                ModelId = ChatModelId,
                MaxContextLength = ChatMaxContextLength
            });

    public static RuntimeSettingsRecord FromSnapshot(RuntimeSettingsSnapshot snapshot)
        => new()
        {
            Id = SingletonId,
            LimitSearches = snapshot.ResearchOrchestratorConfig.LimitSearches,
            MaxUrlParallelism = snapshot.ResearchOrchestratorConfig.MaxUrlParallelism,
            MaxUrlsPerSerpQuery = snapshot.ResearchOrchestratorConfig.MaxUrlsPerSerpQuery,
            MinImportance = snapshot.LearningSimilarityOptions.MinImportance,
            DiversityMaxPerUrl = snapshot.LearningSimilarityOptions.DiversityMaxPerUrl,
            DiversityMaxTextSimilarity = snapshot.LearningSimilarityOptions.DiversityMaxTextSimilarity,
            MaxLearningsPerSegment = snapshot.LearningSimilarityOptions.MaxLearningsPerSegment,
            MinLearningsPerSegment = snapshot.LearningSimilarityOptions.MinLearningsPerSegment,
            GroupAssignSimilarityThreshold = snapshot.LearningSimilarityOptions.GroupAssignSimilarityThreshold,
            GroupSearchTopK = snapshot.LearningSimilarityOptions.GroupSearchTopK,
            MaxEvidenceLength = snapshot.LearningSimilarityOptions.MaxEvidenceLength,
            ChatEndpoint = snapshot.ChatConfig.Endpoint,
            ChatApiKey = snapshot.ChatConfig.ApiKey,
            ChatModelId = snapshot.ChatConfig.ModelId,
            ChatMaxContextLength = snapshot.ChatConfig.MaxContextLength,
            UpdatedAt = DateTimeOffset.UtcNow
        };

    public void Apply(RuntimeSettingsSnapshot snapshot)
    {
        LimitSearches = snapshot.ResearchOrchestratorConfig.LimitSearches;
        MaxUrlParallelism = snapshot.ResearchOrchestratorConfig.MaxUrlParallelism;
        MaxUrlsPerSerpQuery = snapshot.ResearchOrchestratorConfig.MaxUrlsPerSerpQuery;

        MinImportance = snapshot.LearningSimilarityOptions.MinImportance;
        DiversityMaxPerUrl = snapshot.LearningSimilarityOptions.DiversityMaxPerUrl;
        DiversityMaxTextSimilarity = snapshot.LearningSimilarityOptions.DiversityMaxTextSimilarity;
        MaxLearningsPerSegment = snapshot.LearningSimilarityOptions.MaxLearningsPerSegment;
        MinLearningsPerSegment = snapshot.LearningSimilarityOptions.MinLearningsPerSegment;
        GroupAssignSimilarityThreshold = snapshot.LearningSimilarityOptions.GroupAssignSimilarityThreshold;
        GroupSearchTopK = snapshot.LearningSimilarityOptions.GroupSearchTopK;
        MaxEvidenceLength = snapshot.LearningSimilarityOptions.MaxEvidenceLength;

        ChatEndpoint = snapshot.ChatConfig.Endpoint;
        ChatApiKey = snapshot.ChatConfig.ApiKey;
        ChatModelId = snapshot.ChatConfig.ModelId;
        ChatMaxContextLength = snapshot.ChatConfig.MaxContextLength;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
