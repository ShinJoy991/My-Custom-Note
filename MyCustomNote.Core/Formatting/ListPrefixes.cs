using System.Text;

namespace MyCustomNote.Core.Formatting;

/// <summary>
/// The phone app has no heading or list formatting: its toolbar types the text prefixes "# ", "- " and "1. " at the
/// start of a line. This is the other half of the exchange with it: the phone turns the paragraph kinds of a Windows
/// note into those prefixes when it imports one, and these functions turn the prefixes of a phone note back into
/// paragraph kinds, so a list survives the round trip phone to Windows and back. Numbers are not stored here:
/// <see cref="ParagraphFormats.Numbering"/> counts them again.
/// </summary>
public static class ListPrefixes
{
    /// <summary>
    /// The kind of a line and the length of its prefix: "# " is a heading, "- " or "+ " a bullet, digits followed by ". "
    /// a numbered item (an item with nothing after the prefix, like "1. ", counts). Anything else is Normal with length 0.
    /// </summary>
    public static (ParagraphKind Kind, int Length) SplitLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (line.StartsWith("# ", StringComparison.Ordinal))
        {
            return (ParagraphKind.Heading, 2);
        }

        if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("+ ", StringComparison.Ordinal))
        {
            return (ParagraphKind.Bullet, 2);
        }

        int digits = 0;
        while (digits < line.Length && char.IsAsciiDigit(line[digits]))
        {
            digits++;
        }

        if (digits > 0 && digits + 1 < line.Length && line[digits] == '.' && line[digits + 1] == ' ')
        {
            return (ParagraphKind.Numbered, digits + 2);
        }

        return (ParagraphKind.Normal, 0);
    }

    /// <summary>
    /// Takes the prefixes off every line and returns them as paragraph kinds, with the style spans moved to match
    /// (a span that covered a prefix now starts at the text; a span that was only on a prefix disappears). A text
    /// without prefixes comes back unchanged. The returned spans are not normalized: call
    /// <see cref="StyleSpans.Normalize"/> with the new body afterwards.
    /// </summary>
    public static (string Body, IReadOnlyList<StyleSpan> Spans, IReadOnlyList<ParagraphFormat> Paragraphs) Extract(
        string body, IEnumerable<StyleSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(spans);

        string[] lines = body.Split('\n');
        var prefixLengths = new int[lines.Length];
        var originalStarts = new int[lines.Length]; // where each line starts in the text we were given
        var newStarts = new int[lines.Length];      // where it starts without its prefix
        var paragraphs = new List<ParagraphFormat>();
        var builder = new StringBuilder();

        int originalOffset = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            var (kind, length) = SplitLine(lines[i]);
            prefixLengths[i] = length;
            originalStarts[i] = originalOffset;

            if (i > 0)
            {
                builder.Append('\n');
            }

            newStarts[i] = builder.Length;
            builder.Append(lines[i], length, lines[i].Length - length);
            if (kind != ParagraphKind.Normal)
            {
                paragraphs.Add(new ParagraphFormat(i, kind));
            }

            originalOffset += lines[i].Length + 1;
        }

        int LineOf(int index)
        {
            int line = lines.Length - 1;
            while (line > 0 && originalStarts[line] > index)
            {
                line--;
            }

            return line;
        }

        // Where the character at this index of the given text ends up; a character inside a prefix lands at the start of its line's text.
        int Moved(int index)
        {
            int line = LineOf(index);
            return newStarts[line] + Math.Max(0, index - originalStarts[line] - prefixLengths[line]);
        }

        bool InsidePrefix(int index)
        {
            int line = LineOf(index);
            return index - originalStarts[line] < prefixLengths[line];
        }

        var moved = new List<StyleSpan>();
        foreach (var span in spans)
        {
            int start = Math.Clamp(span.Start, 0, body.Length);
            int end = Math.Clamp(span.End, 0, body.Length);
            if (end <= start)
            {
                continue;
            }

            int newStart = Moved(start);
            int newEnd = InsidePrefix(end - 1) ? Moved(end - 1) : Moved(end - 1) + 1;
            if (newEnd > newStart)
            {
                moved.Add(new StyleSpan(newStart, newEnd, span.Kind));
            }
        }

        return (builder.ToString(), moved, paragraphs);
    }
}
