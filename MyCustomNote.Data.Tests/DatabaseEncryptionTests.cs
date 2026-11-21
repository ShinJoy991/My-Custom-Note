using System.Text;
using Microsoft.Data.Sqlite;
using MyCustomNote.Core.Models;

namespace MyCustomNote.Data.Tests;

[TestClass]
public sealed class DatabaseEncryptionTests
{
    private const string Marker = "unique-marker-zebra-4417";

    private static long AddNote(NoteDatabase database, string title = Marker)
    {
        var notes = new NoteRepository(database, () => 100);
        return notes.Create(new Note { Title = title, Body = "body of " + title, CreatedAt = 1, ModifiedAt = 1 });
    }

    private static bool FileContains(string path, string text) =>
        File.ReadAllText(path, Encoding.Latin1).Contains(text);

    [TestMethod]
    public void ANewDatabaseWithAPassword_IsEncryptedOnDisk()
    {
        using var temp = new TempDatabasePath();
        var database = new NoteDatabase(temp.Path, "correct horse battery");
        AddNote(database);

        Assert.IsTrue(NoteDatabase.FileIsEncrypted(temp.Path));
        Assert.IsFalse(FileContains(temp.Path, Marker), "the text is not readable in the file");
        Assert.IsFalse(FileContains(temp.Path, "SQLite format 3"), "and the file does not even look like SQLite");
        Assert.AreEqual(1, new NoteRepository(new NoteDatabase(temp.Path, "correct horse battery")).GetActive().Count);
    }

    [TestMethod]
    public void AWrongOrMissingPassword_IsRefused()
    {
        using var temp = new TempDatabasePath();
        _ = new NoteDatabase(temp.Path, "right password");

        Assert.Throws<WrongDatabasePasswordException>(() => new NoteDatabase(temp.Path, "wrong password"));
        Assert.Throws<WrongDatabasePasswordException>(() => new NoteDatabase(temp.Path));
        Assert.IsTrue(NoteDatabase.CheckPassword(temp.Path, "right password"));
        Assert.IsFalse(NoteDatabase.CheckPassword(temp.Path, "wrong password"));
        Assert.IsFalse(NoteDatabase.CheckPassword(temp.Path, ""));
    }

    [TestMethod]
    public void APlainDatabase_IsNotEncrypted_AndMissingFilesAreNot()
    {
        using var temp = new TempDatabasePath();
        Assert.IsFalse(NoteDatabase.FileIsEncrypted(temp.Path), "no file yet");

        AddNote(new NoteDatabase(temp.Path));

        Assert.IsFalse(NoteDatabase.FileIsEncrypted(temp.Path));
        Assert.IsTrue(FileContains(temp.Path, Marker), "a plain file shows its text");
        Assert.IsFalse(NoteDatabase.CheckPassword(temp.Path, "any password"), "a plain file does not open with a key");
        Assert.IsTrue(NoteDatabase.CheckPassword(temp.Path, ""));
    }

    [TestMethod]
    public void Encrypt_ConvertsAnExistingDatabase_KeepingEverything()
    {
        using var temp = new TempDatabasePath();
        var plain = new NoteDatabase(temp.Path);
        long id = AddNote(plain);
        var tags = new TagRepository(plain);
        new TagRepository(plain).AddToNote(id, tags.Create("kept-tag"));
        new FolderRepository(plain).Create(new Folder(0, "kept-folder"));
        int version = plain.Version;

        NoteDatabase.Encrypt(temp.Path, "a long password");

        Assert.IsTrue(NoteDatabase.FileIsEncrypted(temp.Path));
        Assert.IsFalse(FileContains(temp.Path, Marker));
        Assert.IsFalse(FileContains(temp.Path, "kept-tag"));
        Assert.IsFalse(FileContains(temp.Path, "kept-folder"));
        Assert.IsFalse(File.Exists(temp.Path + ".converting"), "no temporary file is left");

        var encrypted = new NoteDatabase(temp.Path, "a long password");
        Assert.AreEqual(version, encrypted.Version);
        var note = new NoteRepository(encrypted).GetById(id)!;
        Assert.AreEqual(Marker, note.Title);
        Assert.AreEqual("kept-tag", note.Tags.Single().Name);
        Assert.AreEqual("kept-folder", new FolderRepository(encrypted).GetAll().Single().Name);
    }

