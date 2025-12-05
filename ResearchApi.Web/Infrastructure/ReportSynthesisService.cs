using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
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
        IEnumerable<Learning> allLearningsForJob,
        CancellationToken ct)
    {
        var visitedUrls    = job.VisitedUrls?.Select(v => v.Url) ?? Enumerable.Empty<string>();
        var sourceIndexMap = BuildSourceIndexMap(visitedUrls, allLearningsForJob);

        var sections = await PlanSectionsAsync(job, clarificationsText, ct);

        var (mainSections, conclusionPlan) = SplitMainAndConclusion(sections);

        var sectionResults = await GenerateSectionsAsync(
            job, clarificationsText, mainSections, sourceIndexMap, ct);

        // summaries для заключения
        foreach (var sectionResult in sectionResults)
        {
            sectionResult.Summary = await GenerateSectionSummaryAsync(
                job, sectionResult, ct);
        }

        var conclusionText = await GenerateConclusionAsync(
            job, clarificationsText, sectionResults, ct);

        var rawBody = BuildReportBody(sectionResults, conclusionPlan, conclusionText);
        var bodyFixed = FixChainedCitations(rawBody);
        var finalBody = ExpandCitationsToLinks(bodyFixed, sourceIndexMap);
        var sourcesSection = BuildSourcesSection(sourceIndexMap);
        return finalBody + sourcesSection;
    }

    // 1) Планирование
    private async Task<IReadOnlyList<SectionPlan>> PlanSectionsAsync(
        ResearchJob job,
        string clarificationsText,
        CancellationToken ct)
    {
        var planningPrompt = SectionPlanningPromptFactory.BuildPlanningPrompt(
            job.Query,
            clarificationsText,
            job.TargetLanguage);

        var tokens = await llmService.TokenizePromptAsync(planningPrompt, ct);
        logger.LogDebug("[ReportSynthesis] Planning prompt tokens: {count}/{max}",
            tokens.Count, tokens.MaxModelLen);

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        var responseFormat = SectionPlanningResponse.JsonResponseSchema(jsonOptions);

        var response = await llmService.ChatAsync(
            planningPrompt,
            tools: null,
            responseFormat: responseFormat,
            cancellationToken: ct);

        var raw = llmService.StripThinkBlock(response.Text);
        logger.LogDebug("[ReportSynthesis] Planning raw response: {text}", raw);

        SectionPlanningResponse? structured = null;
        try
        {
            structured = JsonSerializer.Deserialize<SectionPlanningResponse>(raw, jsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ReportSynthesis] Failed to deserialize SectionPlanningResponse.");
        }

        var sectionPlans = structured?.Sections is { Count: > 0 }
            ? SectionPlanningPromptFactory.ToSectionPlans(structured)
            : FallbackSectionPlans();

        return sectionPlans;
    }

    private static IReadOnlyList<SectionPlan> FallbackSectionPlans() =>
        new List<SectionPlan>
        {
            new()
            {
                Title        = "Analysis",
                Description  = "Main analysis of the research question.",
                IsConclusion = false
            },
            new()
            {
                Title        = "Conclusion",
                Description  = "Final summary and answer to the main question.",
                IsConclusion = true
            }
        };

    private static (IReadOnlyList<SectionPlan> Main, SectionPlan Conclusion)
        SplitMainAndConclusion(IReadOnlyList<SectionPlan> sections)
    {
        var conclusion = sections.LastOrDefault(s => s.IsConclusion) ?? sections[^1];
        var main       = sections.Where(s => !ReferenceEquals(s, conclusion)).ToList();
        return (main, conclusion);
    }

    // 2) Генерация секций с tool
    private async Task<List<SectionResult>> GenerateSectionsAsync(
        ResearchJob job,
        string clarificationsText,
        IReadOnlyList<SectionPlan> mainSections,
        Dictionary<string,int> sourceIndexMap,
        CancellationToken ct)
    {
        var results = new List<SectionResult>();

        foreach (var section in mainSections)
        {
            logger.LogInformation("[ReportSynthesis] Writing section '{title}'", section.Title);

            var prompt = SectionWritingPromptFactory.BuildSectionPrompt(
                job.Query,
                clarificationsText,
                job.TargetLanguage ?? "en",
                section);

            var tokens = await llmService.TokenizePromptAsync(prompt, ct);
            logger.LogDebug("[ReportSynthesis] Section '{title}' prompt tokens: {count}/{max}",
                section.Title, tokens.Count, tokens.MaxModelLen);

            var toolHandler = new SynthesisToolHandler(
                learningEmbeddingService,
                sourceIndexMap,
                job.Id);

            var tool = LlmService.CreateTool(
                toolHandler.HandleGetSimilarLearningsAsync,
                "get_similar_learnings");

            var response = await llmService.ChatAsync(
                prompt,
                tools: [tool],
                cancellationToken: ct);

            logger.LogDebug("[ReportSynthesis] Raw section '{title}' response: {text}",
                section.Title, response.Text);

            var text = llmService.StripThinkBlock(response.Text).Trim();

            results.Add(new SectionResult
            {
                Plan = section,
                Text = text
            });
        }

        return results;
    }

    // 3) Summary секции (для использования в заключении)
    private async Task<string> GenerateSectionSummaryAsync(
        ResearchJob job,
        SectionResult section,
        CancellationToken ct)
    {
        var targetLang = job.TargetLanguage ?? "en";
        var prompt     = SectionSummaryPromptFactory.BuildSummaryPrompt(
            section.Plan.Title,
            section.Text,
            targetLang);

        var tokens = await llmService.TokenizePromptAsync(prompt, ct);
        logger.LogDebug("[ReportSynthesis] Section '{title}' summary prompt tokens: {count}/{max}",
            section.Plan.Title, tokens.Count, tokens.MaxModelLen);

        // если очень много токенов — можно в будущем добавить обрезку текста / extra guard

        var response = await llmService.ChatAsync(
            prompt,
            tools: null,
            cancellationToken: ct);

        var summary = llmService.StripThinkBlock(response.Text).Trim();
        return summary;
    }

    // 4) Генерация заключения
    private async Task<string> GenerateConclusionAsync(
        ResearchJob job,
        string clarificationsText,
        IReadOnlyList<SectionResult> sectionResults,
        CancellationToken ct)
    {
        var targetLang = job.TargetLanguage ?? "en";

        var prompt = ConclusionPromptFactory.BuildConclusionPrompt(
            job.Query,
            clarificationsText,
            targetLang,
            sectionResults);

        var tokens = await llmService.TokenizePromptAsync(prompt, ct);
        logger.LogDebug("[ReportSynthesis] Conclusion prompt tokens: {count}/{max}",
            tokens.Count, tokens.MaxModelLen);

        // если вдруг близко к лимиту — можно здесь добавить логику "урезать summaries"

        var response = await llmService.ChatAsync(
            prompt,
            tools: null,
            cancellationToken: ct);

        var conclusionText = llmService.StripThinkBlock(response.Text).Trim();
        return conclusionText;
    }

    // 5) Склейка отчёта
    private static string BuildReportBody(
        IReadOnlyList<SectionResult> mainSections,
        SectionPlan conclusionPlan,
        string conclusionText)
    {
        var sb = new StringBuilder();

        foreach (var section in mainSections)
        {
            if (string.IsNullOrWhiteSpace(section.Text))
                continue;

            sb.AppendLine($"## {section.Plan.Title}");
            sb.AppendLine();
            sb.AppendLine(section.Text.Trim());
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(conclusionText))
        {
            sb.AppendLine($"## {conclusionPlan.Title}");
            sb.AppendLine();
            sb.AppendLine(conclusionText.Trim());
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private  string BuildSourcesSection(Dictionary<string, int> sourceIndexMap)
    {
        if (sourceIndexMap.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        foreach (var kvp in sourceIndexMap.OrderBy(k => k.Value))
        {
            sb.AppendLine($"{kvp.Value}. {kvp.Key}");
        }

        return sb.ToString();
    }

    private static Dictionary<string, int> BuildSourceIndexMap(
        IEnumerable<string> visitedUrls,
        IEnumerable<Learning> learnings)
    {
        var map   = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var index = 1;

        if (visitedUrls != null)
        {
            foreach (var url in visitedUrls.Where(u => !string.IsNullOrWhiteSpace(u)))
            {
                if (!map.ContainsKey(url))
                    map[url] = index++;
            }
        }

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

    private string FixChainedCitations(string text)
    {
        // [6][8] -> [6], [8]
        // [6][7][8] -> [6], [7], [8]
        return Regex.Replace(
            text,
            @"\[(\d+)\]\s*\[(\d+)\]",
            "[$1], [$2]",
            RegexOptions.Multiline);
    }

    private string ExpandCitationsToLinks(
        string text,
        Dictionary<string, int> sourceIndexMap)
    {
        // index -> url
        var indexToUrl = sourceIndexMap
            .GroupBy(kvp => kvp.Value)
            .ToDictionary(g => g.Key, g => g.First().Key);

        return Regex.Replace(
            text,
            @"\[(\d+)\]",
            m =>
            {
                if (!int.TryParse(m.Groups[1].Value, out var idx))
                    return m.Value;

                if (!indexToUrl.TryGetValue(idx, out var url))
                    return m.Value;

                // превращаем [3] в [3](https://...)
                return $"[{idx}]({url})";
            });
    }
}


public sealed class SectionPlan
{
    public string Title { get; set; } = null!;
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Можно помечать, что эта секция — заключение (опционально).
    /// Пока будем считать, что последняя секция — это conclusion.
    /// </summary>
    public bool IsConclusion { get; set; }
}

public sealed class SectionResult
{
    public required SectionPlan Plan { get; init; }
    public required string Text { get; init; }
    public string? Summary { get; set; }
}

public sealed class SectionPlanItem
{
    [Description("Short, informative title of the report section.")]
    public required string Title { get; init; }

    [Description("One or two sentences describing what this section should cover.")]
    public required string Description { get; init; }
}

public sealed class SectionPlanningResponse
{
    [Description("Ordered list of planned sections for the report.")]
    public required List<SectionPlanItem> Sections { get; init; }

    public static ChatResponseFormat JsonResponseSchema(JsonSerializerOptions? jsonSerializerOptions = default)
    {
        var jsonElement = AIJsonUtilities.CreateJsonSchema(
            typeof(SectionPlanningResponse),
            description: "Structured section planning result for a research report",
            serializerOptions: jsonSerializerOptions);

        return new ChatResponseFormatJson(jsonElement);
    }
}