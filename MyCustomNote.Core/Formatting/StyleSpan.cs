namespace MyCustomNote.Core.Formatting;

/// <summary>
/// Inline text styles. Same four kinds as the Android app (<c>TextStyleKind</c>).
/// </summary>
public enum TextStyleKind
{
    Bold,
    Italic,
    Underline,
    Strikethrough
}

/// <summary>
/// A character range [Start, End) of a note's title or body that carries one style.
/// Offsets are UTF-16 code units, the same unit Kotlin strings use on Android.
/// The text itself never contains formatting characters; the spans are the formatting.
/// </summary>
public readonly record struct StyleSpan(int Start, int End, TextStyleKind Kind)
{
    public int Length => End - Start;

    public bool IsEmpty => End <= Start;
}
