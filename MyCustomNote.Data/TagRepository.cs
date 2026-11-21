using MyCustomNote.Core.Models;

namespace MyCustomNote.Data;

public sealed class TagRepository
{
    private readonly NoteDatabase _database;

    public TagRepository(NoteDatabase database) => _database = database;

    /// <summary>Every tag by name. Only for places already behind the notes PIN: it also lists tags used only by locked or hidden notes.</summary>
    public IReadOnlyList<Tag> GetAll() => Query("SELECT id, name FROM tags ORDER BY name");

    /// <summary>
    /// Tags on at least one visible note (not locked, not hidden, not trashed). Use this anywhere tags are browsed
    /// without the PIN, so a tag used only by a locked or hidden note never shows its name.
    /// </summary>
    public IReadOnlyList<Tag> GetVisible() =>
        Query("""
              SELECT DISTINCT t.id, t.name FROM tags t
              INNER JOIN note_tag_cross_ref r ON t.id = r.tagId
              INNER JOIN notes n ON n.id = r.noteId
              WHERE n.isLocked = 0 AND n.isHidden = 0 AND n.isTrashed = 0
              ORDER BY t.name
              """);

    public Tag? GetById(long id) => Query("SELECT id, name FROM tags WHERE id = @id", ("@id", id)).FirstOrDefault();

    public IReadOnlyList<Tag> GetForNote(long noteId) =>
        Query("""
              SELECT t.id, t.name FROM tags t
              INNER JOIN note_tag_cross_ref r ON t.id = r.tagId
              WHERE r.noteId = @note ORDER BY t.name
              """,
            ("@note", noteId));

    /// <summary>Adds a tag and returns its id. If a tag with exactly this name exists, its id is returned (Android returns -1 there).</summary>
    public long Create(string name)
    {
        using var connection = _database.Open();
        using (var insert = connection.Sql("INSERT OR IGNORE INTO tags (name) VALUES (@name)", ("@name", name)))
        {
            insert.ExecuteNonQuery();
        }

        using var select = connection.Sql("SELECT id FROM tags WHERE name = @name", ("@name", name));
        return Convert.ToInt64(select.ExecuteScalar());
    }

    /// <summary>Renames a tag. Returns false (and changes nothing) if the tag does not exist or another tag already has that name.</summary>
    public bool Rename(long id, string newName)
    {
        using var connection = _database.Open();
        using var command = connection.Sql(
            "UPDATE tags SET name = @name WHERE id = @id AND NOT EXISTS (SELECT 1 FROM tags WHERE name = @name AND id <> @id)",
            ("@name", newName), ("@id", id));
        return command.ExecuteNonQuery() == 1;
    }

    /// <summary>Deletes a tag and its links to notes; the notes stay.</summary>
    public bool Delete(long id)
    {
        using var connection = _database.Open();
        using var command = connection.Sql("DELETE FROM tags WHERE id = @id", ("@id", id));
        return command.ExecuteNonQuery() == 1;
    }

    /// <summary>Puts a tag on a note (nothing happens if it is already there). Throws if the note or the tag does not exist.</summary>
    public void AddToNote(long noteId, long tagId)
    {
        using var connection = _database.Open();
        using var command = connection.Sql(
            "INSERT OR IGNORE INTO note_tag_cross_ref (noteId, tagId) VALUES (@note, @tag)", ("@note", noteId), ("@tag", tagId));
        command.ExecuteNonQuery();
    }

    public void RemoveFromNote(long noteId, long tagId)
    {
        using var connection = _database.Open();
        using var command = connection.Sql(
            "DELETE FROM note_tag_cross_ref WHERE noteId = @note AND tagId = @tag", ("@note", noteId), ("@tag", tagId));
        command.ExecuteNonQuery();
    }

    public void ClearForNote(long noteId)
    {
        using var connection = _database.Open();
        using var command = connection.Sql("DELETE FROM note_tag_cross_ref WHERE noteId = @note", ("@note", noteId));
        command.ExecuteNonQuery();
    }

    private List<Tag> Query(string sql, params (string Name, object? Value)[] args)
    {
        using var connection = _database.Open();
        using var command = connection.Sql(sql, args);
        using var reader = command.ExecuteReader();
        var tags = new List<Tag>();
        while (reader.Read())
        {
            tags.Add(new Tag(reader.GetInt64(0), reader.GetString(1)));
        }

        return tags;
    }
}
