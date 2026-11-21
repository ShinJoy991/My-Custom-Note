using System.Text;
using MyCustomNote.Core.Formatting;

namespace MyCustomNote.Core.IO;

public enum NoteFileFormat
{
    Text,
    Markdown,
    Html,
    Json
}

/// <summary>
/// Turns one note into the text of a file. Text, Markdown and HTML can only carry title and body (and the heading
/// and list structure); Json carries everything. A Markdown file also ends with a hidden comment holding the whole
/// note as Json (Base64, so nothing in the note can break out of it), which makes a re-import lossless, the same
/// trick the Android app uses; other Markdown readers do not show it.
/// </summary>
public static class NoteExporter
{
    public const string EmbeddedMarker = "<!--MYCUSTOMNOTE:";

    public static string Extension(NoteFileFormat format) => format switch
    {
        NoteFileFormat.Text => "txt",
        NoteFileFormat.Markdown => "md",
        NoteFileFormat.Html => "html",
        NoteFileFormat.Json => "json",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };

    public static string Export(NoteBundle bundle, NoteFileFormat format)
    {
        var note = bundle.Note;
        switch (format)
        {
            case NoteFileFormat.Text:
                return note.Title + "\n\n" + PlainTextRenderer.Render(note.Body, note.BodyStyles, note.Paragraphs);

            case NoteFileFormat.Markdown:
                string embedded = Convert.ToBase64String(Encoding.UTF8.GetBytes(NoteJson.Write(bundle, indented: false)));
                return "# " + MarkdownRenderer.Render(note.Title, note.TitleStyles)
                    + "\n\n" + MarkdownRenderer.Render(note.Body, note.BodyStyles, note.Paragraphs)
                    + "\n\n" + EmbeddedMarker + embedded + "-->";

            case NoteFileFormat.Html:
                return HtmlRenderer.RenderDocument(note.Title, NoteLayout.Build(note.Body, note.BodyStyles, note.Paragraphs));

            case NoteFileFormat.Json:
                return NoteJson.Write(bundle);

            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, null);
        }
    }

    /// <summary>A file name for a note: its title without characters Windows does not allow, never empty.</summary>
    public static string SafeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string cleaned = new string(title.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim('.', ' ');
        if (cleaned.Length > 80)
        {
            cleaned = cleaned[..80].TrimEnd('.', ' ');
        }

        return cleaned.Length == 0 ? "Untitled" : cleaned;
    }
}
