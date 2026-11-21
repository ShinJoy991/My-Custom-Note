using System.Security.Cryptography;
using System.Text;
using MyCustomNote.Core.Formatting;
using MyCustomNote.Core.Models;
using MyCustomNote.Core.Security;

namespace MyCustomNote.Data.Tests;

[TestClass]
public sealed class LockedNoteTests
{
    private static VaultKey NewKey(string pin = "1234") => Vault.TryUnlock(Vault.Create(pin, 1000), pin)!;

    private static long NoteWithChecklist(Repos repos, string title = "Secret plan", string body = "meet at the old bridge")
    {
        long id = repos.AddNote(n => n with
        {
            Title = title,
            Body = body,
            BodyStyles = [new StyleSpan(0, 4, TextStyleKind.Bold)],
            Paragraphs = [new ParagraphFormat(0, ParagraphKind.Heading)],
        });
        repos.Checklists.ReplaceItems(id, [new ChecklistItem(0, id, "buy a ticket"), new ChecklistItem(0, id, "pack", true)]);
        return id;
    }

    [TestMethod]
    public void Lock_EncryptsTitleBodyAndChecklist_AndSetsTheFlagAndTime()
    {
        using var repos = new Repos();
        var key = NewKey();
        long id = NoteWithChecklist(repos);
        repos.Now = 4444;

        Assert.IsTrue(repos.Notes.Lock(id, key));
        var raw = repos.Notes.GetById(id)!;

        Assert.IsTrue(raw.IsLocked);
        Assert.AreEqual(4444, raw.ModifiedAt);
        Assert.IsTrue(VaultKey.IsEncrypted(raw.Title));
        Assert.IsTrue(VaultKey.IsEncrypted(raw.Body));
        Assert.IsTrue(raw.ChecklistItems.All(i => VaultKey.IsEncrypted(i.Text)));
        Assert.IsFalse(raw.Title.Contains("Secret"));
        Assert.AreEqual(new StyleSpan(0, 4, TextStyleKind.Bold), raw.BodyStyles.Single(), "spans stay readable, as on Android");
        Assert.AreEqual(new ParagraphFormat(0, ParagraphKind.Heading), raw.Paragraphs.Single());
    }

    [TestMethod]
    public void ALockedNote_LeavesEveryNormalListAndShowsOnTheLockedList()
    {
        using var repos = new Repos();
        long id = NoteWithChecklist(repos);

        repos.Notes.Lock(id, NewKey());

        Assert.AreEqual(0, repos.Notes.GetActive().Count);
        Assert.AreEqual(0, repos.Notes.Search("bridge").Count);
        CollectionAssert.AreEqual(new[] { id }, repos.Notes.GetLocked().Select(n => n.Id).ToList());
    }

    [TestMethod]
    public void Decrypted_GivesBackTheOriginalText_AndCheckTextsToo()
    {
        using var repos = new Repos();
        var key = NewKey();
        long id = NoteWithChecklist(repos);
        repos.Notes.Lock(id, key);

        var plain = RepositoryDecrypt(repos.Notes.GetById(id)!, key);

        Assert.AreEqual("Secret plan", plain.Title);
        Assert.AreEqual("meet at the old bridge", plain.Body);
        CollectionAssert.AreEqual(new[] { "buy a ticket", "pack" }, plain.ChecklistItems.Select(i => i.Text).ToList());
        Assert.IsTrue(plain.ChecklistItems[1].IsChecked);
    }

    private static Note RepositoryDecrypt(Note note, VaultKey? key) => NoteRepository.Decrypted(note, key);

    [TestMethod]
    public void Decrypted_WithoutAKeyOrWithAWrongKey_ReturnsTheNoteAsStored()
    {
        using var repos = new Repos();
        long id = NoteWithChecklist(repos);
        repos.Notes.Lock(id, NewKey("1111"));
        var raw = repos.Notes.GetById(id)!;

        Assert.AreEqual(raw.Title, NoteRepository.Decrypted(raw, null).Title);
        Assert.AreEqual(raw.Title, NoteRepository.Decrypted(raw, NewKey("2222")).Title);
    }

    [TestMethod]
    public void Unlock_RestoresTheTextAndTheFlag()
    {
        using var repos = new Repos();
        var key = NewKey();
        long id = NoteWithChecklist(repos);
        repos.Notes.Lock(id, key);

        Assert.IsTrue(repos.Notes.Unlock(id, key));
        var back = repos.Notes.GetById(id)!;

        Assert.IsFalse(back.IsLocked);
        Assert.AreEqual("Secret plan", back.Title);
        Assert.AreEqual("meet at the old bridge", back.Body);
        CollectionAssert.AreEqual(new[] { "buy a ticket", "pack" }, back.ChecklistItems.Select(i => i.Text).ToList());
        Assert.AreEqual(1, repos.Notes.GetActive().Count);
    }

