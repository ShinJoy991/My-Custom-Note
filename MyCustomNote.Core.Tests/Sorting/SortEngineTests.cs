using Microsoft.VisualStudio.TestTools.UnitTesting;
using MyCustomNote.Core.Models;
using MyCustomNote.Core.Sorting;

namespace MyCustomNote.Core.Tests.Sorting;

[TestClass]
public class SortEngineTests
{
    private static Note N(long id, string title = "", long created = 0, long modified = 0, long? opened = null, long order = 0, bool pinned = false) =>
        new() { Id = id, Title = title, CreatedAt = created, ModifiedAt = modified, LastOpenedAt = opened, SortOrder = order, IsPinned = pinned };

    private static long[] Ids(IReadOnlyList<Note> notes) => notes.Select(n => n.Id).ToArray();

    [TestMethod]
    public void DateCreated_NewestFirst()
    {
        var sorted = SortEngine.Sort([N(1, created: 10), N(2, created: 30), N(3, created: 20)], SortOption.DateCreated);

        CollectionAssert.AreEqual(new long[] { 2, 3, 1 }, Ids(sorted));
    }

    [TestMethod]
    public void DateModified_NewestFirst()
    {
        var sorted = SortEngine.Sort([N(1, modified: 5), N(2, modified: 1), N(3, modified: 9)], SortOption.DateModified);

        CollectionAssert.AreEqual(new long[] { 3, 1, 2 }, Ids(sorted));
    }

    [TestMethod]
    public void Title_AToZIgnoringCase()
    {
        var sorted = SortEngine.Sort([N(1, "banana"), N(2, "Apple"), N(3, "cherry"), N(4, "APRICOT")], SortOption.Title);

        CollectionAssert.AreEqual(new long[] { 2, 4, 1, 3 }, Ids(sorted));
    }

    [TestMethod]
    public void Title_EmptyTitlesComeFirst()
    {
        var sorted = SortEngine.Sort([N(1, "a"), N(2, "")], SortOption.Title);

        CollectionAssert.AreEqual(new long[] { 2, 1 }, Ids(sorted));
    }

    [TestMethod]
    public void LastOpened_MostRecentFirst_NeverOpenedLast()
    {
        var sorted = SortEngine.Sort([N(1, opened: null), N(2, opened: 100), N(3, opened: 500), N(4, opened: null)], SortOption.LastOpened);

        CollectionAssert.AreEqual(new long[] { 3, 2, 1, 4 }, Ids(sorted));
    }

    [TestMethod]
    public void LastOpened_ATimeOfZeroIsStillBeforeNeverOpened()
    {
        var sorted = SortEngine.Sort([N(1, opened: null), N(2, opened: 0)], SortOption.LastOpened);

        CollectionAssert.AreEqual(new long[] { 2, 1 }, Ids(sorted));
    }

    [TestMethod]
    public void Manual_FollowsTheSortOrder()
    {
        var sorted = SortEngine.Sort([N(1, order: 3), N(2, order: 1), N(3, order: 2)], SortOption.Manual);

        CollectionAssert.AreEqual(new long[] { 2, 3, 1 }, Ids(sorted));
    }

    [TestMethod]
    [DataRow(SortOption.DateCreated)]
    [DataRow(SortOption.DateModified)]
    [DataRow(SortOption.Title)]
    [DataRow(SortOption.LastOpened)]
    [DataRow(SortOption.Manual)]
    public void PinnedNotes_AreFirstWhateverTheOption_KeepingTheOrderInsideEachGroup(SortOption option)
    {
        // Notes 1 and 2 are pinned and rank last under every option; 3 and 4 are not pinned.
        var notes = new[]
        {
            N(1, "z", created: 1, modified: 1, opened: 1, order: 9, pinned: true),
            N(2, "y", created: 2, modified: 2, opened: null, order: 10, pinned: true),
            N(3, "b", created: 100, modified: 100, opened: 100, order: 1),
            N(4, "a", created: 200, modified: 200, opened: 200, order: 2),
        };

        var ids = Ids(SortEngine.Sort(notes, option));

        Assert.IsTrue(ids.Take(2).All(id => id is 1 or 2), "pinned first");
        Assert.IsTrue(ids.Skip(2).All(id => id is 3 or 4), "then the others");
    }

    [TestMethod]
    public void PinnedGroup_IsSortedByTheOptionToo()
    {
        var sorted = SortEngine.Sort([N(1, created: 5), N(2, created: 50, pinned: true), N(3, created: 20, pinned: true), N(4, created: 99)], SortOption.DateCreated);

        CollectionAssert.AreEqual(new long[] { 2, 3, 4, 1 }, Ids(sorted));
    }

    [TestMethod]
    public void EqualNotes_KeepTheirIncomingOrder()
    {
        var sorted = SortEngine.Sort([N(1, "same"), N(2, "SAME"), N(3, "same")], SortOption.Title);

        CollectionAssert.AreEqual(new long[] { 1, 2, 3 }, Ids(sorted));
    }

    [TestMethod]
    public void TheInputIsNotChanged_AndAnEmptyListWorks()
    {
        var input = new List<Note> { N(1, order: 2), N(2, order: 1) };

        var sorted = SortEngine.Sort(input, SortOption.Manual);

        CollectionAssert.AreEqual(new long[] { 1, 2 }, Ids(input));
        CollectionAssert.AreEqual(new long[] { 2, 1 }, Ids(sorted));
        Assert.AreEqual(0, SortEngine.Sort([], SortOption.Title).Count);
    }
}
