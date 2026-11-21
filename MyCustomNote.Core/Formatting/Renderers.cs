using System.Text;

namespace MyCustomNote.Core.Formatting;

/// <summary>
/// Plain text with line prefixes for the paragraph kinds: "# " heading, "- " bullet, "1. " numbered.
/// Style spans are dropped (plain text has no styles).
/// </summary>
public static class PlainTextRenderer
{
    public static string Render(IReadOnlyList<StyledLine> lines) =>
        string.Join("\n", lines.Select(line => Prefix(line) + line.PlainText));

    public static string Render(string text, IEnumerable<StyleSpan> spans, IEnumerable<ParagraphFormat>? paragraphs = null) =>
        Render(NoteLayout.Build(text, spans, paragraphs));

    internal static string Prefix(StyledLine line) => line.Kind switch
    {
        ParagraphKind.Heading => "# ",
        ParagraphKind.Bullet => "- ",
        ParagraphKind.Numbered => $"{line.Number}. ",
        _ => ""
    };
}

/// <summary>
/// Markdown from styled lines: **bold**, *italic*, ~~strikethrough~~, &lt;u&gt;underline&lt;/u&gt;, "# ", "- " and
/// "1. " prefixes. Text characters that would be read as Markdown are escaped with a backslash. Spaces at the
/// edge of a styled run are moved outside the markers (Markdown does not accept "** bold **"). Two normal
/// text lines in a row are joined with a hard line break (two trailing spaces) so they stay separate lines.
/// Known limits: Markdown cannot say everything plain text can (for example leading indentation), so JSON is
/// the lossless format for moving notes between the apps.
/// </summary>
public static class MarkdownRenderer
{
    public static string Render(IReadOnlyList<StyledLine> lines)
    {
        var builder = new StringBuilder();
        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                builder.Append('\n');
            }

            var line = lines[i];
            builder.Append(PlainTextRenderer.Prefix(line));
            builder.Append(EscapeLineStart(RenderRuns(line)));

            if (IsPlainTextLine(line) && i + 1 < lines.Count && IsPlainTextLine(lines[i + 1]))
            {
                builder.Append("  ");
            }
        }

        return builder.ToString();
    }

    public static string Render(string text, IEnumerable<StyleSpan> spans, IEnumerable<ParagraphFormat>? paragraphs = null) =>
        Render(NoteLayout.Build(text, spans, paragraphs));

    private static bool IsPlainTextLine(StyledLine line) => line.Kind == ParagraphKind.Normal && line.Runs.Count > 0;

    private static string RenderRuns(StyledLine line)
    {
        var builder = new StringBuilder();
        foreach (var run in line.Runs)
        {
            string text = run.Text;
            int lead = 0;
            while (lead < text.Length && char.IsWhiteSpace(text[lead]))
            {
                lead++;
            }

            if (lead == text.Length)
            {
                builder.Append(text); // only spaces: nothing to style
                continue;
            }

            int trail = 0;
            while (char.IsWhiteSpace(text[text.Length - 1 - trail]))
            {
                trail++;
            }

            string core = text.Substring(lead, text.Length - lead - trail);
            builder.Append(text, 0, lead);
            builder.Append(Open(run.Styles)).Append(Escape(core)).Append(Close(run.Styles));
            builder.Append(text, text.Length - trail, trail);
        }

        return builder.ToString();
    }

    private static string Open(TextStyles styles) =>
        (styles.HasFlag(TextStyles.Underline) ? "<u>" : "")
        + (styles.HasFlag(TextStyles.Strikethrough) ? "~~" : "")
        + (styles.HasFlag(TextStyles.Bold) ? "**" : "")
        + (styles.HasFlag(TextStyles.Italic) ? "*" : "");

    private static string Close(TextStyles styles) =>
        (styles.HasFlag(TextStyles.Italic) ? "*" : "")
        + (styles.HasFlag(TextStyles.Bold) ? "**" : "")
        + (styles.HasFlag(TextStyles.Strikethrough) ? "~~" : "")
        + (styles.HasFlag(TextStyles.Underline) ? "</u>" : "");

    private static string Escape(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c is '\\' or '*' or '_' or '`' or '~' or '<' or '>' or '[' or ']' or '&')
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    // A normal line that starts like a heading or a list item would turn into one when read back.
    private static string EscapeLineStart(string inline)
    {
        if (inline.Length == 0)
        {
            return inline;
        }

        if (inline[0] is '#' or '-' or '+')
        {
            return "\\" + inline;
        }

        int digits = 0;
        while (digits < inline.Length && char.IsAsciiDigit(inline[digits]))
        {
            digits++;
        }

        if (digits > 0 && digits < inline.Length && inline[digits] is '.' or ')')
        {
            return inline.Substring(0, digits) + "\\" + inline.Substring(digits);
        }

        return inline;
    }
}

