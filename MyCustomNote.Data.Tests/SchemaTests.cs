using Microsoft.Data.Sqlite;

namespace MyCustomNote.Data.Tests;

[TestClass]
public sealed class SchemaTests
{
    [TestMethod]
    public void NewDatabase_IsCreatedAtTheLatestVersionWithAllTables()
    {
        using var temp = new TempDatabasePath();
        var database = new NoteDatabase(temp.Path);

        Assert.AreEqual(Schema.All.Max(m => m.Version), database.Version);

        using var connection = database.Open();
        var tables = connection.Column("SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'");
        CollectionAssert.AreEquivalent(
            new[] { "notes", "folders", "tags", "note_tag_cross_ref", "checklist_items" }, tables);
    }

    [TestMethod]
    public void NotesTable_HasTheAndroidColumnsPlusParagraphs()
    {
        using var temp = new TempDatabasePath();
        using var connection = new NoteDatabase(temp.Path).Open();

        var columns = connection.Column("SELECT name FROM pragma_table_info('notes')");
        CollectionAssert.AreEquivalent(
            new[]
            {
                "id", "title", "body", "folderId", "colorTag", "isPinned", "isArchived", "isTrashed",
                "createdAt", "modifiedAt", "lastOpenedAt", "trashedAt", "sortOrder", "isLocked", "isHidden",
                "bodyStylesJson", "titleStylesJson", "paragraphsJson",
            },
            columns);
    }

    [TestMethod]
    public void OtherTables_HaveTheAndroidColumns()
    {
        using var temp = new TempDatabasePath();
        using var connection = new NoteDatabase(temp.Path).Open();

        CollectionAssert.AreEquivalent(
            new[] { "id", "name", "parentFolderId", "sortOrder" },
            connection.Column("SELECT name FROM pragma_table_info('folders')"));
        CollectionAssert.AreEquivalent(
            new[] { "id", "name" },
            connection.Column("SELECT name FROM pragma_table_info('tags')"));
        CollectionAssert.AreEquivalent(
            new[] { "noteId", "tagId" },
            connection.Column("SELECT name FROM pragma_table_info('note_tag_cross_ref')"));
        CollectionAssert.AreEquivalent(
            new[] { "id", "noteId", "text", "isChecked", "position" },
            connection.Column("SELECT name FROM pragma_table_info('checklist_items')"));
    }

    [TestMethod]
    public void NewNote_GetsTheAndroidDefaults()
    {
        using var temp = new TempDatabasePath();
        using var connection = new NoteDatabase(temp.Path).Open();
        long id = connection.InsertNote();

        Assert.AreEqual(0, connection.Count($"SELECT isPinned + isArchived + isTrashed + isLocked + isHidden + sortOrder FROM notes WHERE id = {id}"));
        Assert.AreEqual(3, connection.Count(
            $"SELECT (bodyStylesJson = '[]') + (titleStylesJson = '[]') + (paragraphsJson = '[]') FROM notes WHERE id = {id}"));
    }

    [TestMethod]
    public void ForeignKeys_AreSwitchedOnForEveryConnection()
    {
        using var temp = new TempDatabasePath();
        var database = new NoteDatabase(temp.Path);

        using var first = database.Open();
        using var second = database.Open();
        Assert.AreEqual(1, first.Count("PRAGMA foreign_keys"));
        Assert.AreEqual(1, second.Count("PRAGMA foreign_keys"));
    }

    [TestMethod]
    public void ChecklistItem_ForAMissingNote_IsRejected()
    {
        using var temp = new TempDatabasePath();
        using var connection = new NoteDatabase(temp.Path).Open();

        Assert.ThrowsExactly<SqliteException>(
            () => connection.Run("INSERT INTO checklist_items (noteId, text) VALUES (999, 'x')"));
    }

    [TestMethod]
    public void DeletingANote_RemovesItsChecklistItemsAndTagLinksButKeepsTheTag()
    {
        using var temp = new TempDatabasePath();
        using var connection = new NoteDatabase(temp.Path).Open();
        long note = connection.InsertNote();
        connection.Run("INSERT INTO tags (name) VALUES ('work')");
        connection.Run($"INSERT INTO note_tag_cross_ref (noteId, tagId) VALUES ({note}, 1)");
        connection.Run($"INSERT INTO checklist_items (noteId, text) VALUES ({note}, 'a'), ({note}, 'b')");

        connection.Run($"DELETE FROM notes WHERE id = {note}");

        Assert.AreEqual(0, connection.Count("SELECT COUNT(*) FROM checklist_items"));
        Assert.AreEqual(0, connection.Count("SELECT COUNT(*) FROM note_tag_cross_ref"));
        Assert.AreEqual(1, connection.Count("SELECT COUNT(*) FROM tags"));
    }

