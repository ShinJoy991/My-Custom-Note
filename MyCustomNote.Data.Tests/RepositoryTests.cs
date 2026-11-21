using Microsoft.Data.Sqlite;
using MyCustomNote.Core.Formatting;
using MyCustomNote.Core.Models;

namespace MyCustomNote.Data.Tests;

/// <summary>A temporary database with all repositories and a clock the test controls.</summary>
internal sealed class Repos : IDisposable
{
    private readonly TempDatabasePath _temp = new();

    public Repos()
    {
        Database = new NoteDatabase(_temp.Path);
        Notes = new NoteRepository(Database, () => Now);
        Folders = new FolderRepository(Database);
        Tags = new TagRepository(Database);
        Checklists = new ChecklistRepository(Database);
    }

    public long Now { get; set; } = 1_000_000;

    public NoteDatabase Database { get; }

    public NoteRepository Notes { get; }

    public FolderRepository Folders { get; }

    public TagRepository Tags { get; }

    public ChecklistRepository Checklists { get; }

    /// <summary>Creates a note with the given changes applied to a plain note and returns its id.</summary>
    public long AddNote(Func<Note, Note>? change = null, string title = "note")
    {
        var note = new Note { Title = title, CreatedAt = 10, ModifiedAt = 10 };
        return Notes.Create(change is null ? note : change(note));
    }

    public void Dispose() => _temp.Dispose();
}

[TestClass]
public sealed class NoteRepositoryTests
{
    [TestMethod]
    public void Create_ThenGetById_KeepsEveryField()
    {
        using var repos = new Repos();
        var note = new Note
        {
            Title = "Shopping",
            Body = "milk\neggs 😀",
            FolderId = 7,
            ColorTag = NoteColorTag.Blue,
            IsPinned = true,
            IsArchived = true,
            CreatedAt = 111,
            ModifiedAt = 222,
            LastOpenedAt = 333,
            SortOrder = 5,
            BodyStyles = [new StyleSpan(0, 4, TextStyleKind.Bold), new StyleSpan(5, 9, TextStyleKind.Italic)],
            TitleStyles = [new StyleSpan(0, 8, TextStyleKind.Underline)],
            Paragraphs = [new ParagraphFormat(0, ParagraphKind.Heading), new ParagraphFormat(1, ParagraphKind.Bullet)],
        };

        long id = repos.Notes.Create(note);
        var loaded = repos.Notes.GetById(id);

        Assert.IsNotNull(loaded);
        Assert.AreEqual(id, loaded.Id);
        Assert.AreEqual("Shopping", loaded.Title);
        Assert.AreEqual("milk\neggs 😀", loaded.Body);
        Assert.AreEqual(7, loaded.FolderId);
        Assert.AreEqual(NoteColorTag.Blue, loaded.ColorTag);
        Assert.IsTrue(loaded.IsPinned);
        Assert.IsTrue(loaded.IsArchived);
        Assert.IsFalse(loaded.IsTrashed);
        Assert.IsFalse(loaded.IsLocked);
        Assert.IsFalse(loaded.IsHidden);
        Assert.AreEqual(111, loaded.CreatedAt);
        Assert.AreEqual(222, loaded.ModifiedAt);
        Assert.AreEqual(333, loaded.LastOpenedAt);
        Assert.IsNull(loaded.TrashedAt);
        Assert.AreEqual(5, loaded.SortOrder);
        CollectionAssert.AreEqual(note.BodyStyles.ToList(), loaded.BodyStyles.ToList());
        CollectionAssert.AreEqual(note.TitleStyles.ToList(), loaded.TitleStyles.ToList());
        CollectionAssert.AreEqual(note.Paragraphs.ToList(), loaded.Paragraphs.ToList());
    }

    [TestMethod]
    public void Create_IgnoresTheGivenIdAndReturnsNewIds()
    {
        using var repos = new Repos();
        long first = repos.Notes.Create(new Note { Id = 99, CreatedAt = 1, ModifiedAt = 1 });
        long second = repos.Notes.Create(new Note { Id = 99, CreatedAt = 1, ModifiedAt = 1 });

        Assert.AreNotEqual(99, first);
        Assert.AreNotEqual(first, second);
    }

