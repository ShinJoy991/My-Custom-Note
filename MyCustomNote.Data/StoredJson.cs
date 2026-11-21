using System.Text;
using System.Text.Json;
using MyCustomNote.Core.Formatting;
using MyCustomNote.Core.Models;

namespace MyCustomNote.Data;

/// <summary>
/// The text forms stored in the database. Spans use the Android format exactly:
/// <c>[{"start":0,"end":5,"kind":"BOLD"}]</c>. Paragraph kinds (Windows only) use
/// <c>[{"line":0,"kind":"BULLET"}]</c>. Reading never throws: like Android, a column that cannot be read gives an
/// empty list (the text is still shown, only the formatting is lost).
/// </summary>
internal static class StoredJson
{
    public static string ToStored<T>(T value)
        where T : struct, Enum => value.ToString().ToUpperInvariant();

    public static T? EnumFromStored<T>(string? stored)
        where T : struct, Enum => Enum.TryParse<T>(stored, ignoreCase: true, out var value) && Enum.IsDefined(value) ? value : null;

    public static string SpansToJson(IReadOnlyList<StyleSpan> spans)
    {
        return Write(writer =>
        {
            foreach (var span in spans)
            {
                writer.WriteStartObject();
                writer.WriteNumber("start", span.Start);
                writer.WriteNumber("end", span.End);
                writer.WriteString("kind", ToStored(span.Kind));
                writer.WriteEndObject();
            }
        });
    }

    public static IReadOnlyList<StyleSpan> SpansFromJson(string? json)
    {
        return Read(json, element => new StyleSpan(
            element.GetProperty("start").GetInt32(),
            element.GetProperty("end").GetInt32(),
            Enum.Parse<TextStyleKind>(element.GetProperty("kind").GetString() ?? "", ignoreCase: true)));
    }

    public static string ParagraphsToJson(IReadOnlyList<ParagraphFormat> paragraphs)
    {
        return Write(writer =>
        {
            foreach (var paragraph in paragraphs)
            {
                writer.WriteStartObject();
                writer.WriteNumber("line", paragraph.Line);
                writer.WriteString("kind", ToStored(paragraph.Kind));
                writer.WriteEndObject();
            }
        });
    }

    public static IReadOnlyList<ParagraphFormat> ParagraphsFromJson(string? json)
    {
        return Read(json, element => new ParagraphFormat(
            element.GetProperty("line").GetInt32(),
            Enum.Parse<ParagraphKind>(element.GetProperty("kind").GetString() ?? "", ignoreCase: true)));
    }

    private static string Write(Action<Utf8JsonWriter> writeItems)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            writeItems(writer);
            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static IReadOnlyList<T> Read<T>(string? json, Func<JsonElement, T> readItem)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var items = new List<T>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                items.Add(readItem(element));
            }

            return items;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException
                                       or KeyNotFoundException or ArgumentException or OverflowException)
        {
            return [];
        }
    }
}
