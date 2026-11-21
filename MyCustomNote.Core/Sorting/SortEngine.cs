using MyCustomNote.Core.Models;

namespace MyCustomNote.Core.Sorting;

/// <summary>
/// Orders a list of notes by a <see cref="SortOption"/>, pinned notes first. Same rules as the Android SortEngine:
/// the date options put the newest first, Title is case-insensitive A to Z, Last opened puts notes that were never
/// opened last, Manual follows <see cref="Note.SortOrder"/>. Notes that compare equal keep their incoming order
/// (the sort is stable), and the input list is not changed.
/// </summary>
public static class SortEngine
{
    public static IReadOnlyList<Note> Sort(IEnumerable<Note> notes, SortOption option)
    {
        IEnumerable<Note> ordered = option switch
        {
            SortOption.DateCreated => notes.OrderByDescending(n => n.CreatedAt),
            SortOption.DateModified => notes.OrderByDescending(n => n.ModifiedAt),
            SortOption.Title => notes.OrderBy(n => n.Title.ToLowerInvariant(), StringComparer.Ordinal),
            SortOption.LastOpened => notes.OrderByDescending(n => n.LastOpenedAt ?? long.MinValue),
            SortOption.Manual => notes.OrderBy(n => n.SortOrder),
            _ => throw new ArgumentOutOfRangeException(nameof(option), option, null),
        };

        var list = ordered.ToList();
        return list.Where(n => n.IsPinned).Concat(list.Where(n => !n.IsPinned)).ToList();
    }
}
