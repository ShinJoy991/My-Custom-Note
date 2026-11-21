using Microsoft.Data.Sqlite;
using MyCustomNote.Core.Models;
using MyCustomNote.Core.Security;

namespace MyCustomNote.Data;

/// <summary>
/// Notes in the database. Rule from Android: locked and hidden notes never appear in a normal list, search, folder,
/// tag, archive or trash query; only <see cref="GetLocked"/>, <see cref="GetHidden"/> and <see cref="GetById"/> return them.
/// Every read fills <see cref="Note.Tags"/> and <see cref="Note.ChecklistItems"/>.
/// Title and body of a locked note are stored encrypted (see <see cref="Lock"/>); everything else is plain.
/// </summary>
public sealed class NoteRepository
{
    private static readonly string[] ColumnNames =
    [
        "id", "title", "body", "folderId", "colorTag", "isPinned", "isArchived", "isTrashed", "createdAt", "modifiedAt",
        "lastOpenedAt", "trashedAt", "sortOrder", "isLocked", "isHidden", "bodyStylesJson", "titleStylesJson", "paragraphsJson",
    ];

    private const string NormalNote = "isTrashed = 0 AND isLocked = 0 AND isHidden = 0";

    private readonly NoteDatabase _database;
    private readonly Func<long> _clock;

