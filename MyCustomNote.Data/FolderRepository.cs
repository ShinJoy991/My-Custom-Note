using MyCustomNote.Core.Models;

namespace MyCustomNote.Data;

public sealed class FolderRepository
{
    private readonly NoteDatabase _database;

    public FolderRepository(NoteDatabase database) => _database = database;

    /// <summary>All folders in manual order.</summary>
    public IReadOnlyList<Folder> GetAll() =>
        Query("SELECT id, name, parentFolderId, sortOrder FROM folders ORDER BY sortOrder, id");

    public IReadOnlyList<Folder> GetSubFolders(long parentId) =>
        Query("SELECT id, name, parentFolderId, sortOrder FROM folders WHERE parentFolderId = @parent ORDER BY sortOrder, id",
            ("@parent", parentId));

    public Folder? GetById(long id) =>
        Query("SELECT id, name, parentFolderId, sortOrder FROM folders WHERE id = @id", ("@id", id)).FirstOrDefault();

    /// <summary>Adds a folder and returns its new id (<see cref="Folder.Id"/> is ignored).</summary>
    public long Create(Folder folder)
    {
        using var connection = _database.Open();
        using var command = connection.Sql(
            "INSERT INTO folders (name, parentFolderId, sortOrder) VALUES (@name, @parent, @order) RETURNING id",
            ("@name", folder.Name), ("@parent", folder.ParentFolderId), ("@order", folder.SortOrder));
        return Convert.ToInt64(command.ExecuteScalar());
    }

    public bool Update(Folder folder)
    {
        using var connection = _database.Open();
        using var command = connection.Sql(
            "UPDATE folders SET name = @name, parentFolderId = @parent, sortOrder = @order WHERE id = @id",
            ("@name", folder.Name), ("@parent", folder.ParentFolderId), ("@order", folder.SortOrder), ("@id", folder.Id));
        return command.ExecuteNonQuery() == 1;
    }

    /// <summary>
    /// Deletes a folder. Its notes stay and lose their folder (as on Android, the tables have no foreign key for this).
    /// Sub-folders move up to the deleted folder's parent so that none is left pointing at a missing folder
    /// (Android leaves them dangling; the Android screens may not use nested folders at all).
    /// </summary>
    public bool Delete(long id)
    {
        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();

        using (var notes = connection.Sql("UPDATE notes SET folderId = NULL WHERE folderId = @id", ("@id", id)))
        {
            notes.ExecuteNonQuery();
        }

        using (var children = connection.Sql(
            "UPDATE folders SET parentFolderId = (SELECT parentFolderId FROM folders WHERE id = @id) WHERE parentFolderId = @id",
            ("@id", id)))
        {
            children.ExecuteNonQuery();
        }

        using var delete = connection.Sql("DELETE FROM folders WHERE id = @id", ("@id", id));
        bool deleted = delete.ExecuteNonQuery() == 1;

        transaction.Commit();
        return deleted;
    }

    private List<Folder> Query(string sql, params (string Name, object? Value)[] args)
    {
        using var connection = _database.Open();
        using var command = connection.Sql(sql, args);
        using var reader = command.ExecuteReader();
        var folders = new List<Folder>();
        while (reader.Read())
        {
            folders.Add(new Folder(reader.GetInt64(0), reader.GetString(1), reader.GetNullableInt64(2), reader.GetInt64(3)));
        }

        return folders;
    }
}
