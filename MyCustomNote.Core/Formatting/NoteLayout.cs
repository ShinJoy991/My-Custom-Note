namespace MyCustomNote.Core.Formatting;

/// <summary>The styles a piece of text carries (a flag set; the span model stores one kind per span).</summary>
[Flags]
public enum TextStyles
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Underline = 4,
    Strikethrough = 8
}

/// <summary>A piece of one line whose characters all carry the same styles.</summary>
public sealed record StyledRun(string Text, TextStyles Styles);

/// <summary>
/// One line of a note, ready to draw or export: its paragraph kind, the number to show for a numbered
/// line (0 otherwise) and its runs. An empty line has no runs.
/// </summary>
public sealed record StyledLine(ParagraphKind Kind, int Number, IReadOnlyList<StyledRun> Runs)
{
    public string PlainText => string.Concat(Runs.Select(run => run.Text));
}

/// <summary>
/// Turns text + style spans + paragraph kinds into lines of styled runs. Everything that draws or
/// exports a note (list previews, text, Markdown, HTML) starts from this, so none of them has to
/// parse or search the text for formatting characters: there are none in it.
/// </summary>
public static class NoteLayout
{
    public static IReadOnlyList<StyledLine> Build(
        string text, IEnumerable<StyleSpan> spans, IEnumerable<ParagraphFormat>? paragraphs = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(spans);

        var cleanSpans = StyleSpans.Normalize(spans, text);
        var formats = ParagraphFormats.Normalize(paragraphs ?? Array.Empty<ParagraphFormat>(), text);
        var kinds = formats.ToDictionary(f => f.Line, f => f.Kind);
        var numbers = ParagraphFormats.Numbering(formats, text);

        var lines = new List<StyledLine>();
        int lineStart = 0;
        int lineIndex = 0;
        while (true)
        {
            int newline = text.IndexOf('\n', lineStart);
            int lineEnd = newline < 0 ? text.Length : newline;

            var kind = kinds.GetValueOrDefault(lineIndex, ParagraphKind.Normal);
            int number = kind == ParagraphKind.Numbered ? numbers[lineIndex] : 0;
            lines.Add(new StyledLine(kind, number, BuildRuns(text, cleanSpans, lineStart, lineEnd)));

            if (newline < 0)
            {
                break;
            }

            lineStart = newline + 1;
            lineIndex++;
        }

        return lines;
    }

    private static List<StyledRun> BuildRuns(string text, IReadOnlyList<StyleSpan> spans, int start, int end)
    {
        var runs = new List<StyledRun>();
        if (end <= start)
        {
            return runs;
        }

        var points = new SortedSet<int> { start, end };
        foreach (var span in spans)
        {
            if (span.End <= start || span.Start >= end)
            {
                continue;
            }

            points.Add(Math.Max(span.Start, start));
            points.Add(Math.Min(span.End, end));
        }

        var ordered = points.ToList();
        for (int i = 0; i + 1 < ordered.Count; i++)
        {
            int from = ordered[i];
            int to = ordered[i + 1];

            var styles = TextStyles.None;
            foreach (var span in spans)
            {
                if (span.Start <= from && span.End >= to)
                {
                    styles |= ToStyles(span.Kind);
                }
            }

            string piece = text.Substring(from, to - from);
            if (runs.Count > 0 && runs[^1].Styles == styles)
            {
                runs[^1] = runs[^1] with { Text = runs[^1].Text + piece };
            }
            else
            {
                runs.Add(new StyledRun(piece, styles));
            }
        }

        return runs;
    }

    private static TextStyles ToStyles(TextStyleKind kind) => kind switch
    {
        TextStyleKind.Bold => TextStyles.Bold,
        TextStyleKind.Italic => TextStyles.Italic,
        TextStyleKind.Underline => TextStyles.Underline,
        TextStyleKind.Strikethrough => TextStyles.Strikethrough,
        _ => TextStyles.None
    };
}