    [TestMethod]
    public void LockAndUnlock_ReturnFalseWhenThereIsNothingToDo()
    {
        using var repos = new Repos();
        var key = NewKey();
        long id = repos.AddNote();

        Assert.IsFalse(repos.Notes.Unlock(id, key), "not locked");
        Assert.IsTrue(repos.Notes.Lock(id, key));
        Assert.IsFalse(repos.Notes.Lock(id, key), "already locked");
        Assert.IsFalse(repos.Notes.Lock(999, key), "missing");
        Assert.IsFalse(repos.Notes.Unlock(999, key), "missing");
    }

    [TestMethod]
    public void UnlockingWithAnotherKey_Throws_AndChangesNothing()
    {
        using var repos = new Repos();
        long id = NoteWithChecklist(repos);
        repos.Notes.Lock(id, NewKey("1111"));
        var before = repos.Notes.GetById(id)!;

        Assert.Throws<CryptographicException>(() => repos.Notes.Unlock(id, NewKey("2222")));

        var after = repos.Notes.GetById(id)!;
        Assert.IsTrue(after.IsLocked);
        Assert.AreEqual(before.Title, after.Title);
        Assert.AreEqual(before.Body, after.Body);
        CollectionAssert.AreEqual(
            before.ChecklistItems.Select(i => i.Text).ToList(),
            after.ChecklistItems.Select(i => i.Text).ToList());
    }

    [TestMethod]
    public void UpdateContent_OnALockedNote_NeedsTheKeyAndStoresEncryptedText()
    {
        using var repos = new Repos();
        var key = NewKey();
        long id = NoteWithChecklist(repos);
        repos.Notes.Lock(id, key);
        var content = new NoteContent("New title", [], "New secret body", [], []);

        Assert.IsFalse(repos.Notes.UpdateContent(id, content), "no key: refused");
        Assert.IsTrue(NoteRepository.Decrypted(repos.Notes.GetById(id)!, key).Title == "Secret plan", "and nothing changed");

        Assert.IsTrue(repos.Notes.UpdateContent(id, content, key));
        var raw = repos.Notes.GetById(id)!;
        Assert.IsTrue(VaultKey.IsEncrypted(raw.Title));
        Assert.IsFalse(raw.Body.Contains("New secret"));
        Assert.AreEqual("New secret body", NoteRepository.Decrypted(raw, key).Body);
    }

    [TestMethod]
    public void UpdateContent_OnANormalNote_StaysPlainEvenIfAKeyIsGiven()
    {
        using var repos = new Repos();
        long id = repos.AddNote();

        repos.Notes.UpdateContent(id, new NoteContent("plain", [], "text", [], []), NewKey());

        Assert.AreEqual("plain", repos.Notes.GetById(id)!.Title);
    }

    [TestMethod]
    public void ReplaceItems_OnALockedNote_NeedsTheKeyAndStoresEncryptedTexts()
    {
        using var repos = new Repos();
        var key = NewKey();
        long id = NoteWithChecklist(repos);
        repos.Notes.Lock(id, key);

        Assert.Throws<InvalidOperationException>(() => repos.Checklists.ReplaceItems(id, [new ChecklistItem(0, id, "x")]));

        repos.Checklists.ReplaceItems(id, [new ChecklistItem(0, id, "new item", true)], key);
        var raw = repos.Checklists.GetItems(id).Single();
        Assert.IsTrue(VaultKey.IsEncrypted(raw.Text));
        Assert.AreEqual("new item", key.Decrypt(raw.Text));
        Assert.IsTrue(raw.IsChecked);
    }

    [TestMethod]
    public void TheDatabaseFile_DoesNotContainThePlainTextOfALockedNote()
    {
        using var repos = new Repos();
        long id = NoteWithChecklist(repos, "Zebra-title-9137", "quokka-body-5521 and more words so the row is long enough");
        repos.Checklists.ReplaceItems(id, [new ChecklistItem(0, id, "walrus-item-7788")]);

        repos.Notes.Lock(id, NewKey());

        string file = File.ReadAllText(repos.Database.Path, Encoding.Latin1);
        Assert.IsFalse(file.Contains("Zebra-title-9137"), "title");
        Assert.IsFalse(file.Contains("quokka-body-5521"), "body");
        Assert.IsFalse(file.Contains("walrus-item-7788"), "checklist");
    }

    [TestMethod]
    public void LockingKeepsTagsAndFolder_ButTheTagIsNotVisibleWhenOnlyLockedNotesUseIt()
    {
        using var repos = new Repos();
        long folder = repos.Folders.Create(new Folder(0, "f"));
        long id = repos.AddNote(n => n with { FolderId = folder });
        long tag = repos.Tags.Create("private-tag");
        repos.Tags.AddToNote(id, tag);

        repos.Notes.Lock(id, NewKey());

        Assert.AreEqual(folder, repos.Notes.GetById(id)!.FolderId);
        Assert.AreEqual("private-tag", repos.Notes.GetById(id)!.Tags.Single().Name);
        Assert.AreEqual(0, repos.Tags.GetVisible().Count);
    }
}
