using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace BodyCam.Models;

internal static class TranscriptMarkdownFormatter
{
    private const string CodeFontFamily = "Consolas";

    public static FormattedString ToFormattedString(string role, string markdown, Color textColor)
    {
        var formatted = new FormattedString();

        foreach (var run in ToTextRuns(role, markdown))
        {
            var span = new Span
            {
                Text = run.Text,
                TextColor = textColor,
                FontAttributes = run.FontAttributes,
                TextDecorations = run.TextDecorations
            };

            if (!string.IsNullOrEmpty(run.FontFamily))
                span.FontFamily = run.FontFamily;

            formatted.Spans.Add(span);
        }

        return formatted;
    }

    internal static IReadOnlyList<TranscriptTextRun> ToTextRuns(string role, string markdown)
    {
        var runs = new List<TranscriptTextRun>();
        var renderer = new Renderer(runs);

        renderer.AppendText($"{role}: ", FontAttributes.Bold);

        if (string.IsNullOrEmpty(markdown))
            return runs;

        try
        {
            renderer.Render(Markdown.Parse(markdown));
        }
        catch
        {
            renderer.AppendText(markdown);
        }

        return runs;
    }

    public static string ToPlainText(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;

        try
        {
            return NormalizePlainText(Markdown.ToPlainText(markdown));
        }
        catch
        {
            return markdown;
        }
    }

    private static string NormalizePlainText(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

    private sealed class Renderer
    {
        private readonly List<TranscriptTextRun> _runs;

        public Renderer(List<TranscriptTextRun> runs)
        {
            _runs = runs;
        }

        public void Render(MarkdownDocument document)
        {
            var isFirst = true;

            foreach (var block in document)
            {
                if (!isFirst)
                    AppendText("\n");

                RenderBlock(block);
                isFirst = false;
            }
        }

        private void RenderBlock(Block block)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    RenderLeafInline(heading, FontAttributes.Bold);
                    break;
                case ParagraphBlock paragraph:
                    RenderLeafInline(paragraph);
                    break;
                case ListBlock list:
                    RenderList(list);
                    break;
                case QuoteBlock quote:
                    RenderQuote(quote);
                    break;
                case CodeBlock code:
                    RenderCodeBlock(code);
                    break;
                case ThematicBreakBlock:
                    AppendText("--------");
                    break;
                case LeafBlock leaf:
                    RenderLeaf(leaf);
                    break;
                case ContainerBlock container:
                    RenderContainer(container);
                    break;
            }
        }

        private void RenderContainer(ContainerBlock container)
        {
            var isFirst = true;

            foreach (var child in container)
            {
                if (!isFirst)
                    AppendText("\n");

                RenderBlock(child);
                isFirst = false;
            }
        }

        private void RenderList(ListBlock list)
        {
            var index = list.OrderedStart;
            var itemNumber = string.IsNullOrWhiteSpace(index) || !int.TryParse(index, out var parsedStart)
                ? 1
                : parsedStart;
            var isFirst = true;

            foreach (var child in list)
            {
                if (child is not ListItemBlock item)
                    continue;

                if (!isFirst)
                    AppendText("\n");

                var marker = list.IsOrdered
                    ? $"{itemNumber++}. "
                    : "- ";

                AppendText(marker);
                RenderContainer(item);
                isFirst = false;
            }
        }

        private void RenderQuote(QuoteBlock quote)
        {
            var isFirst = true;

            foreach (var child in quote)
            {
                if (!isFirst)
                    AppendText("\n");

                AppendText("> ");
                RenderBlock(child);
                isFirst = false;
            }
        }

        private void RenderLeaf(LeafBlock leaf)
        {
            if (leaf.Inline is not null)
            {
                RenderInlines(leaf.Inline);
                return;
            }

            AppendText(leaf.Lines.ToString().TrimEnd('\r', '\n'));
        }

        private void RenderLeafInline(LeafBlock leaf, FontAttributes attributes = FontAttributes.None)
        {
            if (leaf.Inline is null)
                RenderLeaf(leaf);
            else
                RenderInlines(leaf.Inline, new SpanStyle(attributes));
        }

        private void RenderCodeBlock(CodeBlock code)
        {
            AppendStyledText(code.Lines.ToString().TrimEnd('\r', '\n'), new SpanStyle(FontFamily: CodeFontFamily));
        }

        private void RenderInlines(ContainerInline container, SpanStyle style = default)
        {
            for (var inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
                RenderInline(inline, style);
        }

        private void RenderInline(Markdig.Syntax.Inlines.Inline inline, SpanStyle style)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    AppendStyledText(literal.Content.ToString(), style);
                    break;
                case CodeInline code:
                    AppendStyledText(code.Content, style with { FontFamily = CodeFontFamily });
                    break;
                case EmphasisInline emphasis:
                    RenderInlines(emphasis, style.WithAttributes(
                        emphasis.DelimiterCount >= 2 ? FontAttributes.Bold : FontAttributes.Italic));
                    break;
                case LinkInline link:
                    RenderInlines(link, style with { TextDecorations = TextDecorations.Underline });
                    break;
                case LineBreakInline:
                    AppendStyledText("\n", style);
                    break;
                case ContainerInline container:
                    RenderInlines(container, style);
                    break;
            }
        }

        public void AppendText(string text, FontAttributes attributes = FontAttributes.None) =>
            AppendStyledText(text, new SpanStyle(attributes));

        private void AppendStyledText(string? text, SpanStyle style = default)
        {
            if (string.IsNullOrEmpty(text))
                return;

            _runs.Add(new TranscriptTextRun(
                text,
                style.FontAttributes,
                style.FontFamily,
                style.TextDecorations));
        }
    }

    private readonly record struct SpanStyle(
        FontAttributes FontAttributes = FontAttributes.None,
        string? FontFamily = null,
        TextDecorations TextDecorations = TextDecorations.None)
    {
        public SpanStyle WithAttributes(FontAttributes attributes) =>
            this with { FontAttributes = FontAttributes | attributes };
    }
}

internal readonly record struct TranscriptTextRun(
    string Text,
    FontAttributes FontAttributes = FontAttributes.None,
    string? FontFamily = null,
    TextDecorations TextDecorations = TextDecorations.None);