    [TestMethod]
    public void MissingColorAndFolder_StayNull()
    {
        using var repos = new Repos();
        var loaded = repos.Notes.GetById(repos.AddNote());

        Assert.IsNull(loaded!.ColorTag);
        Assert.IsNull(loaded.FolderId);
        Assert.IsNull(loaded.LastOpenedAt);
        Assert.IsNull(loaded.TrashedAt);
    }

    [TestMethod]
    public void Update_SavesTheFieldsAndReportsAMissingNote()
    {
        using var repos = new Repos();
        long id = repos.AddNote();

        bool updated = repos.Notes.Update(new Note
        {
            Id = id, Title = "new", Body = "text", ColorTag = NoteColorTag.Red, CreatedAt = 10, ModifiedAt = 50,
            BodyStyles = [new StyleSpan(0, 2, TextStyleKind.Bold)],
        });
        var loaded = repos.Notes.GetById(id)!;

        Assert.IsTrue(updated);
        Assert.AreEqual("new", loaded.Title);
        Assert.AreEqual(NoteColorTag.Red, loaded.ColorTag);
        Assert.AreEqual(50, loaded.ModifiedAt);
        Assert.AreEqual(1, loaded.BodyStyles.Count);
        Assert.IsFalse(repos.Notes.Update(new Note { Id = 12345, CreatedAt = 1, ModifiedAt = 1 }));
    }

    [TestMethod]
    public void GetActive_ExcludesTrashedArchivedLockedAndHidden_NewestModifiedFirst()
    {
        using var repos = new Repos();
        long older = repos.AddNote(n => n with { ModifiedAt = 100 }, "older");
        long newer = repos.AddNote(n => n with { ModifiedAt = 200 }, "newer");
        repos.AddNote(n => n with { IsTrashed = true }, "trashed");
        repos.AddNote(n => n with { IsArchived = true }, "archived");
        repos.AddNote(n => n with { IsLocked = true }, "locked");
        repos.AddNote(n => n with { IsHidden = true }, "hidden");

        var ids = repos.Notes.GetActive().Select(n => n.Id).ToList();

        CollectionAssert.AreEqual(new[] { newer, older }, ids);
    }

    [TestMethod]
    public void LockedAndHiddenNotes_NeverAppearInANormalQuery()
    {
        using var repos = new Repos();
        long tag = repos.Tags.Create("work");
        long folder = repos.Folders.Create(new Folder(0, "f"));

        long locked = repos.AddNote(n => n with { IsLocked = true, FolderId = folder, Title = "secret locked" });
        long hidden = repos.AddNote(n => n with { IsHidden = true, FolderId = folder, Title = "secret hidden" });
        long lockedArchived = repos.AddNote(n => n with { IsLocked = true, IsArchived = true, Title = "secret archived" });
        long lockedTrashed = repos.AddNote(n => n with { IsLocked = true, IsTrashed = true, TrashedAt = 5, Title = "secret trashed" });
        foreach (long id in new[] { locked, hidden, lockedArchived, lockedTrashed })
        {
            repos.Tags.AddToNote(id, tag);
        }

        Assert.AreEqual(0, repos.Notes.GetActive().Count, "active");
        Assert.AreEqual(0, repos.Notes.GetInFolder(folder).Count, "folder");
        Assert.AreEqual(0, repos.Notes.GetWithTag(tag).Count, "tag");
        Assert.AreEqual(0, repos.Notes.GetArchived().Count, "archive");
        Assert.AreEqual(0, repos.Notes.GetTrashed().Count, "trash");
        Assert.AreEqual(0, repos.Notes.Search("secret").Count, "search");
    }

    [TestMethod]
    public void LockedAndHiddenScreens_ShowOnlyTheirOwnNotes()
    {
        using var repos = new Repos();
        long locked = repos.AddNote(n => n with { IsLocked = true });
        long hidden = repos.AddNote(n => n with { IsHidden = true });
        long both = repos.AddNote(n => n with { IsLocked = true, IsHidden = true });
        repos.AddNote(n => n with { IsLocked = true, IsTrashed = true });
        repos.AddNote();

        CollectionAssert.AreEquivalent(new[] { locked }, repos.Notes.GetLocked().Select(n => n.Id).ToList());
        CollectionAssert.AreEquivalent(new[] { hidden, both }, repos.Notes.GetHidden().Select(n => n.Id).ToList());
        Assert.IsNotNull(repos.Notes.GetById(both));
    }