/// <summary>
/// HTML from styled lines: &lt;strong&gt;, &lt;em&gt;, &lt;u&gt;, &lt;s&gt;, &lt;h2&gt; for a heading (the note title is the
/// h1 of a whole document), &lt;ul&gt;/&lt;ol&gt; for consecutive list lines, &lt;p&gt; for the rest. Text is escaped.
/// </summary>
public static class HtmlRenderer
{
    /// <summary>The body fragment, one element per line (or per list).</summary>
    public static string Render(IReadOnlyList<StyledLine> lines)
    {
        var builder = new StringBuilder();
        string? openList = null;

        void CloseList()
        {
            if (openList is not null)
            {
                builder.Append("</").Append(openList).Append(">\n");
                openList = null;
            }
        }

        foreach (var line in lines)
        {
            string? listTag = line.Kind switch
            {
                ParagraphKind.Bullet => "ul",
                ParagraphKind.Numbered => "ol",
                _ => null
            };

            if (listTag != openList)
            {
                CloseList();
                if (listTag is not null)
                {
                    builder.Append('<').Append(listTag).Append(">\n");
                    openList = listTag;
                }
            }

            string inner = line.Runs.Count == 0 ? "<br>" : string.Concat(line.Runs.Select(RenderRun));
            switch (line.Kind)
            {
                case ParagraphKind.Heading:
                    builder.Append("<h2>").Append(inner).Append("</h2>\n");
                    break;
                case ParagraphKind.Bullet:
                case ParagraphKind.Numbered:
                    builder.Append("<li>").Append(inner).Append("</li>\n");
                    break;
                default:
                    builder.Append("<p>").Append(inner).Append("</p>\n");
                    break;
            }
        }

        CloseList();
        return builder.ToString();
    }

    public static string Render(string text, IEnumerable<StyleSpan> spans, IEnumerable<ParagraphFormat>? paragraphs = null) =>
        Render(NoteLayout.Build(text, spans, paragraphs));

    /// <summary>A complete HTML page: the title as h1, then the body.</summary>
    public static string RenderDocument(string title, IReadOnlyList<StyledLine> lines) =>
        "<!DOCTYPE html>\n<html>\n<head>\n<meta charset=\"utf-8\">\n"
        + $"<title>{Escape(title)}</title>\n"
        + "<style>p { margin: 0; white-space: pre-wrap; }</style>\n</head>\n<body>\n"
        + $"<h1 class=\"note-title\">{Escape(title)}</h1>\n"
        + Render(lines)
        + "</body>\n</html>\n";

    private static string RenderRun(StyledRun run)
    {
        string open = (run.Styles.HasFlag(TextStyles.Bold) ? "<strong>" : "")
            + (run.Styles.HasFlag(TextStyles.Italic) ? "<em>" : "")
            + (run.Styles.HasFlag(TextStyles.Underline) ? "<u>" : "")
            + (run.Styles.HasFlag(TextStyles.Strikethrough) ? "<s>" : "");
        string close = (run.Styles.HasFlag(TextStyles.Strikethrough) ? "</s>" : "")
            + (run.Styles.HasFlag(TextStyles.Underline) ? "</u>" : "")
            + (run.Styles.HasFlag(TextStyles.Italic) ? "</em>" : "")
            + (run.Styles.HasFlag(TextStyles.Bold) ? "</strong>" : "");
        return open + Escape(run.Text) + close;
    }

    private static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
