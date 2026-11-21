using MyCustomNote.Core.Models;
using MyCustomNote.Core.Security;

namespace MyCustomNote.Data;

public sealed class ChecklistRepository
{
    private readonly NoteDatabase _database;

    public ChecklistRepository(NoteDatabase database) => _database = database;

    /// <summary>The items of a note in their saved order.</summary>
    public IReadOnlyList<ChecklistItem> GetItems(long noteId)
    {
        using var connection = _database.Open();
        using var command = connection.Sql(
            "SELECT id, noteId, text, isChecked, position FROM checklist_items WHERE noteId = @note ORDER BY position, id",
            ("@note", noteId));
        using var reader = command.ExecuteReader();
        var items = new List<ChecklistItem>();
        while (reader.Read())
        {
            items.Add(new ChecklistItem(
                reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetFlag(3), (int)reader.GetInt64(4)));
        }

        return items;
    }

    /// <summary>
    /// Saves a note's whole checklist: the old items are removed and the given ones stored in the given order
    /// (<see cref="ChecklistItem.Position"/> is set from the order; ids are assigned again). All or nothing.
    /// Throws if the note does not exist.
    /// </summary>
    public void ReplaceItems(long noteId, IEnumerable<ChecklistItem> items, VaultKey? key = null)
    {
        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();

        // The texts of a locked note are stored encrypted, so writing them needs the key.
        bool locked;
        using (var check = connection.Sql("SELECT isLocked FROM notes WHERE id = @note", ("@note", noteId)))
        {
            locked = check.ExecuteScalar() is { } value && Convert.ToInt64(value) != 0;
        }

        if (locked && key is null)
        {
            throw new InvalidOperationException("The checklist of a locked note cannot be saved without the key.");
        }

        using (var clear = connection.Sql("DELETE FROM checklist_items WHERE noteId = @note", ("@note", noteId)))
        {
            clear.ExecuteNonQuery();
        }

        int position = 0;
        foreach (var item in items)
        {
            using var insert = connection.Sql(
                "INSERT INTO checklist_items (noteId, text, isChecked, position) VALUES (@note, @text, @checked, @position)",
                ("@note", noteId), ("@text", locked ? key!.Encrypt(item.Text) : item.Text), ("@checked", SqlExtensions.Flag(item.IsChecked)), ("@position", position));
            insert.ExecuteNonQuery();
            position++;
        }

        transaction.Commit();
    }
}