    [TestMethod]
    public void Trash_MovesTheNoteWithTheTimeAndRestoreBringsItBack()
    {
        using var repos = new Repos();
        long id = repos.AddNote();
        repos.Now = 5000;

        Assert.IsTrue(repos.Notes.MoveToTrash(id));
        Assert.AreEqual(0, repos.Notes.GetActive().Count);
        var trashed = repos.Notes.GetTrashed().Single();
        Assert.AreEqual(5000, trashed.TrashedAt);
        Assert.IsTrue(trashed.IsTrashed);

        Assert.IsTrue(repos.Notes.RestoreFromTrash(id));
        Assert.AreEqual(1, repos.Notes.GetActive().Count);
        Assert.IsNull(repos.Notes.GetById(id)!.TrashedAt);
    }

    [TestMethod]
    public void Trash_ListsTheMostRecentlyTrashedFirst()
    {
        using var repos = new Repos();
        long a = repos.AddNote();
        long b = repos.AddNote();
        repos.Now = 100;
        repos.Notes.MoveToTrash(a);
        repos.Now = 200;
        repos.Notes.MoveToTrash(b);

        CollectionAssert.AreEqual(new[] { b, a }, repos.Notes.GetTrashed().Select(n => n.Id).ToList());
    }

    [TestMethod]
    public void Archive_SetsTheFlagAndTrashedNotesAreNotListed()
    {
        using var repos = new Repos();
        long archived = repos.AddNote();
        long archivedThenTrashed = repos.AddNote();
        repos.Notes.SetArchived(archived, true);
        repos.Notes.SetArchived(archivedThenTrashed, true);
        repos.Notes.MoveToTrash(archivedThenTrashed);

        CollectionAssert.AreEqual(new[] { archived }, repos.Notes.GetArchived().Select(n => n.Id).ToList());

        repos.Notes.SetArchived(archived, false);
        Assert.AreEqual(0, repos.Notes.GetArchived().Count);
        Assert.AreEqual(1, repos.Notes.GetActive().Count);
    }

    [TestMethod]
    public void PinAndHide_ChangeTheFlag()
    {
        using var repos = new Repos();
        long id = repos.AddNote();

        repos.Notes.SetPinned(id, true);
        Assert.IsTrue(repos.Notes.GetById(id)!.IsPinned);

        repos.Notes.SetHidden(id, true);
        Assert.AreEqual(0, repos.Notes.GetActive().Count);
        Assert.AreEqual(1, repos.Notes.GetHidden().Count);

        repos.Notes.SetHidden(id, false);
        Assert.AreEqual(1, repos.Notes.GetActive().Count);
    }

    [TestMethod]
    public void Search_FindsTitleAndBody_IgnoresCaseOfLatinLetters_AndIncludesArchived()
    {
        using var repos = new Repos();
        long inTitle = repos.AddNote(n => n with { Title = "Holiday plans" });
        long inBody = repos.AddNote(n => n with { Body = "book the HOLIDAY flight" }, "other");
        long archived = repos.AddNote(n => n with { Title = "holiday archive", IsArchived = true });
        repos.AddNote(n => n with { Title = "holiday trash", IsTrashed = true });
        repos.AddNote(title: "unrelated");

        var found = repos.Notes.Search("holiday").Select(n => n.Id).ToList();

        CollectionAssert.AreEquivalent(new[] { inTitle, inBody, archived }, found);
    }

    [TestMethod]
    public void Search_TreatsPercentUnderscoreAndBackslashAsPlainCharacters()
    {
        using var repos = new Repos();
        long percent = repos.AddNote(title: "save 100% now");
        long underscore = repos.AddNote(title: "snake_case");
        long backslash = repos.AddNote(title: @"C:\temp");
        repos.AddNote(title: "snakeXcase 100 now");

        CollectionAssert.AreEquivalent(new[] { percent }, repos.Notes.Search("100%").Select(n => n.Id).ToList());
        CollectionAssert.AreEquivalent(new[] { underscore }, repos.Notes.Search("snake_c").Select(n => n.Id).ToList());
        CollectionAssert.AreEquivalent(new[] { backslash }, repos.Notes.Search(@"C:\t").Select(n => n.Id).ToList());
        Assert.AreEqual(0, repos.Notes.Search("%x%").Count);
    }

