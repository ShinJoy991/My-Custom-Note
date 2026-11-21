using System.Text;
using MyCustomNote.Core.Formatting;

namespace MyCustomNote.Core.Models;

/// <summary>
/// What the user edits in a note: title and body text and their formatting. It is what autosave compares and stores;
/// flags such as pinned or archived are not part of it, so saving the content can never overwrite them.
/// </summary>
public sealed record NoteContent(
    string Title,
    IReadOnlyList<StyleSpan> TitleStyles,
    string Body,
    IReadOnlyList<StyleSpan> BodyStyles,
    IReadOnlyList<ParagraphFormat> Paragraphs)
{
    public static NoteContent From(Note note) =>
        new(note.Title, note.TitleStyles, note.Body, note.BodyStyles, note.Paragraphs);

    /// <summary>True if both have the same text and the same formatting (lists are compared by their items, not by reference).</summary>
    public bool SameAs(NoteContent? other) =>
        other is not null
        && Title == other.Title
        && Body == other.Body
        && TitleStyles.SequenceEqual(other.TitleStyles)
        && BodyStyles.SequenceEqual(other.BodyStyles)
        && Paragraphs.SequenceEqual(other.Paragraphs);
}

/// <summary>The numbers shown under the editor.</summary>
public readonly record struct TextStats(int Words, int Characters)
{
    /// <summary>
    /// Words are runs of characters between white space. Characters are Unicode characters (an emoji counts as one,
    /// not two), without line breaks.
    /// </summary>
    public static TextStats Count(string text)
    {
        int words = 0;
        int characters = 0;
        bool inWord = false;

        foreach (Rune rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                inWord = false;
                if (rune.Value is not ('\n' or '\r'))
                {
                    characters++;
                }

                continue;
            }

            characters++;
            if (!inWord)
            {
                words++;
                inWord = true;
            }
        }

        return new TextStats(words, characters);
    }
}
