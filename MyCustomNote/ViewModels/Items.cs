using System.Windows.Media;
using MyCustomNote.Core.Models;
using MyCustomNote.Services;

namespace MyCustomNote.ViewModels;

internal enum NavKind
{
    /// <summary>A section title in the left pane ("Folders", "Tags"); not selectable.</summary>
    Header,

    /// <summary>A line of muted text shown instead of an empty section ("No folders yet"); not selectable.</summary>
    Placeholder,
    AllNotes,
    Archive,
    Trash,
    Locked,
    Folder,
    Tag,
}

/// <summary>One line of the left pane.</summary>
internal sealed record NavItem(NavKind Kind, string Title, long Id = 0)
{
    public bool IsSelectable => Kind is not (NavKind.Header or NavKind.Placeholder);

    public bool IsHeader => Kind == NavKind.Header;

    public bool IsPlaceholder => Kind == NavKind.Placeholder;
}

/// <summary>One card of the middle pane: the note plus the texts the card shows.</summary>
internal sealed class NoteListItem
{
    private const int PreviewLength = 140;

    public NoteListItem(Note note)
    {
        Note = note;
        Title = string.IsNullOrWhiteSpace(note.Title) ? "Untitled" : note.Title.Trim();
        Preview = FirstLine(note.Body);
        DateText = FormatDate(note.ModifiedAt);
        ColorBrush = NoteColors.BrushFor(note.ColorTag);
        TagsText = string.Join("  ", note.Tags.Select(t => "#" + t.Name));
        ChecklistText = note.ChecklistItems.Count == 0
            ? ""
            : $"  ·  {note.ChecklistItems.Count(i => i.IsChecked)}/{note.ChecklistItems.Count} done";
    }

    public Note Note { get; }

    public long Id => Note.Id;

    public string Title { get; }

    public string Preview { get; }

    public string Body => Note.Body;

    public string DateText { get; }

    public bool IsPinned => Note.IsPinned;

    /// <summary>The stripe on the left of the card (transparent without a color tag).</summary>
    public Brush ColorBrush { get; }

    /// <summary>"#work  #home" (empty without tags).</summary>
    public string TagsText { get; }

    public bool HasTags => TagsText.Length > 0;

    /// <summary>"  ·  2/5 done" (empty without a checklist).</summary>
    public string ChecklistText { get; }

    private static string FirstLine(string body)
    {
        // Only the first non-empty line is needed, so a huge note is not copied.
        int start = 0;
        while (start < body.Length)
        {
            int end = body.IndexOf('\n', start);
            if (end < 0)
            {
                end = body.Length;
            }

            string line = body[start..end].Trim();
            if (line.Length > 0)
            {
                if (line.Length > PreviewLength)
                {
                    int cut = PreviewLength;
                    if (char.IsHighSurrogate(line[cut - 1]))
                    {
                        cut--; // never cut an emoji in half
                    }

                    line = line[..cut] + "…";
                }

                return line;
            }

            start = end + 1;
        }

        return "";
    }

    private static string FormatDate(long unixMilliseconds)
    {
        try
        {
            var time = DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds).ToLocalTime();
            return time.Date == DateTime.Today ? time.ToString("t") : time.ToString("d");
        }
        catch (ArgumentOutOfRangeException)
        {
            return "";
        }
    }
}
