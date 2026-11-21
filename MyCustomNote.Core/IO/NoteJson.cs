using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using MyCustomNote.Core.Formatting;
using MyCustomNote.Core.Models;

namespace MyCustomNote.Core.IO;

/// <summary>A note together with what lives outside the note row: its folder name, tag names and checklist (the file formats carry names, not database ids).</summary>
public sealed record NoteBundle(
    Note Note,
    string? FolderName = null,
    IReadOnlyList<string>? TagNames = null,
    IReadOnlyList<ChecklistItem>? Checklist = null)
{
    public IReadOnlyList<string> Tags => TagNames ?? [];

    public IReadOnlyList<ChecklistItem> ChecklistItems => Checklist ?? [];
}

/// <summary>
/// The JSON file format of one note. It is the Android format (<c>"version": 3</c>: the keys of
/// <c>FileExporter.buildNoteJson</c>) plus one extra key, <c>paragraphs</c> (headings and lists), written as
/// <c>"version": 4</c>. Android ignores keys it does not know, so a Windows file opens there (without headings and
/// lists), and a file from Android opens here. Reading is tolerant: a missing or damaged part gives a default
/// for that part, only text that is not a JSON object at all is an error.
/// </summary>
public static class NoteJson
{
    public const string FormatName = "MyCustomNote";
    public const int Version = 4;

    public static string Write(NoteBundle bundle, bool indented = true)
    {
        var note = bundle.Note;
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = indented, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", FormatName);
            writer.WriteNumber("version", Version);
            writer.WriteString("title", note.Title);
            writer.WriteString("body", note.Body);
            writer.WriteNumber("createdAt", note.CreatedAt);
            writer.WriteNumber("modifiedAt", note.ModifiedAt);
            writer.WriteBoolean("isPinned", note.IsPinned);
            writer.WriteBoolean("isArchived", note.IsArchived);
            if (note.ColorTag is { } color && color != NoteColorTag.Default)
            {
                writer.WriteString("colorTag", color.ToString().ToUpperInvariant());
            }

            if (bundle.FolderName is not null)
            {
                writer.WriteString("folderName", bundle.FolderName);
            }

            writer.WriteStartArray("tags");
            foreach (string tag in bundle.Tags)
            {
                writer.WriteStringValue(tag);
            }

            writer.WriteEndArray();

            writer.WriteStartArray("checklistItems");
            foreach (var item in bundle.ChecklistItems)
            {
                writer.WriteStartObject();
                writer.WriteString("text", item.Text);
                writer.WriteBoolean("isChecked", item.IsChecked);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            WriteSpans(writer, "bodyStylesJson", note.BodyStyles);
            WriteSpans(writer, "titleStylesJson", note.TitleStyles);

            writer.WriteStartArray("paragraphs");
            foreach (var paragraph in note.Paragraphs)
            {
                writer.WriteStartObject();
                writer.WriteNumber("line", paragraph.Line);
                writer.WriteString("kind", paragraph.Kind.ToString().ToUpperInvariant());
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <exception cref="FormatException">The text is not a JSON object.</exception>
    public static NoteBundle Read(string json, string fallbackTitle, long now)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new FormatException("The file is not valid JSON.", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("The file does not contain a note.");
            }

            string title = Text(root, "title") ?? fallbackTitle;
            string body = Text(root, "body") ?? "";
            IEnumerable<StyleSpan> bodySpans = ReadSpans(root, "bodyStylesJson");

            // This app always writes the "paragraphs" key (even when empty) and keeps list prefixes out of the text. A file
            // without the key comes from the phone app, whose lists are the text prefixes "# ", "- ", "1. ": turn them back
            // into paragraph kinds, so a list survives the round trip phone to Windows and back.
            IReadOnlyList<ParagraphFormat> paragraphs;
            if (root.TryGetProperty("paragraphs", out _))
            {
                paragraphs = ReadParagraphs(root);
            }
            else
            {
                (body, bodySpans, paragraphs) = ListPrefixes.Extract(body, bodySpans);
            }

            var note = new Note
            {
                Title = title,
                Body = body,
                IsPinned = Flag(root, "isPinned"),
                IsArchived = Flag(root, "isArchived"),
                ColorTag = Enum.TryParse<NoteColorTag>(Text(root, "colorTag"), ignoreCase: true, out var color)
                    && Enum.IsDefined(color) && color != NoteColorTag.Default ? color : null,
                CreatedAt = Number(root, "createdAt") ?? now,
                ModifiedAt = Number(root, "modifiedAt") ?? now,
                TitleStyles = StyleSpans.Normalize(ReadSpans(root, "titleStylesJson"), title),
                BodyStyles = StyleSpans.Normalize(bodySpans, body),
                Paragraphs = paragraphs,
            };

            var tags = new List<string>();
            if (root.TryGetProperty("tags", out var tagArray) && tagArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in tagArray.EnumerateArray())
                {
                    string? name = element.ValueKind == JsonValueKind.String ? element.GetString()?.Trim() : null;
                    if (!string.IsNullOrEmpty(name) && !tags.Contains(name))
                    {
                        tags.Add(name);
                    }
                }
            }

            var checklist = new List<ChecklistItem>();
            if (root.TryGetProperty("checklistItems", out var itemArray) && itemArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in itemArray.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.Object && Text(element, "text") is { Length: > 0 } text)
                    {
                        checklist.Add(new ChecklistItem(0, 0, text, Flag(element, "isChecked"), checklist.Count));
                    }
                }
            }

