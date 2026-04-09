using System.Text.RegularExpressions;
using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax.Inlines;

namespace ResearchEngine.WebUI.Services;

public static class Citations
{
    private const string GuidPattern =
        @"(?:[0-9a-fA-F]{32}|[0-9a-fA-F]{8}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{12})";

    // Canonical ASCII citation syntax: [lrn:<guid>] or [lrn:<guid>|label]
    private static readonly Regex LrnRegex = new(
        $@"\[lrn:(?<id>{GuidPattern})(?:(?:\|)(?<label>[^\]]+))?\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // Tolerate bracket variations the model occasionally emits, including fullwidth CJK brackets.
    private static readonly Regex LrnLooseBracketRegex = new(
        $@"(?:\[|【)\s*lrn:(?<id>{GuidPattern})(?:(?:\|)(?<label>[^\]】]+))?\s*(?:\]|】)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // Rarely the model emits a naked lrn:<guid> token. Canonicalize it before rewrite/render.
    private static readonly Regex LrnBareRegex = new(
        $@"(?<![\[【\p{{L}}\p{{N}}_/\-])lrn:(?<id>{GuidPattern})(?![\p{{L}}\p{{N}}_])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static IReadOnlyList<Guid> ExtractLearningIds(string markdown)
    {
        var normalized = NormalizeLearningCitationMarkup(markdown);
        if (string.IsNullOrWhiteSpace(normalized))
            return Array.Empty<Guid>();

        var set = new HashSet<Guid>();
        foreach (Match m in LrnRegex.Matches(normalized))
        {
            var s = m.Groups["id"].Value;
            if (Guid.TryParse(s, out var id))
                set.Add(id);
        }
        return set.ToList();
    }

    public static IReadOnlyList<Guid> ExtractLearningIdsInAppearanceOrder(string markdown)
    {
        var normalized = NormalizeLearningCitationMarkup(markdown);
        if (string.IsNullOrWhiteSpace(normalized))
            return Array.Empty<Guid>();

        var seen = new HashSet<Guid>();
        var ordered = new List<Guid>();

        foreach (Match m in LrnRegex.Matches(normalized))
        {
            var s = m.Groups["id"].Value;
            if (!Guid.TryParse(s, out var id))
                continue;

            if (seen.Add(id))
                ordered.Add(id);
        }

        return ordered;
    }

    public static string RewriteLearningCitations(string markdown, Func<Guid, string?> labelProvider)
    {
        var normalized = NormalizeLearningCitationMarkup(markdown);
        if (string.IsNullOrWhiteSpace(normalized))
            return normalized;

        return LrnRegex.Replace(normalized, match =>
        {
            var s = match.Groups["id"].Value;
            if (!Guid.TryParse(s, out var id))
                return match.Value;

            var label = labelProvider(id);
            if (string.IsNullOrWhiteSpace(label))
                return $"[lrn:{id:D}]";

            return $"[lrn:{id:D}|{label.Trim()}]";
        });
    }

    public static string RewriteLearningCitationsToLabels(string markdown, Func<Guid, string?> labelProvider)
    {
        var normalized = NormalizeLearningCitationMarkup(markdown);
        if (string.IsNullOrWhiteSpace(normalized))
            return normalized;

        return LrnRegex.Replace(normalized, match =>
        {
            var s = match.Groups["id"].Value;
            if (!Guid.TryParse(s, out var id))
                return match.Value;

            var label = labelProvider(id);
            if (string.IsNullOrWhiteSpace(label))
                return $"[lrn:{id:D}]";

            return $"[{label.Trim()}]";
        });
    }

    public static string NormalizeLearningCitationMarkup(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return markdown ?? string.Empty;

        var normalized = LrnLooseBracketRegex.Replace(markdown, CanonicalizeMatch);
        normalized = LrnBareRegex.Replace(normalized, match =>
        {
            var s = match.Groups["id"].Value;
            if (!Guid.TryParse(s, out var id))
                return match.Value;

            return $"[lrn:{id:N}]";
        });

        return normalized;
    }

    public static MarkdownPipeline CreatePipelineWithCitations()
    {
        return new MarkdownPipelineBuilder()
            .UseAutoLinks()
            .UsePipeTables()
            .UseTaskLists()
            .DisableHtml()
            .Use(new LearningCitationExtension())
            .Build();
    }

    private sealed class LearningCitationExtension : IMarkdownExtension
    {
        public void Setup(MarkdownPipelineBuilder pipeline)
        {
            // Insert before LinkInlineParser so [lrn:...] does not become a link-like structure.
            if (!pipeline.InlineParsers.Any(p => p is LearningCitationInlineParser))
                pipeline.InlineParsers.Insert(0, new LearningCitationInlineParser());
        }

        public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
        {
            if (renderer is HtmlRenderer html)
            {
                if (!html.ObjectRenderers.Any(r => r is LearningCitationHtmlRenderer))
                    html.ObjectRenderers.Insert(0, new LearningCitationHtmlRenderer());
            }
        }
    }

    private sealed class LearningCitationInlineParser : InlineParser
    {
        public LearningCitationInlineParser()
        {
            OpeningCharacters = new[] { '[' };
        }

        public override bool Match(InlineProcessor processor, ref StringSlice slice)
        {
            // Attempt to match literal pattern from current position
            // Example: [lrn:01234567-89ab-cdef-0123-456789abcdef]
            var start = slice.Start;
            var text = slice.Text;
            if (start < 0 || start >= slice.Text.Length) return false;

            // Quick check
            if (text[start] != '[') return false;

            // Find closing ']'
            var end = text.IndexOf(']', start);
            if (end <= start) return false;

            var candidate = text.Substring(start, end - start + 1);

            var m = LrnRegex.Match(candidate);
            if (!m.Success) return false;

            if (!Guid.TryParse(m.Groups["id"].Value, out var id))
                return false;

            // Create inline
            var inline = new LearningCitationInline
            {
                LearningId = id,
                Literal = candidate,
                DisplayLabel = m.Groups["label"].Success ? m.Groups["label"].Value.Trim() : null
            };

            processor.Inline = inline;

            // Advance slice to after ']'
            slice.Start = end + 1;
            return true;
        }
    }

    private sealed class LearningCitationInline : LeafInline
    {
        public Guid LearningId { get; set; }
        public string Literal { get; set; } = "";
        public string? DisplayLabel { get; set; }
    }

    private sealed class LearningCitationHtmlRenderer : HtmlObjectRenderer<LearningCitationInline>
    {
        protected override void Write(HtmlRenderer renderer, LearningCitationInline obj)
        {
            var id = obj.LearningId.ToString();

            renderer.Write("<button type=\"button\" class=\"lrn-cite\" data-lrn=\"");
            renderer.WriteEscape(id);
            renderer.Write("\" aria-label=\"Learning citation ");
            renderer.WriteEscape(id);
            renderer.Write("\">");

            var text = string.IsNullOrWhiteSpace(obj.DisplayLabel)
                ? obj.Literal
                : $"[{obj.DisplayLabel}]";

            renderer.WriteEscape(text);

            renderer.Write("</button>");
        }
    }

    private static string CanonicalizeMatch(Match match)
    {
        var s = match.Groups["id"].Value;
        if (!Guid.TryParse(s, out var id))
            return match.Value;

        var label = match.Groups["label"].Success
            ? match.Groups["label"].Value.Trim()
            : null;

        if (string.IsNullOrWhiteSpace(label))
            return $"[lrn:{id:N}]";

        return $"[lrn:{id:N}|{label}]";
    }
}
