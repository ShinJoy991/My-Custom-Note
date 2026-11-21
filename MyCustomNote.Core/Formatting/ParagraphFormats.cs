namespace MyCustomNote.Core.Formatting;

/// <summary>
/// Pure functions over paragraph formats (heading, bullet, numbered). Lines are the pieces of the
/// text between '\n' characters. Every function returns a new list and never changes its input.
/// </summary>
public static class ParagraphFormats
{
    /// <summary>Number of lines: the count of '\n' plus one. Empty text is one (empty) line.</summary>
    public static int LineCount(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        int count = 1;
        foreach (char c in text)
        {
            if (c == '\n')
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// The line a caret offset is on. An offset right before a '\n' is still on that line; the offset
    /// right after it is on the next one. Out-of-range offsets are clamped.
    /// </summary>
    public static int LineOfOffset(string text, int offset)
    {
        ArgumentNullException.ThrowIfNull(text);

        offset = Math.Clamp(offset, 0, text.Length);
        int line = 0;
        for (int i = 0; i < offset; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    /// <summary>
    /// The first and last line a selection touches. The selection may be reversed or out of range.
    /// A selection that ends right after a line break does not reach the next line (like Word).
    /// A collapsed selection touches the line of the caret.
    /// </summary>
    public static (int First, int Last) LinesTouched(string text, int selectionStart, int selectionEnd)
    {
        ArgumentNullException.ThrowIfNull(text);

        int start = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, text.Length);
        int end = Math.Clamp(Math.Max(selectionStart, selectionEnd), 0, text.Length);

        int first = LineOfOffset(text, start);
        int lastOffset = end > start && text[end - 1] == '\n' ? end - 1 : end;
        int last = Math.Max(first, LineOfOffset(text, lastOffset));
        return (first, last);
    }

    /// <summary>
    /// Cleans a list against the current text: drops Normal entries and lines that do not exist,
    /// lets the last entry for a line win, and sorts by line.
    /// </summary>
    public static IReadOnlyList<ParagraphFormat> Normalize(IEnumerable<ParagraphFormat> formats, string text)
    {
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(text);

        int lines = LineCount(text);
        var byLine = new SortedDictionary<int, ParagraphKind>();
        foreach (var format in formats)
        {
            if (format.Line < 0 || format.Line >= lines)
            {
                continue;
            }

            if (format.Kind == ParagraphKind.Normal)
            {
                byLine.Remove(format.Line);
            }
            else
            {
                byLine[format.Line] = format.Kind;
            }
        }

        return byLine.Select(pair => new ParagraphFormat(pair.Key, pair.Value)).ToList();
    }

    /// <summary>
    /// Toolbar action. If every touched line already has <paramref name="kind"/> they all go back to
    /// Normal; otherwise every touched line gets it (replacing any other kind). Toggling
    /// <see cref="ParagraphKind.Normal"/> clears the touched lines.
    /// </summary>
    public static IReadOnlyList<ParagraphFormat> Toggle(
        IEnumerable<ParagraphFormat> formats, string text, int selectionStart, int selectionEnd, ParagraphKind kind)
    {
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(text);

        var kinds = Normalize(formats, text).ToDictionary(f => f.Line, f => f.Kind);
        var (first, last) = LinesTouched(text, selectionStart, selectionEnd);

        bool allAlready = kind != ParagraphKind.Normal
            && Enumerable.Range(first, last - first + 1).All(line => kinds.TryGetValue(line, out var current) && current == kind);

        for (int line = first; line <= last; line++)
        {
            if (allAlready || kind == ParagraphKind.Normal)
            {
                kinds.Remove(line);
            }
            else
            {
                kinds[line] = kind;
            }
        }

        return Normalize(kinds.Select(pair => new ParagraphFormat(pair.Key, pair.Value)), text);
    }

    /// <summary>
    /// The number shown for each line: consecutive Numbered lines count 1, 2, 3 and the count restarts
    /// after any line that is not Numbered. Lines that are not Numbered get 0. One entry per line.
    /// </summary>
    public static IReadOnlyList<int> Numbering(IEnumerable<ParagraphFormat> formats, string text)
    {
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(text);

        var kinds = Normalize(formats, text).ToDictionary(f => f.Line, f => f.Kind);
        var numbers = new int[LineCount(text)];
        int counter = 0;
        for (int line = 0; line < numbers.Length; line++)
        {
            if (kinds.TryGetValue(line, out var kind) && kind == ParagraphKind.Numbered)
            {
                numbers[line] = ++counter;
            }
            else
            {
                counter = 0;
            }
        }

        return numbers;
    }
}