            string? folder = Text(root, "folderName");
            return new NoteBundle(note, string.IsNullOrWhiteSpace(folder) ? null : folder.Trim(), tags, checklist);
        }
    }

    private static void WriteSpans(Utf8JsonWriter writer, string name, IReadOnlyList<StyleSpan> spans)
    {
        writer.WriteStartArray(name);
        foreach (var span in spans)
        {
            writer.WriteStartObject();
            writer.WriteNumber("start", span.Start);
            writer.WriteNumber("end", span.End);
            writer.WriteString("kind", span.Kind.ToString().ToUpperInvariant());
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    // TryGetInt32 throws (it does not return false) when the value is not a number, so the kind is checked first.
    private static bool TryInt(JsonElement parent, string name, out int value)
    {
        value = 0;
        return parent.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out value);
    }

    private static string? Text(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Flag(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static long? Number(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long n) ? n : null;

    // Android writes the spans as an array; an older form may hold the array as text, so both are accepted.
    private static IEnumerable<StyleSpan> ReadSpans(JsonElement root, string name)
    {
        var spans = new List<StyleSpan>();
        foreach (var element in ArrayOf(root, name))
        {
            if (element.ValueKind == JsonValueKind.Object
                && TryInt(element, "start", out int s)
                && TryInt(element, "end", out int e)
                && Enum.TryParse<TextStyleKind>(Text(element, "kind"), ignoreCase: true, out var kind) && Enum.IsDefined(kind))
            {
                spans.Add(new StyleSpan(s, e, kind));
            }
        }

        return spans;
    }

    private static IReadOnlyList<ParagraphFormat> ReadParagraphs(JsonElement root)
    {
        var result = new List<ParagraphFormat>();
        foreach (var element in ArrayOf(root, "paragraphs"))
        {
            if (element.ValueKind == JsonValueKind.Object
                && TryInt(element, "line", out int l) && l >= 0
                && Enum.TryParse<ParagraphKind>(Text(element, "kind"), ignoreCase: true, out var kind) && Enum.IsDefined(kind))
            {
                result.Add(new ParagraphFormat(l, kind));
            }
        }

        return result;
    }

    private static List<JsonElement> ArrayOf(JsonElement root, string name)
    {
        var items = new List<JsonElement>();
        if (!root.TryGetProperty(name, out var value))
        {
            return items;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            try
            {
                using var inner = JsonDocument.Parse(value.GetString() ?? "");
                if (inner.RootElement.ValueKind == JsonValueKind.Array)
                {
                    items.AddRange(inner.RootElement.EnumerateArray().Select(e => e.Clone()));
                }
            }
            catch (JsonException)
            {
                // damaged text: no spans
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            items.AddRange(value.EnumerateArray());
        }

        return items;
    }
}