    [TestMethod]
    public void Search_FindsTextWithQuotes()
    {
        using var repos = new Repos();
        long id = repos.AddNote(title: "it's a \"test\"");

        CollectionAssert.AreEqual(new[] { id }, repos.Notes.Search("it's").Select(n => n.Id).ToList());
    }

    [TestMethod]
    public void NotesInAFolder_AreTheActiveOnesOfThatFolder()
    {
        using var repos = new Repos();
        long folder = repos.Folders.Create(new Folder(0, "f"));
        long inside = repos.AddNote(n => n with { FolderId = folder });
        repos.AddNote(n => n with { FolderId = folder, IsArchived = true });
        repos.AddNote(n => n with { FolderId = folder, IsTrashed = true });
        repos.AddNote();

        CollectionAssert.AreEqual(new[] { inside }, repos.Notes.GetInFolder(folder).Select(n => n.Id).ToList());
    }

    [TestMethod]
    public void NotesWithATag_IncludeArchivedButNotTrashed()
    {
        using var repos = new Repos();
        long tag = repos.Tags.Create("t");
        long active = repos.AddNote();
        long archived = repos.AddNote(n => n with { IsArchived = true });
        long trashed = repos.AddNote(n => n with { IsTrashed = true });
        long untagged = repos.AddNote();
        foreach (long id in new[] { active, archived, trashed })
        {
            repos.Tags.AddToNote(id, tag);
        }

        CollectionAssert.AreEquivalent(new[] { active, archived }, repos.Notes.GetWithTag(tag).Select(n => n.Id).ToList());
        Assert.AreNotEqual(0, untagged);
    }

    [TestMethod]
    public void ReadNotes_CarryTheirTagsByNameAndChecklistInOrder()
    {
        using var repos = new Repos();
        long id = repos.AddNote();
        repos.AddNote();
        long beta = repos.Tags.Create("beta");
        long alpha = repos.Tags.Create("alpha");
        repos.Tags.AddToNote(id, beta);
        repos.Tags.AddToNote(id, alpha);
        repos.Checklists.ReplaceItems(id, [new ChecklistItem(0, id, "first"), new ChecklistItem(0, id, "second", true)]);

        var note = repos.Notes.GetActive().Single(n => n.Id == id);

        CollectionAssert.AreEqual(new[] { "alpha", "beta" }, note.Tags.Select(t => t.Name).ToList());
        CollectionAssert.AreEqual(new[] { "first", "second" }, note.ChecklistItems.Select(i => i.Text).ToList());
        Assert.IsTrue(note.ChecklistItems[1].IsChecked);
        Assert.AreEqual(0, repos.Notes.GetActive().Single(n => n.Id != id).Tags.Count);
    }

    [TestMethod]
    public void ManyNotes_AreReadWithTheirTags()
    {
        using var repos = new Repos();
        long tag = repos.Tags.Create("t");
        for (int i = 0; i < 1100; i++)
        {
            repos.Tags.AddToNote(repos.AddNote(), tag);
        }

        var notes = repos.Notes.GetActive();

        Assert.AreEqual(1100, notes.Count);
        Assert.IsTrue(notes.All(n => n.Tags.Count == 1));
    }

    [TestMethod]
    public void SetFolder_MovesAndRemovesANoteFromAFolder()
    {
        using var repos = new Repos();
        long folder = repos.Folders.Create(new Folder(0, "f"));
        long id = repos.AddNote();

        Assert.IsTrue(repos.Notes.SetFolder(id, folder));
        Assert.AreEqual(folder, repos.Notes.GetById(id)!.FolderId);
        CollectionAssert.AreEqual(new[] { id }, repos.Notes.GetInFolder(folder).Select(n => n.Id).ToList());

        Assert.IsTrue(repos.Notes.SetFolder(id, null));
        Assert.IsNull(repos.Notes.GetById(id)!.FolderId);
        Assert.IsFalse(repos.Notes.SetFolder(999, folder));
    }