    public NoteRepository(NoteDatabase database)
        : this(database, () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
    {
    }

    /// <summary>The clock (Unix milliseconds) is a parameter so that tests control the time.</summary>
    public NoteRepository(NoteDatabase database, Func<long> clock)
    {
        _database = database;
        _clock = clock;
    }

    private static string Select(string prefix = "") => string.Join(", ", ColumnNames.Select(name => prefix + name));

    // ---------- lists ----------

    /// <summary>Not trashed, not archived, not locked, not hidden. Newest modified first (the sort option is applied later).</summary>
    public IReadOnlyList<Note> GetActive() =>
        Query($"SELECT {Select()} FROM notes WHERE {NormalNote} AND isArchived = 0 ORDER BY modifiedAt DESC");

    public IReadOnlyList<Note> GetInFolder(long folderId) =>
        Query($"SELECT {Select()} FROM notes WHERE folderId = @folder AND {NormalNote} AND isArchived = 0 ORDER BY modifiedAt DESC",
            ("@folder", folderId));

    /// <summary>Notes with a tag. Archived notes are included, as on Android.</summary>
    public IReadOnlyList<Note> GetWithTag(long tagId) =>
        Query($"""
               SELECT {Select("n.")} FROM notes n
               INNER JOIN note_tag_cross_ref r ON n.id = r.noteId
               WHERE r.tagId = @tag AND n.isTrashed = 0 AND n.isLocked = 0 AND n.isHidden = 0
               ORDER BY n.modifiedAt DESC
               """,
            ("@tag", tagId));

    public IReadOnlyList<Note> GetArchived() =>
        Query($"SELECT {Select()} FROM notes WHERE isArchived = 1 AND {NormalNote} ORDER BY modifiedAt DESC");

    /// <summary>The trash, most recently trashed first.</summary>
    public IReadOnlyList<Note> GetTrashed() =>
        Query($"SELECT {Select()} FROM notes WHERE isTrashed = 1 AND isLocked = 0 AND isHidden = 0 ORDER BY trashedAt DESC");

    /// <summary>Locked notes that are not hidden (for the Locked Notes screen).</summary>
    public IReadOnlyList<Note> GetLocked() =>
        Query($"SELECT {Select()} FROM notes WHERE isLocked = 1 AND isHidden = 0 AND isTrashed = 0 ORDER BY modifiedAt DESC");

    /// <summary>Hidden notes, locked or not (for the Hidden screen).</summary>
    public IReadOnlyList<Note> GetHidden() =>
        Query($"SELECT {Select()} FROM notes WHERE isHidden = 1 AND isTrashed = 0 ORDER BY modifiedAt DESC");

    /// <summary>
    /// Search in title and body (not trashed, not locked, not hidden; archived notes are included, as on Android).
    /// <c>%</c>, <c>_</c> and <c>\</c> in the query are plain characters. SQLite ignores case for the letters A-Z only.
    /// </summary>
    public IReadOnlyList<Note> Search(string query)
    {
        string escaped = query.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        return Query($"""
                      SELECT {Select()} FROM notes
                      WHERE {NormalNote} AND (title LIKE @pattern ESCAPE '\' OR body LIKE @pattern ESCAPE '\')
                      ORDER BY modifiedAt DESC
                      """,
            ("@pattern", "%" + escaped + "%"));
    }

    /// <summary>One note by id, whatever its state (also locked, hidden and trashed).</summary>
    public Note? GetById(long id) =>
        Query($"SELECT {Select()} FROM notes WHERE id = @id", ("@id", id)).FirstOrDefault();

    // ---------- changes ----------

    /// <summary>Adds a note and returns its new id (<see cref="Note.Id"/> is ignored). Tags and checklist items are saved by their own repositories.</summary>
    public long Create(Note note)
    {
        using var connection = _database.Open();
        string columns = string.Join(", ", ColumnNames.Skip(1));
        string values = string.Join(", ", ColumnNames.Skip(1).Select(n => "@" + n));
        using var command = connection.Sql(
            $"INSERT INTO notes ({columns}) VALUES ({values}) RETURNING id",
            Bind(note, includeId: false));
        return Convert.ToInt64(command.ExecuteScalar());
    }

    /// <summary>Saves every field of the note except tags and checklist items. Returns false if the note does not exist.</summary>
    public bool Update(Note note)
    {
        using var connection = _database.Open();
        string assignments = string.Join(", ", ColumnNames.Skip(1).Select(n => $"{n} = @{n}"));
        using var command = connection.Sql($"UPDATE notes SET {assignments} WHERE id = @id", Bind(note, includeId: true));
        return command.ExecuteNonQuery() == 1;
    }

    /// <summary>
    /// Saves only the text and formatting of a note and sets <c>modifiedAt</c> from the clock. Used by autosave: it
    /// cannot overwrite a flag (pinned, archived, folder, color...) that something else changed while the note was open.
    /// Returns false if the note does not exist.
    /// </summary>
    public bool UpdateContent(long id, NoteContent content, VaultKey? key = null)
    {
        bool? locked = ReadIsLocked(id);
        if (locked is null)
        {
            return false;
        }

        if (locked == true)
        {
            // A locked note is only ever written encrypted, and never without the key.
            if (key is null)
            {
                return false;
            }

            content = content with { Title = key.Encrypt(content.Title), Body = key.Encrypt(content.Body) };
        }

        return UpdateContentCore(id, content);
    }

    private bool? ReadIsLocked(long id)
    {
        using var connection = _database.Open();
        using var command = connection.Sql("SELECT isLocked FROM notes WHERE id = @id", ("@id", id));
        return command.ExecuteScalar() is { } value ? Convert.ToInt64(value) != 0 : null;
    }

    /// <summary>
    /// Locks a note: title, body and every checklist item text become encrypted text, the flag is set and the
    /// modified time changes, all in one transaction (a failure leaves the note as it was). Returns false if the
    /// note does not exist or is already locked. Spans and paragraph kinds stay plain (as on Android).
    /// </summary>
    public bool Lock(long id, VaultKey key) => Transform(id, key, locking: true);

    /// <summary>The reverse of <see cref="Lock"/>. Throws a <see cref="System.Security.Cryptography.CryptographicException"/> if the key is not the one that locked it.</summary>
    public bool Unlock(long id, VaultKey key) => Transform(id, key, locking: false);

    private bool Transform(long id, VaultKey key, bool locking)
    {
        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();

        string title;
        string body;
        using (var read = connection.Sql(
            "SELECT title, body FROM notes WHERE id = @id AND isLocked = @from",
            ("@id", id), ("@from", locking ? 0 : 1)))
        using (var reader = read.ExecuteReader())
        {
            if (!reader.Read())
            {
                return false;
            }

            title = reader.GetString(0);
            body = reader.GetString(1);
        }

        string Convert(string text) => locking ? key.Encrypt(text) : (VaultKey.IsEncrypted(text) ? key.Decrypt(text) : text);

        using (var update = connection.Sql(
            "UPDATE notes SET title = @title, body = @body, isLocked = @to, modifiedAt = @now WHERE id = @id",
            ("@title", Convert(title)), ("@body", Convert(body)), ("@to", locking ? 1 : 0), ("@now", _clock()), ("@id", id)))
        {
            update.ExecuteNonQuery();
        }

        var items = new List<(long Id, string Text)>();
        using (var read = connection.Sql("SELECT id, text FROM checklist_items WHERE noteId = @id", ("@id", id)))
        using (var reader = read.ExecuteReader())
        {
            while (reader.Read())
            {
                items.Add((reader.GetInt64(0), reader.GetString(1)));
            }
        }

        foreach (var (itemId, itemText) in items)
        {
            using var update = connection.Sql("UPDATE checklist_items SET text = @text WHERE id = @id", ("@text", Convert(itemText)), ("@id", itemId));
            update.ExecuteNonQuery();
        }

        transaction.Commit();
        return true;
    }

    /// <summary>
    /// A copy of a locked note with title, body and checklist texts decrypted, for showing it after the notes PIN was
    /// entered. Notes that are not locked, or a missing key, give the note unchanged (titles then show as encrypted text).
    /// </summary>
    public static Note Decrypted(Note note, VaultKey? key)
    {
        if (!note.IsLocked || key is null)
        {
            return note;
        }

        try
        {
            string Plain(string text) => VaultKey.IsEncrypted(text) ? key.Decrypt(text) : text;
            return note with
            {
                Title = Plain(note.Title),
                Body = Plain(note.Body),
                ChecklistItems = note.ChecklistItems.Select(i => i with { Text = Plain(i.Text) }).ToList(),
            };
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return note;
        }
    }

    private bool UpdateContentCore(long id, NoteContent content) =>
        Execute(
            """
            UPDATE notes SET title = @title, body = @body, titleStylesJson = @titleStyles,
                bodyStylesJson = @bodyStyles, paragraphsJson = @paragraphs, modifiedAt = @now
            WHERE id = @id
            """,
            ("@title", content.Title),
            ("@body", content.Body),
            ("@titleStyles", StoredJson.SpansToJson(content.TitleStyles)),
            ("@bodyStyles", StoredJson.SpansToJson(content.BodyStyles)),
            ("@paragraphs", StoredJson.ParagraphsToJson(content.Paragraphs)),
            ("@now", _clock()),
            ("@id", id)) == 1;

    public bool SetPinned(long id, bool isPinned) =>
        Execute("UPDATE notes SET isPinned = @v WHERE id = @id", ("@v", SqlExtensions.Flag(isPinned)), ("@id", id)) == 1;

    public bool SetArchived(long id, bool isArchived) =>
        Execute("UPDATE notes SET isArchived = @v WHERE id = @id", ("@v", SqlExtensions.Flag(isArchived)), ("@id", id)) == 1;

    public bool SetHidden(long id, bool isHidden) =>
        Execute("UPDATE notes SET isHidden = @v WHERE id = @id", ("@v", SqlExtensions.Flag(isHidden)), ("@id", id)) == 1;

    /// <summary>Moves a note into a folder, or out of all folders (null).</summary>
    public bool SetFolder(long id, long? folderId) =>
        Execute("UPDATE notes SET folderId = @folder WHERE id = @id", ("@folder", folderId), ("@id", id)) == 1;

    /// <summary>Sets the color tag of a note; null or <see cref="NoteColorTag.Default"/> removes it.</summary>
    public bool SetColor(long id, NoteColorTag? color) =>
        Execute(
            "UPDATE notes SET colorTag = @color WHERE id = @id",
            ("@color", (color is null or NoteColorTag.Default) ? null : StoredJson.ToStored(color.Value)),
            ("@id", id)) == 1;

    public bool MoveToTrash(long id) =>
        Execute("UPDATE notes SET isTrashed = 1, trashedAt = @now WHERE id = @id", ("@now", _clock()), ("@id", id)) == 1;

    public bool RestoreFromTrash(long id) =>
        Execute("UPDATE notes SET isTrashed = 0, trashedAt = NULL WHERE id = @id", ("@id", id)) == 1;

    public bool UpdateLastOpened(long id) =>
        Execute("UPDATE notes SET lastOpenedAt = @now WHERE id = @id", ("@now", _clock()), ("@id", id)) == 1;

    /// <summary>Sets the manual order of several notes in one step (all or none).</summary>
    public void UpdateSortOrder(IEnumerable<(long Id, long Order)> idsWithOrder)
    {
        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();
        foreach (var (id, order) in idsWithOrder)
        {
            using var command = connection.Sql("UPDATE notes SET sortOrder = @order WHERE id = @id", ("@order", order), ("@id", id));
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>Deletes the note for good; its tag links and checklist items go with it.</summary>
    public bool PermanentlyDelete(long id) => Execute("DELETE FROM notes WHERE id = @id", ("@id", id)) == 1;

    /// <summary>Deletes trashed notes that have been in the trash longer than <paramref name="ageMillis"/>. Returns how many.</summary>
    public int PurgeTrashOlderThan(long ageMillis) =>
        Execute("DELETE FROM notes WHERE isTrashed = 1 AND trashedAt < @limit", ("@limit", _clock() - ageMillis));

    // ---------- internals ----------

    private static (string Name, object? Value)[] Bind(Note note, bool includeId)
    {
        var values = new List<(string, object?)>
        {
            ("@title", note.Title),
            ("@body", note.Body),
            ("@folderId", note.FolderId),
            ("@colorTag", note.ColorTag is { } color ? StoredJson.ToStored(color) : null),
            ("@isPinned", SqlExtensions.Flag(note.IsPinned)),
            ("@isArchived", SqlExtensions.Flag(note.IsArchived)),
            ("@isTrashed", SqlExtensions.Flag(note.IsTrashed)),
            ("@createdAt", note.CreatedAt),
            ("@modifiedAt", note.ModifiedAt),
            ("@lastOpenedAt", note.LastOpenedAt),
            ("@trashedAt", note.TrashedAt),
            ("@sortOrder", note.SortOrder),
            ("@isLocked", SqlExtensions.Flag(note.IsLocked)),
            ("@isHidden", SqlExtensions.Flag(note.IsHidden)),
            ("@bodyStylesJson", StoredJson.SpansToJson(note.BodyStyles)),
            ("@titleStylesJson", StoredJson.SpansToJson(note.TitleStyles)),
            ("@paragraphsJson", StoredJson.ParagraphsToJson(note.Paragraphs)),
        };

        if (includeId)
        {
            values.Add(("@id", note.Id));
        }

        return values.ToArray();
    }

    private int Execute(string sql, params (string Name, object? Value)[] args)
    {
        using var connection = _database.Open();
        using var command = connection.Sql(sql, args);
        return command.ExecuteNonQuery();
    }

    private List<Note> Query(string sql, params (string Name, object? Value)[] args)
    {
        using var connection = _database.Open();
        var notes = new List<Note>();
        using (var command = connection.Sql(sql, args))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                notes.Add(ReadNote(reader));
            }
        }

        return AddTagsAndChecklists(connection, notes);
    }

    // Columns are read by position, in the order of ColumnNames.
    private static Note ReadNote(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        Title = r.GetString(1),
        Body = r.GetString(2),
        FolderId = r.GetNullableInt64(3),
        ColorTag = StoredJson.EnumFromStored<NoteColorTag>(r.GetNullableString(4)),
        IsPinned = r.GetFlag(5),
        IsArchived = r.GetFlag(6),
        IsTrashed = r.GetFlag(7),
        CreatedAt = r.GetInt64(8),
        ModifiedAt = r.GetInt64(9),
        LastOpenedAt = r.GetNullableInt64(10),
        TrashedAt = r.GetNullableInt64(11),
        SortOrder = r.GetInt64(12),
        IsLocked = r.GetFlag(13),
        IsHidden = r.GetFlag(14),
        BodyStyles = StoredJson.SpansFromJson(r.GetString(15)),
        TitleStyles = StoredJson.SpansFromJson(r.GetString(16)),
        Paragraphs = StoredJson.ParagraphsFromJson(r.GetString(17)),
    };

    private static List<Note> AddTagsAndChecklists(SqliteConnection connection, List<Note> notes)
    {
        if (notes.Count == 0)
        {
            return notes;
        }

        var tags = new Dictionary<long, List<Tag>>();
        var items = new Dictionary<long, List<ChecklistItem>>();

        // The ids come from the database (numbers), so they can be written into the SQL text.
        foreach (var chunk in notes.Select(n => n.Id).Chunk(500))
        {
            string ids = string.Join(",", chunk);

            using (var command = connection.Sql(
                $"""
                 SELECT r.noteId, t.id, t.name FROM note_tag_cross_ref r
                 INNER JOIN tags t ON t.id = r.tagId
                 WHERE r.noteId IN ({ids}) ORDER BY t.name
                 """))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    long noteId = reader.GetInt64(0);
                    if (!tags.TryGetValue(noteId, out var list))
                    {
                        tags[noteId] = list = [];
                    }

                    list.Add(new Tag(reader.GetInt64(1), reader.GetString(2)));
                }
            }

            using (var command = connection.Sql(
                $"SELECT id, noteId, text, isChecked, position FROM checklist_items WHERE noteId IN ({ids}) ORDER BY position, id"))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    long noteId = reader.GetInt64(1);
                    if (!items.TryGetValue(noteId, out var list))
                    {
                        items[noteId] = list = [];
                    }

                    list.Add(new ChecklistItem(reader.GetInt64(0), noteId, reader.GetString(2), reader.GetFlag(3), (int)reader.GetInt64(4)));
                }
            }
        }

        return notes
            .Select(n => n with
            {
                Tags = tags.TryGetValue(n.Id, out var t) ? t : [],
                ChecklistItems = items.TryGetValue(n.Id, out var c) ? c : [],
            })
            .ToList();
    }
}
