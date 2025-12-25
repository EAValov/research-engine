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
        // outline is now expected to be strict JSON (SynthesisOutline) or null.
        // We store as-is; validation happens in RunExistingSynthesisAsync.
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

            // Use persisted values from synthesis row
            var outlineJson = synthesis.Outline;
            var instructions = synthesis.Instructions;

            // Load parent sections (for reuse + key mapping)
            IReadOnlyList<SynthesisSection> parentSections = Array.Empty<SynthesisSection>();
            if (synthesis.ParentSynthesisId is Guid parentId)
            {
                var parent = await jobStore.GetSynthesisAsync(parentId, ct);
                if (parent?.Sections is { Count: > 0 })
                    parentSections = parent.Sections.OrderBy(s => s.Index).ToList();
            }

            await progress.InfoSynthesisAsync(
                ResearchEventStage.Summarizing,
                $"[{synTag}][plan] Planning report sections",
                ct);

            var plans = await PlanSectionsAsync(
                job: job,
                clarificationsText: clarificationsText,
                outlineJson: outlineJson,
                instructions: instructions,
                parentSections: parentSections,
                ct: ct);

            // hard order
            plans = plans.OrderBy(p => p.Index).ToList();

            var (mainPlans, conclusionPlan) = SplitMainAndConclusion(plans);

            progress.SynthesisPlanned(mainPlans.Count);
            progress.SynthesisPlanCompleted();

            await progress.InfoSynthesisAsync(
                ResearchEventStage.Summarizing,
                $"[{synTag}] Planned {mainPlans.Count} sections",
                ct);

            // If we have a parent and NO instructions => reuse section bodies for same keys.
            // (Once you implement “change planning”, this reuse decision can become per-section.)
            var allowReuseFromParent = !string.IsNullOrWhiteSpace(synthesis.ParentSynthesisId?.ToString())
                                       && string.IsNullOrWhiteSpace(instructions)
                                       && parentSections.Count > 0;

            var parentByKey = parentSections
                .GroupBy(s => s.SectionKey)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CreatedAt).First());

            var sectionResults = await GenerateSectionsAsync(
                job: job,
                synthesis: synthesis,
                clarificationsText: clarificationsText,
                mainSections: mainPlans,
                instructions: instructions,
                progress: progress,
                allowReuseFromParent: allowReuseFromParent,
                parentByKey: parentByKey,
                ct: ct);

            await progress.InfoSynthesisAsync(
                ResearchEventStage.Summarizing,
                $"[{synTag}][conclusion] Writing conclusion",
                ct);

            var conclusionText = await GenerateConclusionAsync(
                job: job,
                clarificationsText: clarificationsText,
                sectionResults: sectionResults,
                ct: ct);

            progress.SynthesisConclusionCompleted();

            // Build final section rows (including conclusion as a stored section)
            var sectionsToPersist = new List<SynthesisSection>();

            foreach (var sr in sectionResults.OrderBy(r => r.Plan.Index))
            {
                sectionsToPersist.Add(new SynthesisSection
                {
                    Id = Guid.NewGuid(),
                    SynthesisId = synthesis.Id,
                    SectionKey = sr.Plan.SectionKey,
                    Index = sr.Plan.Index - 1, // DB is 0-based
                    Title = sr.Plan.Title,
                    Description = sr.Plan.Description ?? string.Empty,
                    IsConclusion = false,
                    ContentMarkdown = sr.Text ?? string.Empty,
                    Summary = sr.Summary,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }

            sectionsToPersist.Add(new SynthesisSection
            {
                Id = Guid.NewGuid(),
                SynthesisId = synthesis.Id,
                SectionKey = conclusionPlan.SectionKey,
                Index = conclusionPlan.Index - 1,
                Title = conclusionPlan.Title,
                Description = conclusionPlan.Description ?? string.Empty,
                IsConclusion = true,
                ContentMarkdown = conclusionText ?? string.Empty,
                Summary = null,
                CreatedAt = DateTimeOffset.UtcNow
            });

            // Normalize ordering again defensively
            sectionsToPersist = sectionsToPersist
                .OrderBy(s => s.Index)
                .ToList();

            await progress.InfoSynthesisAsync(
                ResearchEventStage.Summarizing,
                $"[{synTag}][finalize] Finalizing citations and sources",
                ct);

            progress.SynthesisFinalized();

            // Persist sections atomically and mark completed
            await jobStore.CompleteSynthesisAsync(synthesis.Id, sectionsToPersist, ct);

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
    private async Task<List<SectionPlan>> PlanSectionsAsync(
        ResearchJob job,
        string clarificationsText,
        string? outlineJson,
        string? instructions,
        IReadOnlyList<SynthesisSection> parentSections,
        CancellationToken ct)
    {
        // 1) If strict outline JSON provided => authoritative, no LLM planner call.
        if (SynthesisOutline.TryParse(outlineJson, out var outline))
        {
            var fromOutline = OutlineToPlans(outline!);
            
            // ensure keys/index/conclusion normalized
            fromOutline = EnforceIndexOrderAndSingleConclusionAtEnd(fromOutline);
            return fromOutline;
        }

        // 2) Otherwise use LLM planner as before
        var planningPrompt = SectionPlanningPromptFactory.BuildPlanningPrompt(
            query: job.Query,
            clarifications: clarificationsText,
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

        var plans = structured?.ToSectionPlans() is { Count: > 0 } sp
            ? sp.ToList()
            : FallbackSectionPlans().ToList();

        // Normalize order + single conclusion + contiguous indices
        plans = EnforceIndexOrderAndSingleConclusionAtEnd(plans);

        // Assign stable SectionKey based on parent titles when possible (otherwise new Guid)
        plans = AssignSectionKeysFromParentByTitle(plans, parentSections);

        return plans;
    }



    private static List<SectionPlan> OutlineToPlans(SynthesisOutline outline)
    {
        var result = new List<SectionPlan>();

        if (outline.Sections is null || outline.Sections.Count == 0)
            return result;

        foreach (var s in outline.Sections)
        {
            if (string.IsNullOrWhiteSpace(s.Title))
                continue;

            var key = s.SectionKey ?? Guid.NewGuid(); // null => new section identity

            result.Add(new SectionPlan
            {
                SectionKey = key,
                Index = s.Index,
                Title = s.Title.Trim(),
                Description = s.Description?.Trim() ?? string.Empty,
                IsConclusion = s.IsConclusion
            });
        }

        return result;
    }

    private static List<SectionPlan> AssignSectionKeysFromParentByTitle(
        List<SectionPlan> plans,
        IReadOnlyList<SynthesisSection> parentSections)
    {
        if (plans.Count == 0 || parentSections.Count == 0)
        {
            // ensure keys exist
            foreach (var p in plans)
                if (p.SectionKey == Guid.Empty) p.SectionKey = Guid.NewGuid();
            return plans;
        }

        // Simple title matching to reuse existing keys in absence of explicit outline keys
        var parentByTitle = parentSections
            .Where(s => !string.IsNullOrWhiteSpace(s.Title))
            .GroupBy(s => s.Title.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var p in plans)
        {
            if (p.SectionKey != Guid.Empty)
                continue;

            if (parentByTitle.TryGetValue(p.Title.Trim(), out var parent))
            {
                p.SectionKey = parent.SectionKey;
                continue;
            }

            // looser contains match
            var loose = parentSections.FirstOrDefault(ps =>
                !string.IsNullOrWhiteSpace(ps.Title) &&
                (ps.Title.Contains(p.Title, StringComparison.OrdinalIgnoreCase) ||
                 p.Title.Contains(ps.Title, StringComparison.OrdinalIgnoreCase)));

            p.SectionKey = loose?.SectionKey ?? Guid.NewGuid();
        }

        return plans;
    }

    private static List<SectionPlan> EnforceIndexOrderAndSingleConclusionAtEnd(List<SectionPlan> plans)
    {
        if (plans.Count == 0)
            return plans;

        plans = plans
            .Where(p => !string.IsNullOrWhiteSpace(p.Title))
            .ToList();

        if (plans.Count == 0)
            return plans;

        // Keep current order if indexes are broken; otherwise sort by Index.
        // We still re-number contiguously 1..N.
        var hasValidIndices = plans.All(p => p.Index > 0);
        plans = hasValidIndices
            ? plans.OrderBy(p => p.Index).ToList()
            : plans.ToList();

        for (var i = 0; i < plans.Count; i++)
        {
            plans[i].Index = i + 1;
            plans[i].IsConclusion = false;

            if (plans[i].SectionKey == Guid.Empty)
                plans[i].SectionKey = Guid.NewGuid();
        }

        plans[^1].IsConclusion = true;
        return plans;
    }

    private static IReadOnlyList<SectionPlan> FallbackSectionPlans() =>
        new List<SectionPlan>
        {
            new() { SectionKey = Guid.NewGuid(), Index = 1, Title = "Analysis", Description = "Main analysis of the research question.", IsConclusion = false },
            new() { SectionKey = Guid.NewGuid(), Index = 2, Title = "Conclusion", Description = "Final summary and answer to the main question.", IsConclusion = true }
        };

    private static (IReadOnlyList<SectionPlan> Main, SectionPlan Conclusion) SplitMainAndConclusion(IReadOnlyList<SectionPlan> sections)
    {
        if (sections.Count == 0)
        {
            return (
                Array.Empty<SectionPlan>(),
                new SectionPlan { SectionKey = Guid.NewGuid(), Index = 1, Title = "Conclusion", Description = "", IsConclusion = true }
            );
        }

        var ordered = sections.OrderBy(s => s.Index).ToList();
        var conclusion = ordered.LastOrDefault(s => s.IsConclusion) ?? ordered[^1];

        // Ensure last is conclusion
        for (var i = 0; i < ordered.Count; i++)
            ordered[i].IsConclusion = false;
        ordered[^1].IsConclusion = true;
        conclusion = ordered[^1];

        var main = ordered
            .Where(s => !ReferenceEquals(s, conclusion))
            .OrderBy(s => s.Index)
            .ToList();

        return (main, conclusion);
    }

    // -----------------------------
    // Generate sections
    // -----------------------------
    private async Task<List<SectionResult>> GenerateSectionsAsync(
        ResearchJob job,
        Synthesis synthesis,
        string clarificationsText,
        IReadOnlyList<SectionPlan> mainSections,
        string? instructions,
        ResearchProgressTracker progress,
        bool allowReuseFromParent,
        Dictionary<Guid, SynthesisSection> parentByKey,
        CancellationToken ct)
    {
        var results = new List<SectionResult>();
        var synTag = $"syn:{synthesis.Id.ToString("N")[..8]}";

        var ordered = mainSections.OrderBy(s => s.Index).ToList();

        for (int i = 0; i < ordered.Count; i++)
        {
            var section = ordered[i];
            progress.SynthesisSectionStarted(section.Title);

            // Fast reuse path (only when instructions are empty)
            if (allowReuseFromParent &&
                parentByKey.TryGetValue(section.SectionKey, out var prev) &&
                !string.IsNullOrWhiteSpace(prev.ContentMarkdown))
            {
                results.Add(new SectionResult { Plan = section, Text = prev.ContentMarkdown, Summary = prev.Summary });

                progress.SynthesisSectionWritten(repaired: false);

                continue;
            }

            var prompt = SectionWritingPromptFactory.BuildSectionPrompt(
                query: job.Query,
                clarifications: clarificationsText,
                targetLanguage: job.TargetLanguage ?? "en",
                section: section,
                instructions: instructions);

            var tokens = await tokenizer.TokenizePromptAsync(prompt, ct);
            logger.LogDebug("[ReportSynthesis] Section '{title}' prompt tokens: {count}/{max}",
                section.Title, tokens.Count, tokens.MaxModelLen);

            var toolHandler = new SynthesisToolHandler(
                learningIntelService,
                synthesis.Id,
                ResearchOrchestrator.ComputeSha256(job.Query),
                job.Region,
                job.TargetLanguage);

            var tool = chatModel.CreateTool(toolHandler.HandleGetSimilarLearningsAsync, "get_similar_learnings");

            var response = await chatModel.ChatAsync(
                prompt,
                tools: [tool],
                cancellationToken: ct);

            var text = chatModel.StripThinkBlock(response.Text).Trim();

            results.Add(new SectionResult { Plan = section, Text = text });
            progress.SynthesisSectionWritten(repaired: false);

            await progress.InfoSynthesisAsync(
                ResearchEventStage.Summarizing,
                $"[{synTag}][section {section.Index}/{ordered.Count}] Summarizing '{section.Title}'",
                ct);

            results[^1].Summary = await GenerateSectionSummaryAsync(job, results[^1], ct);
            progress.SynthesisSectionSummarized();
        }

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
}