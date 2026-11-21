using MyCustomNote.Core.Formatting;

namespace MyCustomNote.Core.Models;

/// <summary>Small colour-coding palette a note can be tagged with (independent of the app theme). Same 7 names as Android.</summary>
public enum NoteColorTag
{
    Default,
    Red,
    Orange,
    Yellow,
    Green,
    Blue,
    Purple
}

/// <summary>The five sort options of the note list (same as Android).</summary>
public enum SortOption
{
    DateCreated,
    DateModified,
    Title,
    LastOpened,
    Manual
}

/// <summary>Light, Dark and three palettes like Android; FollowWindows replaces Android's Dynamic colour.</summary>
public enum ThemeOption
{
    Light,
    Dark,
    Sepia,
    Forest,
    Ocean,
    FollowWindows
}

/// <summary>Font family choices for note text (same four as Android).</summary>
public enum NoteFont
{
    System,
    Serif,
    SansSerif,
    Monospace
}

public sealed record Tag(long Id, string Name);

public sealed record Folder(long Id, string Name, long? ParentFolderId = null, long SortOrder = 0);

public sealed record ChecklistItem(long Id, long NoteId, string Text, bool IsChecked = false, int Position = 0);

/// <summary>
/// A note as the rest of the app sees it (plain text, no encryption). Times are Unix milliseconds, the same unit the
/// Android app stores. Title and body are plain text; the formatting lives in the style spans (inline) and the
/// paragraph formats (headings and lists).
/// </summary>
public sealed record Note
{
    public long Id { get; init; }

    public string Title { get; init; } = "";

    public string Body { get; init; } = "";

    public long? FolderId { get; init; }

    public NoteColorTag? ColorTag { get; init; }

    public bool IsPinned { get; init; }

    public bool IsArchived { get; init; }

    public bool IsTrashed { get; init; }

    public long CreatedAt { get; init; }

    public long ModifiedAt { get; init; }

    public long? LastOpenedAt { get; init; }

    public long? TrashedAt { get; init; }

    public long SortOrder { get; init; }

    public bool IsLocked { get; init; }

    public bool IsHidden { get; init; }

    public IReadOnlyList<StyleSpan> BodyStyles { get; init; } = [];

    public IReadOnlyList<StyleSpan> TitleStyles { get; init; } = [];

    public IReadOnlyList<ParagraphFormat> Paragraphs { get; init; } = [];

    /// <summary>Filled by the repositories when a note is read; ignored when a note is saved (tags have their own repository).</summary>
    public IReadOnlyList<Tag> Tags { get; init; } = [];

    /// <summary>Filled by the repositories when a note is read; ignored when a note is saved (see <c>ChecklistRepository</c>).</summary>
    public IReadOnlyList<ChecklistItem> ChecklistItems { get; init; } = [];
}
