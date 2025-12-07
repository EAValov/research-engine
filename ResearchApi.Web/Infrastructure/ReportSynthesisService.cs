using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ResearchApi.Domain;
using ResearchApi.Prompts;

namespace ResearchApi.Infrastructure;

public class ReportSynthesisService (
    IChatModel chatModel,
    ITokenizer tokenizer,
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

        var tokens = await tokenizer.TokenizePromptAsync(planningPrompt, ct);
        logger.LogDebug("[ReportSynthesis] Planning prompt tokens: {count}/{max}",
            tokens.Count, tokens.MaxModelLen);

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        var responseFormat = SectionPlanningResponse.JsonResponseSchema(jsonOptions);

        var response = await chatModel.ChatAsync(
            planningPrompt,
            tools: null,
            responseFormat: responseFormat,
            cancellationToken: ct);

        var raw = chatModel.StripThinkBlock(response.Text);
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
            ? structured.ToSectionPlans()
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

            var tokens = await tokenizer.TokenizePromptAsync(prompt, ct);
            logger.LogDebug("[ReportSynthesis] Section '{title}' prompt tokens: {count}/{max}",
                section.Title, tokens.Count, tokens.MaxModelLen);

            var toolHandler = new SynthesisToolHandler(
                learningEmbeddingService,
                sourceIndexMap,
                job.Id);

            var tool = chatModel.CreateTool(
                toolHandler.HandleGetSimilarLearningsAsync,
                "get_similar_learnings");

            var response = await chatModel.ChatAsync(
                prompt,
                tools: [tool],
                cancellationToken: ct);

            var text = chatModel.StripThinkBlock(response.Text).Trim();

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

        var tokens = await tokenizer.TokenizePromptAsync(prompt, ct);
        logger.LogDebug("[ReportSynthesis] Section '{title}' summary prompt tokens: {count}/{max}",
            section.Plan.Title, tokens.Count, tokens.MaxModelLen);

        var response = await chatModel.ChatAsync(
            prompt,
            tools: null,
            cancellationToken: ct);

        var summary = chatModel.StripThinkBlock(response.Text).Trim();
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

        var tokens = await tokenizer.TokenizePromptAsync(prompt, ct);
        logger.LogDebug("[ReportSynthesis] Conclusion prompt tokens: {count}/{max}",
            tokens.Count, tokens.MaxModelLen);

        // если вдруг близко к лимиту — можно здесь добавить логику "урезать summaries"

        var response = await chatModel.ChatAsync(
            prompt,
            tools: null,
            cancellationToken: ct);

        var conclusionText = chatModel.StripThinkBlock(response.Text).Trim();
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

                return $"[{idx}]({url})";
            });
    }
}