    [TestMethod]
    public void Encrypt_RefusesADatabaseThatIsAlreadyEncrypted()
    {
        using var temp = new TempDatabasePath();
        _ = new NoteDatabase(temp.Path, "password one");

        Assert.Throws<InvalidOperationException>(() => NoteDatabase.Encrypt(temp.Path, "password two"));
        Assert.IsTrue(NoteDatabase.CheckPassword(temp.Path, "password one"), "and nothing changed");
    }

    [TestMethod]
    public void Decrypt_GivesBackAPlainDatabase()
    {
        using var temp = new TempDatabasePath();
        long id = AddNote(new NoteDatabase(temp.Path, "secret password"));

        NoteDatabase.Decrypt(temp.Path, "secret password");

        Assert.IsFalse(NoteDatabase.FileIsEncrypted(temp.Path));
        Assert.AreEqual(Marker, new NoteRepository(new NoteDatabase(temp.Path)).GetById(id)!.Title);
        Assert.IsFalse(File.Exists(temp.Path + ".converting"));
    }

    [TestMethod]
    public void Decrypt_NeedsTheRightPassword_AndChangesNothingOtherwise()
    {
        using var temp = new TempDatabasePath();
        AddNote(new NoteDatabase(temp.Path, "secret password"));

        Assert.Throws<WrongDatabasePasswordException>(() => NoteDatabase.Decrypt(temp.Path, "not the password"));

        Assert.IsTrue(NoteDatabase.FileIsEncrypted(temp.Path));
        Assert.IsTrue(NoteDatabase.CheckPassword(temp.Path, "secret password"));
        Assert.IsFalse(File.Exists(temp.Path + ".converting"));
    }

    [TestMethod]
    public void Decrypt_RefusesAPlainDatabase()
    {
        using var temp = new TempDatabasePath();
        _ = new NoteDatabase(temp.Path);

        Assert.Throws<InvalidOperationException>(() => NoteDatabase.Decrypt(temp.Path, "x"));
    }

    [TestMethod]
    public void ChangePassword_KeepsTheDataAndReplacesThePassword()
    {
        using var temp = new TempDatabasePath();
        long id = AddNote(new NoteDatabase(temp.Path, "first password"));

        NoteDatabase.ChangePassword(temp.Path, "first password", "second password");

        Assert.IsFalse(NoteDatabase.CheckPassword(temp.Path, "first password"));
        Assert.AreEqual(Marker, new NoteRepository(new NoteDatabase(temp.Path, "second password")).GetById(id)!.Title);
        Assert.IsFalse(FileContains(temp.Path, Marker));
    }

    [TestMethod]
    public void ChangePassword_NeedsTheOldPassword()
    {
        using var temp = new TempDatabasePath();
        _ = new NoteDatabase(temp.Path, "first password");

        Assert.Throws<WrongDatabasePasswordException>(() => NoteDatabase.ChangePassword(temp.Path, "guess", "second password"));
        Assert.IsTrue(NoteDatabase.CheckPassword(temp.Path, "first password"));
    }

    [TestMethod]
    public void PasswordsWithQuotesAndUnicode_Work()
    {
        using var temp = new TempDatabasePath();
        string password = "it's a \"pass\" ph\u1EA9u 'x'";
        long id = AddNote(new NoteDatabase(temp.Path));

        NoteDatabase.Encrypt(temp.Path, password);
        Assert.AreEqual(Marker, new NoteRepository(new NoteDatabase(temp.Path, password)).GetById(id)!.Title);

        NoteDatabase.ChangePassword(temp.Path, password, "plain'password'2");
        Assert.IsTrue(NoteDatabase.CheckPassword(temp.Path, "plain'password'2"));

        NoteDatabase.Decrypt(temp.Path, "plain'password'2");
        Assert.AreEqual(Marker, new NoteRepository(new NoteDatabase(temp.Path)).GetById(id)!.Title);
    }

    [TestMethod]
    public void TheWholeRepositoryApi_WorksOnAnEncryptedDatabase()
    {
        using var temp = new TempDatabasePath();
        var database = new NoteDatabase(temp.Path, "repository password");
        var notes = new NoteRepository(database, () => 100);
        long a = AddNote(database, "a");
        long b = AddNote(database, "b");

        notes.SetPinned(a, true);
        notes.MoveToTrash(b);
        new ChecklistRepository(database).ReplaceItems(a, [new ChecklistItem(0, a, "item")]);

        Assert.AreEqual(1, notes.GetActive().Count);
        Assert.AreEqual(1, notes.GetTrashed().Count);
        Assert.AreEqual("item", notes.GetById(a)!.ChecklistItems.Single().Text);
        Assert.AreEqual(1, notes.Search("body of a").Count);
    }
}