    [TestMethod]
    public void SetColor_SetsAndClearsTheColorTag()
    {
        using var repos = new Repos();
        long id = repos.AddNote();

        repos.Notes.SetColor(id, NoteColorTag.Orange);
        Assert.AreEqual(NoteColorTag.Orange, repos.Notes.GetById(id)!.ColorTag);

        repos.Notes.SetColor(id, NoteColorTag.Default);
        Assert.IsNull(repos.Notes.GetById(id)!.ColorTag);

        repos.Notes.SetColor(id, NoteColorTag.Blue);
        repos.Notes.SetColor(id, null);
        Assert.IsNull(repos.Notes.GetById(id)!.ColorTag);
    }

    [TestMethod]
    public void UpdateContent_ChangesOnlyTheContentAndTheModifiedTime()
    {
        using var repos = new Repos();
        long id = repos.AddNote(n => n with { IsPinned = true, IsArchived = true, FolderId = 4, ColorTag = NoteColorTag.Green, CreatedAt = 10, ModifiedAt = 10 });
        repos.Now = 777;

        bool saved = repos.Notes.UpdateContent(id, new NoteContent(
            "New title",
            [new StyleSpan(0, 3, TextStyleKind.Bold)],
            "New body\nline 2",
            [new StyleSpan(4, 8, TextStyleKind.Italic)],
            [new ParagraphFormat(1, ParagraphKind.Bullet)]));
        var loaded = repos.Notes.GetById(id)!;

        Assert.IsTrue(saved);
        Assert.AreEqual("New title", loaded.Title);
        Assert.AreEqual("New body\nline 2", loaded.Body);
        Assert.AreEqual(new StyleSpan(0, 3, TextStyleKind.Bold), loaded.TitleStyles.Single());
        Assert.AreEqual(new StyleSpan(4, 8, TextStyleKind.Italic), loaded.BodyStyles.Single());
        Assert.AreEqual(new ParagraphFormat(1, ParagraphKind.Bullet), loaded.Paragraphs.Single());
        Assert.AreEqual(777, loaded.ModifiedAt);

        // everything that is not content stays as it was
        Assert.IsTrue(loaded.IsPinned);
        Assert.IsTrue(loaded.IsArchived);
        Assert.AreEqual(4, loaded.FolderId);
        Assert.AreEqual(NoteColorTag.Green, loaded.ColorTag);
        Assert.AreEqual(10, loaded.CreatedAt);
    }

    [TestMethod]
    public void UpdateContent_ForAMissingNote_ReturnsFalse()
    {
        using var repos = new Repos();

        Assert.IsFalse(repos.Notes.UpdateContent(999, new NoteContent("t", [], "b", [], [])));
    }

    [TestMethod]
    public void UpdateSortOrder_SetsEveryNote()
    {
        using var repos = new Repos();
        long a = repos.AddNote();
        long b = repos.AddNote();

        repos.Notes.UpdateSortOrder([(a, 2), (b, 1)]);

        Assert.AreEqual(2, repos.Notes.GetById(a)!.SortOrder);
        Assert.AreEqual(1, repos.Notes.GetById(b)!.SortOrder);
    }

    [TestMethod]
    public void UpdateLastOpened_UsesTheClock()
    {
        using var repos = new Repos();
        long id = repos.AddNote();
        repos.Now = 4242;

        repos.Notes.UpdateLastOpened(id);

        Assert.AreEqual(4242, repos.Notes.GetById(id)!.LastOpenedAt);
    }

    [TestMethod]
    public void PermanentlyDelete_RemovesTheNoteItsTagLinksAndItsChecklist()
    {
        using var repos = new Repos();
        long id = repos.AddNote();
        long tag = repos.Tags.Create("t");
        repos.Tags.AddToNote(id, tag);
        repos.Checklists.ReplaceItems(id, [new ChecklistItem(0, id, "x")]);

        Assert.IsTrue(repos.Notes.PermanentlyDelete(id));

        Assert.IsNull(repos.Notes.GetById(id));
        Assert.AreEqual(0, repos.Checklists.GetItems(id).Count);
        Assert.IsNotNull(repos.Tags.GetById(tag));
        Assert.IsFalse(repos.Notes.PermanentlyDelete(id));
    }