    [TestMethod]
    public void DeletingATag_RemovesOnlyTheLinks()
    {
        using var temp = new TempDatabasePath();
        using var connection = new NoteDatabase(temp.Path).Open();
        long note = connection.InsertNote();
        connection.Run("INSERT INTO tags (name) VALUES ('work')");
        connection.Run($"INSERT INTO note_tag_cross_ref (noteId, tagId) VALUES ({note}, 1)");

        connection.Run("DELETE FROM tags WHERE id = 1");

        Assert.AreEqual(0, connection.Count("SELECT COUNT(*) FROM note_tag_cross_ref"));
        Assert.AreEqual(1, connection.Count("SELECT COUNT(*) FROM notes"));
    }

    [TestMethod]
    public void TagNames_AreUnique()
    {
        using var temp = new TempDatabasePath();
        using var connection = new NoteDatabase(temp.Path).Open();
        connection.Run("INSERT INTO tags (name) VALUES ('work')");

        Assert.ThrowsExactly<SqliteException>(() => connection.Run("INSERT INTO tags (name) VALUES ('work')"));
    }

    [TestMethod]
    public void SameTagTwiceOnOneNote_IsRejected()
    {
        using var temp = new TempDatabasePath();
        using var connection = new NoteDatabase(temp.Path).Open();
        long note = connection.InsertNote();
        connection.Run("INSERT INTO tags (name) VALUES ('work')");
        connection.Run($"INSERT INTO note_tag_cross_ref (noteId, tagId) VALUES ({note}, 1)");

        Assert.ThrowsExactly<SqliteException>(
            () => connection.Run($"INSERT INTO note_tag_cross_ref (noteId, tagId) VALUES ({note}, 1)"));
    }
}

[TestClass]
public sealed class MigrationTests
{
    private static readonly Migration V1 = new(1, ["CREATE TABLE t (a TEXT NOT NULL)"]);
    private static readonly Migration V2 = new(2, ["ALTER TABLE t ADD COLUMN b TEXT NOT NULL DEFAULT 'new'"]);

    [TestMethod]
    public void OpeningAgain_KeepsTheDataAndDoesNotRunMigrationsTwice()
    {
        using var temp = new TempDatabasePath();
        using (var connection = new NoteDatabase(temp.Path).Open())
        {
            connection.InsertNote("keep me");
        }

        var reopened = new NoteDatabase(temp.Path);
        using var again = reopened.Open();

        Assert.AreEqual(Schema.All.Max(m => m.Version), reopened.Version);
        Assert.AreEqual(1, again.Count("SELECT COUNT(*) FROM notes WHERE title = 'keep me'"));
    }

    [TestMethod]
    public void Upgrade_RunsOnlyTheMissingStepsAndKeepsTheData()
    {
        using var temp = new TempDatabasePath();
        using (var connection = new NoteDatabase(temp.Path, [V1]).Open())
        {
            connection.Run("INSERT INTO t (a) VALUES ('old row')");
        }

        var upgraded = new NoteDatabase(temp.Path, [V1, V2]);
        using var after = upgraded.Open();

        Assert.AreEqual(2, upgraded.Version);
        Assert.AreEqual(1, after.Count("SELECT COUNT(*) FROM t WHERE a = 'old row' AND b = 'new'"));
    }

    [TestMethod]
    public void FailingStep_RollsBackAndLeavesTheOldVersion()
    {
        using var temp = new TempDatabasePath();
        _ = new NoteDatabase(temp.Path, [V1]);
        var broken = new Migration(2, ["CREATE TABLE x (a TEXT)", "THIS IS NOT SQL"]);

        Assert.ThrowsExactly<SqliteException>(() => new NoteDatabase(temp.Path, [V1, broken]));

        var still = new NoteDatabase(temp.Path, [V1]);
        using var connection = still.Open();
        Assert.AreEqual(1, still.Version);
        Assert.AreEqual(0, connection.Count("SELECT COUNT(*) FROM sqlite_master WHERE name = 'x'"));
    }

    [TestMethod]
    public void DatabaseFromANewerApp_IsRejectedAndNotChanged()
    {
        using var temp = new TempDatabasePath();
        _ = new NoteDatabase(temp.Path, [V1, V2]);

        Assert.ThrowsExactly<InvalidOperationException>(() => new NoteDatabase(temp.Path, [V1]));

        Assert.AreEqual(2, new NoteDatabase(temp.Path, [V1, V2]).Version);
    }

    [TestMethod]
    public void MissingFolder_IsCreated()
    {
        string folder = Path.Combine(Path.GetTempPath(), "mycustomnote-tests", Guid.NewGuid().ToString(), "deeper");
        string file = Path.Combine(folder, "notes.db");
        try
        {
            var database = new NoteDatabase(file);

            Assert.IsTrue(File.Exists(file));
            Assert.AreEqual(Schema.All.Max(m => m.Version), database.Version);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(Path.GetDirectoryName(folder)!, recursive: true);
        }
    }
}
