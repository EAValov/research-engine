using System.Text;
using ResearchApi.Application;
using ResearchApi.Domain;
using ResearchApi.Prompts;

namespace ResearchApi.Infrastructure;

public interface IReportSynthesisService
{
    Task<string> WriteFinalReportAsync(
        ResearchJob job,
        string clarificationsText,
        IEnumerable<Learning> learnings,
        CancellationToken ct);
}

public class ReportSynthesisService (
    ILlmService llmService,
    ILearningEmbeddingService learningEmbeddingService,
    ILogger<ReportSynthesisService> logger
) : IReportSynthesisService
{

    public async Task<string> WriteFinalReportAsync(
        ResearchJob job,
        string clarificationsText,
        IEnumerable<Learning> allLearningsForJob, // optional: if you want to inject DB learnings for map
        CancellationToken ct)
    {
        // 1) Build global citation map.
        var visitedUrls = job.VisitedUrls?.Select(v => v.Url) ?? Enumerable.Empty<string>();
        var sourceIndexMap = BuildSourceIndexMap(visitedUrls, allLearningsForJob);
        
        logger.LogInformation("VisitedURLs:{visitedUrls}, Learnings:{allLearnings}, sourceIndexMap: {sourceIndexMap}", visitedUrls.Count(), allLearningsForJob.Count(), sourceIndexMap.Count());

        var systemPrompt = SynthesisPromptFactory.BuildSystemPrompt(job.TargetLanguage);
        var userPrompt   = SynthesisPromptFactory.BuildUserPromptForTools(
            job.Query,
            clarificationsText);


        var prompt = new Prompt(systemPrompt, userPrompt);

        // 3) Create tool handler for this job/run.
        var toolHandler = new SynthesisToolHandler(
            learningEmbeddingService,
            sourceIndexMap,
            job.Id);

        var tool = LlmService.CreateTool(toolHandler.HandleGetSimilarLearningsAsync, "get_similar_learnings");
            
        var result = await llmService.ChatAsync(prompt, new [] { tool }, cancellationToken: ct );

        logger.LogDebug("[WriteFinalReportAsync] Raw response:{response}", result.Text);

        var withoutThink = llmService.StripThinkBlock(result.Text);

        // Append Sources section based on final sourceIndexMap
        var sourcesSection = BuildSourcesSection(sourceIndexMap);
        return withoutThink + sourcesSection;
    }

    static string BuildSourcesSection(Dictionary<string, int> sourceIndexMap)
    {
        if (sourceIndexMap.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("## Sources");
        sb.AppendLine();

        foreach (var kvp in sourceIndexMap.OrderBy(k => k.Value))
        {
            sb.AppendLine($"[{kvp.Value}] {kvp.Key}");
        }

        return sb.ToString();
    }

    Dictionary<string, int> BuildSourceIndexMap(
        IEnumerable<string> visitedUrls,
        IEnumerable<Learning> learnings)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var index = 1;

        // 1) From visited URLs (if you still track them)
        if (visitedUrls != null)
        {
            foreach (var url in visitedUrls.Where(u => !string.IsNullOrWhiteSpace(u)))
            {
                if (!map.ContainsKey(url))
                    map[url] = index++;
            }
        }

        // 2) From learnings (in case there are extra URLs not in visitedUrls)
        if (learnings != null)
        {
            foreach (var l in learnings)
            {
                if (string.IsNullOrWhiteSpace(l.SourceUrl)) continue;
                if (!map.ContainsKey(l.SourceUrl))
                    map[l.SourceUrl] = index++;
            }
        }

        return map;
    }
}