    [TestMethod]
    public void PurgeTrash_DeletesOnlyNotesTrashedLongerAgoThanTheAge()
    {
        using var repos = new Repos();
        long old = repos.AddNote();
        long recent = repos.AddNote();
        long notTrashed = repos.AddNote();
        repos.Now = 1000;
        repos.Notes.MoveToTrash(old);
        repos.Now = 9000;
        repos.Notes.MoveToTrash(recent);

        repos.Now = 10_000;
        int removed = repos.Notes.PurgeTrashOlderThan(5000); // everything trashed before time 5000

        Assert.AreEqual(1, removed);
        Assert.IsNull(repos.Notes.GetById(old));
        Assert.IsNotNull(repos.Notes.GetById(recent));
        Assert.IsNotNull(repos.Notes.GetById(notTrashed));
    }
}

[TestClass]
public sealed class FolderRepositoryTests
{
    [TestMethod]
    public void Folders_AreListedInManualOrder_AndCanBeUpdated()
    {
        using var repos = new Repos();
        long b = repos.Folders.Create(new Folder(0, "B", null, 2));
        long a = repos.Folders.Create(new Folder(0, "A", null, 1));

        CollectionAssert.AreEqual(new[] { a, b }, repos.Folders.GetAll().Select(f => f.Id).ToList());

        Assert.IsTrue(repos.Folders.Update(new Folder(a, "A renamed", b, 9)));
        var loaded = repos.Folders.GetById(a)!;
        Assert.AreEqual("A renamed", loaded.Name);
        Assert.AreEqual(b, loaded.ParentFolderId);
        Assert.AreEqual(9, loaded.SortOrder);
        CollectionAssert.AreEqual(new[] { a }, repos.Folders.GetSubFolders(b).Select(f => f.Id).ToList());
        Assert.IsFalse(repos.Folders.Update(new Folder(999, "x")));
    }

    [TestMethod]
    public void DeletingAFolder_KeepsItsNotesAndClearsTheirFolder()
    {
        using var repos = new Repos();
        long folder = repos.Folders.Create(new Folder(0, "f"));
        long other = repos.Folders.Create(new Folder(0, "other"));
        long inside = repos.AddNote(n => n with { FolderId = folder });
        long elsewhere = repos.AddNote(n => n with { FolderId = other });

        Assert.IsTrue(repos.Folders.Delete(folder));

        Assert.IsNull(repos.Folders.GetById(folder));
        Assert.IsNull(repos.Notes.GetById(inside)!.FolderId);
        Assert.AreEqual(other, repos.Notes.GetById(elsewhere)!.FolderId);
        Assert.IsFalse(repos.Folders.Delete(folder));
    }

    [TestMethod]
    public void DeletingAFolder_MovesItsSubFoldersUpToTheParent()
    {
        using var repos = new Repos();
        long top = repos.Folders.Create(new Folder(0, "top"));
        long middle = repos.Folders.Create(new Folder(0, "middle", top));
        long child = repos.Folders.Create(new Folder(0, "child", middle));

        repos.Folders.Delete(middle);
        Assert.AreEqual(top, repos.Folders.GetById(child)!.ParentFolderId);

        repos.Folders.Delete(top);
        Assert.IsNull(repos.Folders.GetById(child)!.ParentFolderId);
    }
}

[TestClass]
public sealed class TagRepositoryTests
{
    [TestMethod]
    public void CreatingTheSameNameTwice_ReturnsTheSameTag()
    {
        using var repos = new Repos();

        long first = repos.Tags.Create("work");
        long second = repos.Tags.Create("work");

        Assert.AreEqual(first, second);
        Assert.AreEqual(1, repos.Tags.GetAll().Count);
    }

