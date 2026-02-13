using System.Text.RegularExpressions;
using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax.Inlines;

namespace ResearchEngine.Blazor.Services;

public static class Citations
{
    // [lrn:<guid>] (case-insensitive)
    // Accept both hyphenated GUIDs and compact 32-hex GUIDs (N format)
    private static readonly Regex LrnRegex = new(
        @"\[lrn:(?<id>(?:[0-9a-fA-F]{32}|[0-9a-fA-F]{8}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{12}))\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static IReadOnlyList<Guid> ExtractLearningIds(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return Array.Empty<Guid>();

        var set = new HashSet<Guid>();
        foreach (Match m in LrnRegex.Matches(markdown))
        {
            var s = m.Groups["id"].Value;
            if (Guid.TryParse(s, out var id))
                set.Add(id);
        }
        return set.ToList();
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
                Literal = candidate
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

            // Keep citation text visible exactly as authored: [lrn:<guid>]
            renderer.WriteEscape(obj.Literal);

            renderer.Write("</button>");
        }
    }
}
