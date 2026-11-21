using System.Text;
using MyCustomNote.Core.Formatting;
using MyCustomNote.Core.Models;

namespace MyCustomNote.Core.IO;

/// <summary>
/// Reads one note from the text of a file. JSON, and Markdown that this app (or Android) exported, are read
/// directly and lose nothing. Any other Markdown or plain text file is read by a small parser: the first line as
/// title ("# Title", or a first line followed by an empty line, otherwise the file name), <c>**bold**</c>,
/// <c>*italic*</c>, <c>~~strike~~</c>, <c>&lt;u&gt;underline&lt;/u&gt;</c>, and the line prefixes "# ", "- " / "+ " and "1. ".
/// A backslash makes the next punctuation character plain. HTML is not supported (as on Android).
/// </summary>
public static class NoteImporter
{
    /// <exception cref="FormatException">The file is damaged (for example invalid JSON).</exception>
    /// <exception cref="NotSupportedException">The file type cannot be imported (HTML).</exception>
    public static NoteBundle Import(string text, string fileName, long now)
    {
        string extension = Path.GetExtension(fileName).ToLowerInvariant();
        string fallbackTitle = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(fallbackTitle))
        {
            fallbackTitle = "Imported note";
        }

        if (extension == ".json")
        {
            return NoteJson.Read(text, fallbackTitle, now);
        }

        if (extension is ".html" or ".htm")
        {
            throw new NotSupportedException("HTML files cannot be imported yet.");
        }

        if (ReadEmbedded(text) is { } embedded)
        {
            return NoteJson.Read(embedded, fallbackTitle, now);
        }

        return ParseMarkup(text, fallbackTitle, now);
    }

    // The block an exported .md ends with. A damaged block is not an error: the visible Markdown is read instead.
    private static string? ReadEmbedded(string text)
    {
        int start = text.IndexOf(NoteExporter.EmbeddedMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += NoteExporter.EmbeddedMarker.Length;
        int end = text.IndexOf("-->", start, StringComparison.Ordinal);
        if (end < 0)
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(text[start..end]));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static NoteBundle ParseMarkup(string text, string fallbackTitle, long now)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();

        string title = fallbackTitle;
        int bodyStart = 0;
        if (lines.Count > 0)
        {
            string first = lines[0].Trim();
            if (first.StartsWith("# ", StringComparison.Ordinal))
            {
                title = first[2..].Trim();
                bodyStart = 1;
            }
            else if (first.Length > 0 && lines.Count > 1 && string.IsNullOrWhiteSpace(lines[1]))
            {
                title = first;
                bodyStart = 2;
            }
        }

        // The empty lines between the title and the text are not part of the text.
        while (bodyStart < lines.Count && string.IsNullOrWhiteSpace(lines[bodyStart]) && bodyStart > 0)
        {
            bodyStart++;
        }

        var (titleText, titleSpans) = ParseInline(title);
        var body = new StringBuilder();
        var spans = new List<StyleSpan>();
        var paragraphs = new List<ParagraphFormat>();

        for (int i = bodyStart; i < lines.Count; i++)
        {
            string line = lines[i];
            if (line.EndsWith("  ", StringComparison.Ordinal))
            {
                line = line.TrimEnd(' '); // Markdown's hard line break
            }

            var (kind, content) = SplitPrefix(line);
            var (plain, lineSpans) = ParseInline(content);

            int lineNumber = i - bodyStart;
            if (i > bodyStart)
            {
                body.Append('\n');
            }

            int offset = body.Length;
            body.Append(plain);
            spans.AddRange(lineSpans.Select(s => new StyleSpan(s.Start + offset, s.End + offset, s.Kind)));
            if (kind != ParagraphKind.Normal)
            {
                paragraphs.Add(new ParagraphFormat(lineNumber, kind));
            }
        }

        string bodyText = body.ToString();
        var note = new Note
        {
            Title = titleText,
            Body = bodyText,
            CreatedAt = now,
            ModifiedAt = now,
            TitleStyles = StyleSpans.Normalize(titleSpans, titleText),
            BodyStyles = StyleSpans.Normalize(spans, bodyText),
            Paragraphs = paragraphs,
        };
        return new NoteBundle(note);
    }

    private static (ParagraphKind Kind, string Content) SplitPrefix(string line)
    {
        var (kind, length) = ListPrefixes.SplitLine(line);
        return (kind, line[length..]);
    }

    private const string Escapable = "\\*_`~<>[]&#-+.!()";

    /// <summary>
    /// One line of inline markup to plain text and spans. Nested markers are read by calling this again on the
    /// inside, so "&lt;u&gt;**word**&lt;/u&gt;" gives two spans on the same word. An opening marker without a
    /// closing one stays as text.
    /// </summary>
    internal static (string Text, List<StyleSpan> Spans) ParseInline(string raw)
    {
        var plain = new StringBuilder();
        var spans = new List<StyleSpan>();
        int i = 0;

        void AddInner(string inner, TextStyleKind[] kinds)
        {
            var (innerText, innerSpans) = ParseInline(inner);
            int start = plain.Length;
            plain.Append(innerText);
            foreach (var kind in kinds)
            {
                spans.Add(new StyleSpan(start, plain.Length, kind));
            }

            spans.AddRange(innerSpans.Select(s => new StyleSpan(s.Start + start, s.End + start, s.Kind)));
        }

        while (i < raw.Length)
        {
            if (raw[i] == '\\' && i + 1 < raw.Length && Escapable.Contains(raw[i + 1]))
            {
                plain.Append(raw[i + 1]);
                i += 2;
            }
            else if (string.CompareOrdinal(raw, i, "<u>", 0, 3) == 0 && raw.IndexOf("</u>", i + 3, StringComparison.Ordinal) is var closeU and >= 0)
            {
                AddInner(raw[(i + 3)..closeU], [TextStyleKind.Underline]);
                i = closeU + 4;
            }
            else if (string.CompareOrdinal(raw, i, "~~", 0, 2) == 0 && raw.IndexOf("~~", i + 2, StringComparison.Ordinal) is var closeS and >= 0)
            {
                AddInner(raw[(i + 2)..closeS], [TextStyleKind.Strikethrough]);
                i = closeS + 2;
            }
            else if (raw[i] == '*')
            {
                int run = 0;
                while (i + run < raw.Length && raw[i + run] == '*')
                {
                    run++;
                }

                int marker = Math.Min(run, 3);
                int close = FindClosingRun(raw, i + run, marker);
                if (close < 0)
                {
                    plain.Append('*', run);
                    i += run;
                }
                else
                {
                    TextStyleKind[] kinds = marker switch
                    {
                        1 => [TextStyleKind.Italic],
                        2 => [TextStyleKind.Bold],
                        _ => [TextStyleKind.Bold, TextStyleKind.Italic],
                    };
                    AddInner(raw[(i + marker)..close], kinds);
                    i = close + marker;
                }
            }
            else
            {
                plain.Append(raw[i]);
                i++;
            }
        }

        return (plain.ToString(), spans);
    }

    // The next run of at least `length` asterisks at or after `from`, or -1.
    private static int FindClosingRun(string raw, int from, int length)
    {
        int j = from;
        while (j < raw.Length)
        {
            if (raw[j] == '*')
            {
                int run = 0;
                while (j + run < raw.Length && raw[j + run] == '*')
                {
                    run++;
                }

                if (run >= length)
                {
                    return j;
                }

                j += run;
            }
            else
            {
                j++;
            }
        }

        return -1;
    }
}
