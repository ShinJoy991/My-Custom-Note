using System.Text;
using System.Windows;
using System.Windows.Documents;
using MyCustomNote.Core.Formatting;

namespace MyCustomNote.Editor;

[Flags]
internal enum StyleFlags
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Underline = 4,
    Strikethrough = 8
}

/// <summary>One Run (or LineBreak) of a paragraph: where it sits in the plain text and which styles it carries.</summary>
internal sealed record Piece(Inline Inline, int Start, int Length, StyleFlags Flags);

/// <summary>One line of the note text. A line is one WPF Paragraph, either on its own or inside a list item.</summary>
internal sealed class ParagraphInfo
{
    public ParagraphInfo(Paragraph? paragraph, int start, ParagraphKind kind, int displayedNumber)
    {
        Paragraph = paragraph;
        Start = start;
        Kind = kind;
        DisplayedNumber = displayedNumber;
    }

    public Paragraph? Paragraph { get; }

    public int Start { get; }

    public int End { get; set; }

    /// <summary>Normal, Bullet or Numbered (Heading is added in step 1.4c).</summary>
    public ParagraphKind Kind { get; }

    /// <summary>The number WPF shows for a numbered line, 0 for every other line.</summary>
    public int DisplayedNumber { get; }

    public List<Piece> Pieces { get; } = new();
}

/// <summary>
/// A snapshot of a FlowDocument as plain text ('\n' between lines) plus a map from character
/// offsets to TextPointers. Build a fresh one after every change to the document: formatting
/// splits and merges Runs, which makes older maps stale.
/// </summary>
internal sealed class DocumentMap
{
    /// <summary>A Heading line is a top-level paragraph whose own FontSize is this (the document default is smaller).</summary>
    public const double HeadingFontSize = 20;

    private readonly StringBuilder _builder = new();

    private DocumentMap(FlowDocument document)
    {
        Document = document;
    }

    public FlowDocument Document { get; }

    public string Text { get; private set; } = "";

    public List<ParagraphInfo> Paragraphs { get; } = new();

    public static DocumentMap Build(FlowDocument document)
    {
        var map = new DocumentMap(document);
        foreach (Block block in document.Blocks)
        {
            map.AddBlock(block, ParagraphKind.Normal, 0);
        }

        map.Text = map._builder.ToString();
        return map;
    }

    /// <summary>The styles of the document as canonical spans (see <see cref="StyleSpans.Normalize"/>).</summary>
    public IReadOnlyList<StyleSpan> ReadSpans()
    {
        var raw = new List<StyleSpan>();
        foreach (var paragraph in Paragraphs)
        {
            foreach (var piece in paragraph.Pieces)
            {
                if (piece.Inline is not Run)
                {
                    continue;
                }

                foreach (var kind in KindsOf(piece.Flags))
                {
                    raw.Add(new StyleSpan(piece.Start, piece.Start + piece.Length, kind));
                }
            }
        }

        return StyleSpans.Normalize(raw, Text);
    }

    /// <summary>The paragraph kinds of the document, one entry per line that is not Normal.</summary>
    public IReadOnlyList<ParagraphFormat> ReadParagraphs()
    {
        var formats = new List<ParagraphFormat>();
        for (int line = 0; line < Paragraphs.Count; line++)
        {
            if (Paragraphs[line].Kind != ParagraphKind.Normal)
            {
                formats.Add(new ParagraphFormat(line, Paragraphs[line].Kind));
            }
        }

        return ParagraphFormats.Normalize(formats, Text);
    }

    /// <summary>The number WPF shows on each line (0 for lines that are not numbered).</summary>
    public IReadOnlyList<int> DisplayedNumbers() => Paragraphs.Select(p => p.DisplayedNumber).ToList();

    /// <summary>The TextPointer for a character offset (0 to Text.Length). A '\n' between lines has no pointer of its own.</summary>
    public TextPointer PointerAt(int offset)
    {
        offset = Math.Clamp(offset, 0, Text.Length);

        foreach (var info in Paragraphs)
        {
            if (offset < info.Start || offset > info.End)
            {
                continue;
            }

            if (info.Paragraph is null)
            {
                return Document.ContentEnd;
            }

            if (offset == info.End)
            {
                return info.Paragraph.ContentEnd;
            }

            foreach (var piece in info.Pieces)
            {
                if (offset >= piece.Start && offset < piece.Start + piece.Length)
                {
                    return piece.Inline is Run run
                        ? run.ContentStart.GetPositionAtOffset(offset - piece.Start) ?? run.ContentEnd
                        : piece.Inline.ElementStart;
                }
            }

            return info.Paragraph.ContentStart;
        }

        return Document.ContentEnd;
    }

