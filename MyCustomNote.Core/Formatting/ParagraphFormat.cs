namespace MyCustomNote.Core.Formatting;

/// <summary>
/// What kind of paragraph a line of a note is. A line is one kind only, and style spans (bold,
/// italic, ...) work inside every kind. Numbers of a numbered list are not stored, see
/// <see cref="ParagraphFormats.Numbering"/>.
/// </summary>
public enum ParagraphKind
{
    Normal,
    Heading,
    Bullet,
    Numbered
}

/// <summary>
/// One line of the note text (lines are separated by '\n' and counted from 0) that is not a normal
/// paragraph. Normal lines are never stored.
/// </summary>
public readonly record struct ParagraphFormat(int Line, ParagraphKind Kind);