    [TestMethod]
    public void Tags_AreListedByName()
    {
        using var repos = new Repos();
        repos.Tags.Create("b");
        repos.Tags.Create("c");
        repos.Tags.Create("a");

        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, repos.Tags.GetAll().Select(t => t.Name).ToList());
    }

    [TestMethod]
    public void VisibleTags_AreOnlyThoseOnAtLeastOneNormalNote()
    {
        using var repos = new Repos();
        long onNormal = repos.Tags.Create("normal");
        long onLocked = repos.Tags.Create("locked-only");
        long onHidden = repos.Tags.Create("hidden-only");
        long onTrashed = repos.Tags.Create("trashed-only");
        long onArchived = repos.Tags.Create("archived");
        long onNothing = repos.Tags.Create("unused");
        long onBoth = repos.Tags.Create("both");
        repos.Tags.AddToNote(repos.AddNote(), onNormal);
        repos.Tags.AddToNote(repos.AddNote(n => n with { IsLocked = true }), onLocked);
        repos.Tags.AddToNote(repos.AddNote(n => n with { IsHidden = true }), onHidden);
        repos.Tags.AddToNote(repos.AddNote(n => n with { IsTrashed = true }), onTrashed);
        repos.Tags.AddToNote(repos.AddNote(n => n with { IsArchived = true }), onArchived);
        repos.Tags.AddToNote(repos.AddNote(n => n with { IsLocked = true }), onBoth);
        repos.Tags.AddToNote(repos.AddNote(), onBoth);

        var visible = repos.Tags.GetVisible().Select(t => t.Name).ToList();

        CollectionAssert.AreEqual(new[] { "archived", "both", "normal" }, visible);
        Assert.AreEqual(7, repos.Tags.GetAll().Count);
        Assert.AreNotEqual(0, onNothing);
    }

    [TestMethod]
    public void Rename_ChangesTheNameButNotToAnExistingOne()
    {
        using var repos = new Repos();
        long a = repos.Tags.Create("a");
        repos.Tags.Create("b");

        Assert.IsTrue(repos.Tags.Rename(a, "alpha"));
        Assert.AreEqual("alpha", repos.Tags.GetById(a)!.Name);
        Assert.IsFalse(repos.Tags.Rename(a, "b"));
        Assert.AreEqual("alpha", repos.Tags.GetById(a)!.Name);
        Assert.IsTrue(repos.Tags.Rename(a, "alpha"), "the same name again is fine");
        Assert.IsFalse(repos.Tags.Rename(999, "x"));
    }

    [TestMethod]
    public void DeletingATag_KeepsTheNotes()
    {
        using var repos = new Repos();
        long note = repos.AddNote();
        long tag = repos.Tags.Create("t");
        repos.Tags.AddToNote(note, tag);

        Assert.IsTrue(repos.Tags.Delete(tag));

        Assert.AreEqual(0, repos.Notes.GetById(note)!.Tags.Count);
        Assert.IsNotNull(repos.Notes.GetById(note));
    }

    [TestMethod]
    public void AddToNote_TwiceIsHarmlessAndAMissingNoteOrTagThrows()
    {
        using var repos = new Repos();
        long note = repos.AddNote();
        long tag = repos.Tags.Create("t");

        repos.Tags.AddToNote(note, tag);
        repos.Tags.AddToNote(note, tag);

        Assert.AreEqual(1, repos.Tags.GetForNote(note).Count);
        Assert.ThrowsExactly<SqliteException>(() => repos.Tags.AddToNote(999, tag));
        Assert.ThrowsExactly<SqliteException>(() => repos.Tags.AddToNote(note, 999));
    }

    [TestMethod]
    public void RemoveFromNote_AndClearForNote()
    {
        using var repos = new Repos();
        long note = repos.AddNote();
        long a = repos.Tags.Create("a");
        long b = repos.Tags.Create("b");
        repos.Tags.AddToNote(note, a);
        repos.Tags.AddToNote(note, b);

        repos.Tags.RemoveFromNote(note, a);
        CollectionAssert.AreEqual(new[] { "b" }, repos.Tags.GetForNote(note).Select(t => t.Name).ToList());

        repos.Tags.ClearForNote(note);
        Assert.AreEqual(0, repos.Tags.GetForNote(note).Count);
        Assert.AreEqual(2, repos.Tags.GetAll().Count);
    }
}

[TestClass]
public sealed class ChecklistRepositoryTests
{
    [TestMethod]
    public void ReplaceItems_StoresTheGivenOrderAndStateAndDropsTheOldItems()
    {
        using var repos = new Repos();
        long note = repos.AddNote();

        repos.Checklists.ReplaceItems(note, [new ChecklistItem(0, note, "old")]);
        repos.Checklists.ReplaceItems(note,
        [
            new ChecklistItem(0, note, "b", true, 7),
            new ChecklistItem(0, note, "a", false, 3),
        ]);

        var items = repos.Checklists.GetItems(note);
        CollectionAssert.AreEqual(new[] { "b", "a" }, items.Select(i => i.Text).ToList());
        CollectionAssert.AreEqual(new[] { 0, 1 }, items.Select(i => i.Position).ToList());
        CollectionAssert.AreEqual(new[] { true, false }, items.Select(i => i.IsChecked).ToList());
    }

