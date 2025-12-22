using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ResearchApi.Application;
using ResearchApi.Domain;
using ResearchApi.Prompts;

namespace ResearchApi.Infrastructure;

public sealed class ReportSynthesisService(
    IChatModel chatModel,
    ITokenizer tokenizer,
    ILearningIntelService learningIntelService,
    IResearchJobStore jobStore,
    ILogger<ReportSynthesisService> logger
) : IReportSynthesisService
{
    public async Task<Guid> StartSynthesisAsync(
        Guid jobId,
        Guid? parentSynthesisId,
        string? outline,
        string? instructions,
        CancellationToken ct)
    {
        var synthesis = await jobStore.CreateSynthesisAsync(
            jobId: jobId,
            parentSynthesisId: parentSynthesisId,
            outline: outline,
            instructions: instructions,
            ct: ct);

        return synthesis.Id;
    }

    public async Task RunExistingSynthesisAsync(Guid synthesisId, ResearchProgressTracker? progress, CancellationToken ct)
    {
        var synthesis = await jobStore.GetSynthesisAsync(synthesisId, ct)
            ?? throw new InvalidOperationException($"Synthesis {synthesisId} not found.");

        // Idempotency / retry safety
        if (synthesis.Status is SynthesisStatus.Completed or SynthesisStatus.Failed)
            return;

        progress ??= new ResearchProgressTracker(synthesis.JobId, jobStore, minEmitIntervalMs: 250);

        // New synthesis run => reset ephemeral synthesis metrics
        progress.ResetSynthesisMetrics();

        var synTag = $"syn:{synthesis.Id.ToString("N")[..8]}";

        try
        {
            await jobStore.MarkSynthesisRunningAsync(synthesis.Id, ct);

            await progress.InfoSynthesisAsync(
                ResearchEventStage.Summarizing,
                $"[{synTag}] Starting synthesis",
                ct);

            var job = await jobStore.GetJobAsync(synthesis.JobId, ct)
                ?? throw new InvalidOperationException($"Job {synthesis.JobId} not found.");

            var clarificationsText = FormatClarifications(job.Clarifications ?? Array.Empty<Clarification>());

            // Deterministic citations map ONLY from job.Sources
            var sourceIndexMap = BuildSourceIndexMap(job.Sources);

            // Use persisted values from synthesis row
            var outline = synthesis.Outline;
            var instructions = synthesis.Instructions;

            await progress.InfoSynthesisAsync(
                ResearchEventStage.Summarizing,
                $"[{synTag}][plan] Planning report sections",
                ct);

            var sections = await PlanSectionsAsync(job, clarificationsText, outline, instructions, ct);

            // Hard order everywhere: 1..N
            sections = sections
                .OrderBy(s => s.Index)
                .ToList();

            var (mainSections, conclusionPlan) = SplitMainAndConclusion(sections);

            progress.SynthesisPlanned(mainSections.Count);
            progress.SynthesisPlanCompleted();

            await progress.InfoSynthesisAsync(
                ResearchEventStage.Summarizing,
                $"[{synTag}] Planned {mainSections.Count} sections",
                ct);

            var sectionResults = await GenerateSectionsAsync(
                job,
                synthesis,
                clarificationsText,
                mainSections,
                sourceIndexMap,
                outline,
                instructions,
                progress,
                ct);

            await progress.InfoSynthesisAsync(
                ResearchEventStage.Summarizing,
                $"[{synTag}][conclusion] Writing conclusion",
                ct);

            var conclusionText = await GenerateConclusionAsync(job, clarificationsText, sectionResults, ct);
            conclusionText = NormalizeSectionBody(conclusionText);
            progress.SynthesisConclusionCompleted();

            await progress.InfoSynthesisAsync(
                ResearchEventStage.Summarizing,
                $"[{synTag}][finalize] Finalizing citations and sources",
                ct);

            var rawBody = BuildReportBody(sectionResults, conclusionPlan, conclusionText);
            var bodyFixed = FixChainedCitations(rawBody);
            var finalBody = ExpandCitationsToLinks(bodyFixed, sourceIndexMap);
            var sourcesSection = BuildSourcesSection(sourceIndexMap);

            var reportMarkdown = finalBody + sourcesSection;
            progress.SynthesisFinalized();

            await jobStore.CompleteSynthesisAsync(synthesis.Id, reportMarkdown, ct);

            await progress.InfoSynthesisAsync(
                ResearchEventStage.Completed,
                $"[{synTag}] Synthesis completed",
                ct);
        }
        catch (Exception ex)
        {
            await jobStore.FailSynthesisAsync(synthesisId, ex.Message, ct);

            await jobStore.AppendEventAsync(
                synthesis.JobId,
                new ResearchEvent(DateTimeOffset.UtcNow, ResearchEventStage.Failed,
                    $"[{synTag}] Synthesis failed: {ex.Message}"),
                ct);

            throw;
        }
    }

    // -----------------------------
    // Planning
    // -----------------------------
    private async Task<IReadOnlyList<SectionPlan>> PlanSectionsAsync(
        ResearchJob job,
        string clarificationsText,
        string? outlineOverride,
        string? instructions,
        CancellationToken ct)
    {
        var planningPrompt = SectionPlanningPromptFactory.BuildPlanningPrompt(
            query: job.Query,
            clarifications: clarificationsText,
            outline: outlineOverride,
            instructions: instructions,
            targetLanguage: job.TargetLanguage);

        var tokens = await tokenizer.TokenizePromptAsync(planningPrompt, ct);
        logger.LogDebug("[ReportSynthesis] Planning prompt tokens: {count}/{max}", tokens.Count, tokens.MaxModelLen);

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

        var raw = chatModel.StripThinkBlock(response.Text).Trim();

        SectionPlanningResponse? structured = null;
        try
        {
            structured = JsonSerializer.Deserialize<SectionPlanningResponse>(raw, jsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ReportSynthesis] Failed to deserialize SectionPlanningResponse. Raw: {raw}", raw);
        }

        var sectionPlans = structured?.ToSectionPlans() is { Count: > 0 } sp
            ? sp.ToList()
            : FallbackSectionPlans().ToList();

        // Primary enforcement: index ordering + exactly one conclusion at end
        sectionPlans = EnforceIndexOrderAndSingleConclusionAtEnd(sectionPlans);

        // If outlineOverride provided, enforce exact shape/order
        if (!string.IsNullOrWhiteSpace(outlineOverride))
        {
            sectionPlans = EnforceOutlineShape(sectionPlans, outlineOverride).ToList();
            sectionPlans = EnforceIndexOrderAndSingleConclusionAtEnd(sectionPlans);
        }

        return sectionPlans;
    }

    private static List<SectionPlan> EnforceIndexOrderAndSingleConclusionAtEnd(List<SectionPlan> plans)
    {
        if (plans.Count == 0)
            return plans;

        // Drop obviously broken entries
        plans = plans
            .Where(p => !string.IsNullOrWhiteSpace(p.Title))
            .ToList();

        if (plans.Count == 0)
            return plans;

        // Sort by Index if present, otherwise keep current order and reindex
        plans = plans
            .OrderBy(p => p.Index <= 0 ? int.MaxValue : p.Index)
            .ToList();

        // Reindex contiguously 1..N
        for (var i = 0; i < plans.Count; i++)
        {
            plans[i].Index = i + 1;
            plans[i].IsConclusion = false;
        }

        // Exactly one conclusion: last
        plans[^1].IsConclusion = true;

        return plans;
    }

    /// <summary>
    /// Defensive guardrail: outline titles are canonical shape/order.
    /// </summary>
    private static IReadOnlyList<SectionPlan> EnforceOutlineShape(IReadOnlyList<SectionPlan> modelPlans, string outline)
    {
        var outlineTitles = ParseOutlineTitles(outline);
        if (outlineTitles.Count == 0)
            return modelPlans;

        var byTitle = modelPlans
            .Where(p => !string.IsNullOrWhiteSpace(p.Title))
            .ToDictionary(p => p.Title.Trim(), p => p, StringComparer.OrdinalIgnoreCase);

        var result = new List<SectionPlan>(outlineTitles.Count);

        for (var i = 0; i < outlineTitles.Count; i++)
        {
            var title = outlineTitles[i];

            byTitle.TryGetValue(title, out var match);

            // Loose "contains" fallback
            match ??= modelPlans.FirstOrDefault(p =>
                !string.IsNullOrWhiteSpace(p.Title) &&
                p.Title.Contains(title, StringComparison.OrdinalIgnoreCase));

            var desc = match?.Description;
            if (string.IsNullOrWhiteSpace(desc))
                desc = "Describe the key points and evidence relevant to this section.";

            result.Add(new SectionPlan
            {
                Index = i + 1,
                Title = title,
                Description = desc.Trim(),
                IsConclusion = false
            });
        }

        // last is conclusion
        if (result.Count > 0)
            result[^1].IsConclusion = true;

        return result;
    }

    /// <summary>
    /// Very simple outline parser:
    /// - Accepts lines like "1. Title", "- Title", "## Title", or plain "Title"
    /// - Returns non-empty titles in order.
    /// </summary>
    private static List<string> ParseOutlineTitles(string outline)
    {
        var titles = new List<string>();

        foreach (var rawLine in outline.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            line = Regex.Replace(line, @"^(\d+\.)\s*", "");
            line = Regex.Replace(line, @"^[-*]\s*", "");
            line = Regex.Replace(line, @"^#{1,6}\s*", "");

            line = line.Trim();
            if (line.Length == 0)
                continue;

            titles.Add(line);
        }

        return titles;
    }

    private static IReadOnlyList<SectionPlan> FallbackSectionPlans() =>
        new List<SectionPlan>
        {
            new() { Index = 1, Title = "Analysis", Description = "Main analysis of the research question.", IsConclusion = false },
            new() { Index = 2, Title = "Conclusion", Description = "Final summary and answer to the main question.", IsConclusion = true }
        };

    private static (IReadOnlyList<SectionPlan> Main, SectionPlan Conclusion)
        SplitMainAndConclusion(IReadOnlyList<SectionPlan> sections)
    {
        if (sections.Count == 0)
        {
            return (
                Array.Empty<SectionPlan>(),
                new SectionPlan { Index = 1, Title = "Conclusion", Description = "", IsConclusion = true }
            );
        }

        var ordered = sections.OrderBy(s => s.Index).ToList();
        var conclusion = ordered.LastOrDefault(s => s.IsConclusion) ?? ordered[^1];

        var main = ordered
            .Where(s => !ReferenceEquals(s, conclusion))
            .OrderBy(s => s.Index)
            .ToList();

        return (main, conclusion);
    }

    // -----------------------------
    // Generate sections (tool)
    // -----------------------------
    private async Task<List<SectionResult>> GenerateSectionsAsync(
        ResearchJob job,
        Synthesis synthesis,
        string clarificationsText,
        IReadOnlyList<SectionPlan> mainSections,
        Dictionary<string, int> sourceIndexMap,
        string? outline,
        string? instructions,
        ResearchProgressTracker progress,
        CancellationToken ct)
    {
        var results = new List<SectionResult>();
        var synTag = $"syn:{synthesis.Id.ToString("N")[..8]}";

        // Always iterate in explicit order
        var ordered = mainSections.OrderBy(s => s.Index).ToList();

        for (int i = 0; i < ordered.Count; i++)
        {
            var section = ordered[i];
            progress.SynthesisSectionStarted(section.Title);

            var prompt = SectionWritingPromptFactory.BuildSectionPrompt(
                query: job.Query,
                clarifications: clarificationsText,
                targetLanguage: job.TargetLanguage ?? "en",
                section: section,
                outline: outline,
                instructions: instructions);

            var tokens = await tokenizer.TokenizePromptAsync(prompt, ct);
            logger.LogDebug("[ReportSynthesis] Section '{title}' prompt tokens: {count}/{max}",
                section.Title, tokens.Count, tokens.MaxModelLen);

            var toolHandler = new SynthesisToolHandler(
                learningIntelService,
                synthesis.Id,
                ResearchOrchestrator.ComputeSha256(job.Query),
                sourceIndexMap,
                job.Region,
                job.TargetLanguage);

            var tool = chatModel.CreateTool(toolHandler.HandleGetSimilarLearningsAsync, "get_similar_learnings");

            var response = await chatModel.ChatAsync(
                prompt,
                tools: [tool],
                cancellationToken: ct);

            var text = chatModel.StripThinkBlock(response.Text).Trim();

            // validation + repair
            var needsRepair =
                !ContainsAnyCitationMarker(text) ||
                ContainsChainedCitations(text) ||
                (ContainsMermaidBlock(text) && MermaidHasForbiddenNodeLabelChars(text));

            var repairedApplied = false;

            if (needsRepair)
            {
                await progress.InfoSynthesisAsync(
                    ResearchEventStage.Summarizing,
                    $"[{synTag}][section {section.Index}/{ordered.Count}] Repairing invalid output",
                    ct);

                var repairPrompt = SectionUnifiedRepairPromptFactory.BuildRepairPrompt(
                    targetLanguage: job.TargetLanguage ?? "en",
                    section: section,
                    badDraft: text);

                var repaired = await chatModel.ChatAsync(repairPrompt, cancellationToken: ct);
                var repairedText = chatModel.StripThinkBlock(repaired.Text).Trim();

                var okCitations = ContainsAnyCitationMarker(repairedText) && !ContainsChainedCitations(repairedText);
                var okMermaid = !ContainsMermaidBlock(repairedText) || !MermaidHasForbiddenNodeLabelChars(repairedText);

                if (okCitations && okMermaid)
                {
                    text = repairedText;
                    repairedApplied = true;
                }
            }

            // Prevent “mixed sections” caused by model-injected headings
            text = NormalizeSectionBody(text);

            results.Add(new SectionResult { Plan = section, Text = text });
            progress.SynthesisSectionWritten(repaired: repairedApplied);

            await progress.InfoSynthesisAsync(
                ResearchEventStage.Summarizing,
                $"[{synTag}][section {section.Index}/{ordered.Count}] Summarizing '{section.Title}'",
                ct);

            results[^1].Summary = await GenerateSectionSummaryAsync(job, results[^1], ct);
            progress.SynthesisSectionSummarized();
        }

        // Keep deterministic order in results too
        return results.OrderBy(r => r.Plan.Index).ToList();
    }

    private async Task<string> GenerateSectionSummaryAsync(
        ResearchJob job,
        SectionResult section,
        CancellationToken ct)
    {
        var targetLang = job.TargetLanguage ?? "en";

        var prompt = SectionSummaryPromptFactory.BuildSummaryPrompt(
            section.Plan.Title,
            section.Text,
            targetLang);

        var tokens = await tokenizer.TokenizePromptAsync(prompt, ct);
        logger.LogDebug("[ReportSynthesis] Section summary '{title}' prompt tokens: {count}/{max}",
            section.Plan.Title, tokens.Count, tokens.MaxModelLen);

        var response = await chatModel.ChatAsync(prompt, tools: null, cancellationToken: ct);
        return chatModel.StripThinkBlock(response.Text).Trim();
    }

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
            sectionResults.OrderBy(s => s.Plan.Index).ToList());

        var tokens = await tokenizer.TokenizePromptAsync(prompt, ct);
        logger.LogDebug("[ReportSynthesis] Conclusion prompt tokens: {count}/{max}", tokens.Count, tokens.MaxModelLen);

        var response = await chatModel.ChatAsync(prompt, tools: null, cancellationToken: ct);
        return chatModel.StripThinkBlock(response.Text).Trim();
    }

    // -----------------------------
    // Stitch / citations / sources
    // -----------------------------
    private static string BuildReportBody(
        IReadOnlyList<SectionResult> mainSections,
        SectionPlan conclusionPlan,
        string conclusionText)
    {
        var sb = new StringBuilder();

        foreach (var section in mainSections.OrderBy(s => s.Plan.Index))
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

    private static string FixChainedCitations(string text) =>
        Regex.Replace(text, @"\[(\d+)\]\s*\[(\d+)\]", "[$1], [$2]", RegexOptions.Multiline);

    private static string ExpandCitationsToLinks(string text, Dictionary<string, int> sourceIndexMap)
    {
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

    private static string BuildSourcesSection(Dictionary<string, int> sourceIndexMap)
    {
        if (sourceIndexMap.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        foreach (var kvp in sourceIndexMap.OrderBy(k => k.Value))
            sb.AppendLine($"{kvp.Value}. {kvp.Key}");

        return sb.ToString();
    }

    private static Dictionary<string, int> BuildSourceIndexMap(IEnumerable<Source>? sources)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var index = 1;

        if (sources is null)
            return map;

        foreach (var s in sources)
        {
            if (string.IsNullOrWhiteSpace(s.Url))
                continue;

            if (!map.ContainsKey(s.Url))
                map[s.Url] = index++;
        }

        return map;
    }

    private static string FormatClarifications(IEnumerable<Clarification> clarifications)
    {
        var list = clarifications.ToList();
        if (list.Count == 0)
            return "No additional clarifications were provided.";

        var sb = new StringBuilder();
        sb.AppendLine("Additional context from user clarifications:");
        foreach (var c in list)
        {
            sb.AppendLine($"Q: {c.Question}");
            sb.AppendLine($"A: {c.Answer}");
            sb.AppendLine();
        }
        return sb.ToString().Trim();
    }

    // -----------------------------
    // Body normalization (prevents “mixed sections”)
    // -----------------------------
    private static string NormalizeSectionBody(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Remove leading markdown headings (the stitcher adds headings)
        text = Regex.Replace(
            text,
            @"\A\s*#{1,6}\s+.*(?:\r\n|\n|\r)+",
            "",
            RegexOptions.Multiline);

        // Remove a single short "title-like" first line (optional)
        text = Regex.Replace(text, @"\A\s*([^\r\n]{1,80})(?:\r\n|\n|\r)+", m =>
        {
            var line = m.Groups[1].Value.Trim();
            // If it looks like a standalone title line, drop it once
            return line.Length is > 0 and <= 80 ? "" : m.Value;
        });

        return text.Trim();
    }

    // -----------------------------
    // Validation helpers
    // -----------------------------
    private static bool ContainsAnyCitationMarker(string text)
        => !string.IsNullOrWhiteSpace(text) && Regex.IsMatch(text, @"\[(\d{1,4})\]");

    private static bool ContainsChainedCitations(string text)
        => !string.IsNullOrWhiteSpace(text) && Regex.IsMatch(text, @"\[(\d{1,4})\]\s*\[(\d{1,4})\]");

    private static bool ContainsMermaidBlock(string text)
        => !string.IsNullOrWhiteSpace(text) &&
           Regex.IsMatch(text, @"```mermaid\s*[\s\S]*?```", RegexOptions.IgnoreCase);

    private static bool MermaidHasForbiddenNodeLabelChars(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        foreach (Match m in Regex.Matches(text, @"```mermaid\s*([\s\S]*?)```", RegexOptions.IgnoreCase))
        {
            var mermaid = m.Groups[1].Value;

            // Find node labels like A[Label text]
            foreach (Match labelMatch in Regex.Matches(mermaid, @"\[[^\]]*\]"))
            {
                var labelWithBrackets = labelMatch.Value; // "[Label (bad)]"
                var label = labelWithBrackets[1..^1];     // "Label (bad)"

                if (label.Contains('(') || label.Contains(')'))
                    return true;
            }
        }

        return false;
    }
}