    /// <summary>The character offset of a TextPointer, the inverse of <see cref="PointerAt"/>.</summary>
    public int OffsetOf(TextPointer pointer)
    {
        var paragraph = pointer.Paragraph;
        var info = paragraph is null ? null : Paragraphs.FirstOrDefault(p => ReferenceEquals(p.Paragraph, paragraph));
        if (info is null)
        {
            return pointer.CompareTo(Document.ContentStart) <= 0 ? 0 : Text.Length;
        }

        int result = info.Start;
        foreach (var piece in info.Pieces)
        {
            if (piece.Inline is Run run)
            {
                if (pointer.CompareTo(run.ContentEnd) >= 0)
                {
                    result = piece.Start + piece.Length;
                    continue;
                }

                if (pointer.CompareTo(run.ContentStart) > 0)
                {
                    return piece.Start + run.ContentStart.GetOffsetToPosition(pointer);
                }

                return result;
            }

            if (pointer.CompareTo(piece.Inline.ElementEnd) >= 0)
            {
                result = piece.Start + piece.Length;
                continue;
            }

            return result;
        }

        return result;
    }

    public static StyleFlags FlagOf(TextStyleKind kind) => kind switch
    {
        TextStyleKind.Bold => StyleFlags.Bold,
        TextStyleKind.Italic => StyleFlags.Italic,
        TextStyleKind.Underline => StyleFlags.Underline,
        TextStyleKind.Strikethrough => StyleFlags.Strikethrough,
        _ => StyleFlags.None
    };

    public static IEnumerable<TextStyleKind> KindsOf(StyleFlags flags)
    {
        if ((flags & StyleFlags.Bold) != 0) yield return TextStyleKind.Bold;
        if ((flags & StyleFlags.Italic) != 0) yield return TextStyleKind.Italic;
        if ((flags & StyleFlags.Underline) != 0) yield return TextStyleKind.Underline;
        if ((flags & StyleFlags.Strikethrough) != 0) yield return TextStyleKind.Strikethrough;
    }

    private void AddBlock(Block block, ParagraphKind kind, int displayedNumber)
    {
        switch (block)
        {
            case Paragraph paragraph:
                BeginLine();
                var info = new ParagraphInfo(paragraph, _builder.Length, EffectiveKind(paragraph, kind), displayedNumber);
                AddInlines(paragraph.Inlines, info, StyleFlags.None);
                info.End = _builder.Length;
                Paragraphs.Add(info);
                break;

            case List list:
            {
                // Nested lists are not offered by the editor; if one shows up it is read as a flat list.
                var itemKind = list.MarkerStyle switch
                {
                    TextMarkerStyle.None => ParagraphKind.Normal,
                    TextMarkerStyle.Decimal => ParagraphKind.Numbered,
                    _ => ParagraphKind.Bullet
                };

                int number = list.StartIndex;
                foreach (ListItem item in list.ListItems)
                {
                    foreach (Block inner in item.Blocks)
                    {
                        AddBlock(inner, itemKind, itemKind == ParagraphKind.Numbered ? number : 0);
                    }

                    number++;
                }

                break;
            }

            default:
            {
                // Tables and so on are not supported by the spike: read them as plain text.
                BeginLine();
                var fallback = new ParagraphInfo(null, _builder.Length, ParagraphKind.Normal, 0);
                string blockText = new TextRange(block.ContentStart, block.ContentEnd).Text;
                _builder.Append(blockText.Replace("\r\n", "\n").TrimEnd('\n'));
                fallback.End = _builder.Length;
                Paragraphs.Add(fallback);
                break;
            }
        }
    }

    private static ParagraphKind EffectiveKind(Paragraph paragraph, ParagraphKind kind) =>
        kind == ParagraphKind.Normal
        && paragraph.ReadLocalValue(TextElement.FontSizeProperty) is double size
        && size == HeadingFontSize
            ? ParagraphKind.Heading
            : kind;

    private void BeginLine()
    {
        if (Paragraphs.Count > 0)
        {
            _builder.Append('\n');
        }
    }

    private void AddInlines(InlineCollection inlines, ParagraphInfo info, StyleFlags inherited)
    {
        foreach (Inline inline in inlines)
        {
            var flags = inherited | FlagsOf(inline);
            switch (inline)
            {
                case Run run:
                    if (run.Text.Length > 0)
                    {
                        info.Pieces.Add(new Piece(run, _builder.Length, run.Text.Length, flags));
                        _builder.Append(run.Text);
                    }

                    break;

                case LineBreak lineBreak:
                    info.Pieces.Add(new Piece(lineBreak, _builder.Length, 1, flags));
                    _builder.Append('\n');
                    break;

                case Span span:
                    AddInlines(span.Inlines, info, flags);
                    break;
            }
        }
    }

    // FontWeight and FontStyle are inherited properties, so the effective value on an inline already
    // includes its parents. TextDecorations are not inherited, so they are accumulated by the caller.
    private static StyleFlags FlagsOf(Inline inline)
    {
        var flags = StyleFlags.None;

        if (inline.FontWeight >= FontWeights.Bold)
        {
            flags |= StyleFlags.Bold;
        }

        if (inline.FontStyle == FontStyles.Italic || inline.FontStyle == FontStyles.Oblique)
        {
            flags |= StyleFlags.Italic;
        }

        foreach (TextDecoration decoration in inline.TextDecorations)
        {
            if (decoration.Location == TextDecorationLocation.Underline)
            {
                flags |= StyleFlags.Underline;
            }
            else if (decoration.Location == TextDecorationLocation.Strikethrough)
            {
                flags |= StyleFlags.Strikethrough;
            }
        }

        return flags;
    }
}