    [TestMethod]
    public void ReplaceItems_WithNoItems_ClearsTheChecklist()
    {
        using var repos = new Repos();
        long note = repos.AddNote();
        repos.Checklists.ReplaceItems(note, [new ChecklistItem(0, note, "x")]);

        repos.Checklists.ReplaceItems(note, []);

        Assert.AreEqual(0, repos.Checklists.GetItems(note).Count);
    }

    [TestMethod]
    public void ReplaceItems_ForAMissingNote_ThrowsAndChangesNothing()
    {
        using var repos = new Repos();
        long note = repos.AddNote();
        repos.Checklists.ReplaceItems(note, [new ChecklistItem(0, note, "keep")]);

        Assert.ThrowsExactly<SqliteException>(() => repos.Checklists.ReplaceItems(999, [new ChecklistItem(0, 999, "x")]));

        Assert.AreEqual(1, repos.Checklists.GetItems(note).Count);
    }
}

[TestClass]
public sealed class StoredJsonTests
{
    [TestMethod]
    public void Spans_AreWrittenInTheAndroidFormat()
    {
        string json = StoredJson.SpansToJson([new StyleSpan(0, 5, TextStyleKind.Bold), new StyleSpan(2, 4, TextStyleKind.Strikethrough)]);

        Assert.AreEqual("[{\"start\":0,\"end\":5,\"kind\":\"BOLD\"},{\"start\":2,\"end\":4,\"kind\":\"STRIKETHROUGH\"}]", json);
        Assert.AreEqual("[]", StoredJson.SpansToJson([]));
    }

    [TestMethod]
    public void Spans_WrittenByAndroidCanBeRead()
    {
        var spans = StoredJson.SpansFromJson("[{\"start\":1,\"end\":3,\"kind\":\"UNDERLINE\"},{\"start\":0,\"end\":2,\"kind\":\"ITALIC\"}]");

        CollectionAssert.AreEqual(
            new[] { new StyleSpan(1, 3, TextStyleKind.Underline), new StyleSpan(0, 2, TextStyleKind.Italic) }, spans.ToList());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("not json")]
    [DataRow("{}")]
    [DataRow("[{\"start\":1}]")]
    [DataRow("[{\"start\":1,\"end\":2,\"kind\":\"SPARKLY\"}]")]
    [DataRow("[{\"start\":\"x\",\"end\":2,\"kind\":\"BOLD\"}]")]
    public void UnreadableSpans_GiveAnEmptyList(string? json)
    {
        Assert.AreEqual(0, StoredJson.SpansFromJson(json).Count);
    }

    [TestMethod]
    public void MissingSpansColumn_GivesAnEmptyList()
    {
        Assert.AreEqual(0, StoredJson.SpansFromJson(null).Count);
        Assert.AreEqual(0, StoredJson.ParagraphsFromJson(null).Count);
    }

    [TestMethod]
    public void Paragraphs_RoundTrip()
    {
        var paragraphs = new[] { new ParagraphFormat(0, ParagraphKind.Heading), new ParagraphFormat(3, ParagraphKind.Numbered) };

        string json = StoredJson.ParagraphsToJson(paragraphs);

        Assert.AreEqual("[{\"line\":0,\"kind\":\"HEADING\"},{\"line\":3,\"kind\":\"NUMBERED\"}]", json);
        CollectionAssert.AreEqual(paragraphs, StoredJson.ParagraphsFromJson(json).ToList());
        Assert.AreEqual(0, StoredJson.ParagraphsFromJson("garbage").Count);
    }

    [TestMethod]
    public void EnumNames_AreStoredInCapitalsAndReadBackAnyCase()
    {
        Assert.AreEqual("PURPLE", StoredJson.ToStored(NoteColorTag.Purple));
        Assert.AreEqual(NoteColorTag.Purple, StoredJson.EnumFromStored<NoteColorTag>("PURPLE"));
        Assert.AreEqual(NoteColorTag.Default, StoredJson.EnumFromStored<NoteColorTag>("default"));
        Assert.IsNull(StoredJson.EnumFromStored<NoteColorTag>("PINK"));
        Assert.IsNull(StoredJson.EnumFromStored<NoteColorTag>(null));
        Assert.IsNull(StoredJson.EnumFromStored<NoteColorTag>("42"));
    }
}
