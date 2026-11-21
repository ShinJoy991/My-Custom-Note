using Microsoft.VisualStudio.TestTools.UnitTesting;
using MyCustomNote.Core.Formatting;

namespace MyCustomNote.Core.Tests.Formatting;

[TestClass]
public class ParagraphFormatsTests
{
    private static ParagraphFormat F(int line, ParagraphKind kind) => new(line, kind);

    private static void AssertFormats(IReadOnlyList<ParagraphFormat> actual, params ParagraphFormat[] expected) =>
        CollectionAssert.AreEqual(expected, actual.ToArray());

    // ---------- LineCount / LineOfOffset / LinesTouched ----------

    [TestMethod]
    public void LineCount_CountsNewlinesPlusOne()
    {
        Assert.AreEqual(1, ParagraphFormats.LineCount(""));
        Assert.AreEqual(1, ParagraphFormats.LineCount("a"));
        Assert.AreEqual(2, ParagraphFormats.LineCount("a\nb"));
        Assert.AreEqual(2, ParagraphFormats.LineCount("a\n"));
        Assert.AreEqual(3, ParagraphFormats.LineCount("\n\n"));
    }

    [TestMethod]
    public void LineOfOffset_BeforeAndAfterTheLineBreak()
    {
        const string text = "ab\ncd";
        Assert.AreEqual(0, ParagraphFormats.LineOfOffset(text, 0));
        Assert.AreEqual(0, ParagraphFormats.LineOfOffset(text, 2)); // right before the '\n'
        Assert.AreEqual(1, ParagraphFormats.LineOfOffset(text, 3)); // right after it
        Assert.AreEqual(1, ParagraphFormats.LineOfOffset(text, 5));
    }

    [TestMethod]
    public void LineOfOffset_ClampsOutOfRangeOffsets()
    {
        Assert.AreEqual(0, ParagraphFormats.LineOfOffset("ab\ncd", -5));
        Assert.AreEqual(1, ParagraphFormats.LineOfOffset("ab\ncd", 99));
    }

    [TestMethod]
    public void LinesTouched_CollapsedCaret_IsItsLine()
    {
        Assert.AreEqual((0, 0), ParagraphFormats.LinesTouched("ab\ncd", 1, 1));
        Assert.AreEqual((1, 1), ParagraphFormats.LinesTouched("ab\ncd", 4, 4));
        Assert.AreEqual((0, 0), ParagraphFormats.LinesTouched("", 0, 0));
    }

    [TestMethod]
    public void LinesTouched_SelectionAcrossLines()
    {
        Assert.AreEqual((0, 1), ParagraphFormats.LinesTouched("ab\ncd", 1, 4));
        Assert.AreEqual((1, 1), ParagraphFormats.LinesTouched("ab\ncd", 3, 5));
    }

    [TestMethod]
    public void LinesTouched_ReversedSelection_IsOrdered()
    {
        Assert.AreEqual((0, 1), ParagraphFormats.LinesTouched("ab\ncd", 4, 1));
    }

    [TestMethod]
    public void LinesTouched_SelectionEndingAfterALineBreak_DoesNotReachTheNextLine()
    {
        Assert.AreEqual((0, 0), ParagraphFormats.LinesTouched("ab\ncd", 0, 3));
    }

    // ---------- Normalize ----------

    [TestMethod]
    public void Normalize_DropsNormalEntriesAndMissingLines()
    {
        AssertFormats(
            ParagraphFormats.Normalize([F(0, ParagraphKind.Heading), F(1, ParagraphKind.Normal), F(5, ParagraphKind.Bullet), F(-1, ParagraphKind.Bullet)], "a\nb"),
            F(0, ParagraphKind.Heading));
    }

    [TestMethod]
    public void Normalize_LastEntryForALineWins()
    {
        AssertFormats(
            ParagraphFormats.Normalize([F(0, ParagraphKind.Bullet), F(0, ParagraphKind.Numbered)], "a"),
            F(0, ParagraphKind.Numbered));
    }

    [TestMethod]
    public void Normalize_SortsByLine()
    {
        AssertFormats(
            ParagraphFormats.Normalize([F(2, ParagraphKind.Bullet), F(0, ParagraphKind.Heading)], "a\nb\nc"),
            F(0, ParagraphKind.Heading), F(2, ParagraphKind.Bullet));
    }

    // ---------- Toggle ----------

    [TestMethod]
    public void Toggle_AppliesTheKindToEveryTouchedLine()
    {
        AssertFormats(
            ParagraphFormats.Toggle([], "ab\ncd\nef", 1, 4, ParagraphKind.Bullet),
            F(0, ParagraphKind.Bullet), F(1, ParagraphKind.Bullet));
    }

    [TestMethod]
    public void Toggle_SecondTime_RemovesIt()
    {
        var once = ParagraphFormats.Toggle([], "ab\ncd", 0, 5, ParagraphKind.Heading);
        AssertFormats(ParagraphFormats.Toggle(once, "ab\ncd", 0, 5, ParagraphKind.Heading));
    }

    [TestMethod]
    public void Toggle_MixedSelection_AppliesToAllInsteadOfRemoving()
    {
        AssertFormats(
            ParagraphFormats.Toggle([F(0, ParagraphKind.Bullet)], "ab\ncd", 0, 5, ParagraphKind.Bullet),
            F(0, ParagraphKind.Bullet), F(1, ParagraphKind.Bullet));
    }

    [TestMethod]
    public void Toggle_OtherKind_ReplacesTheOldOne()
    {
        AssertFormats(
            ParagraphFormats.Toggle([F(0, ParagraphKind.Bullet)], "ab", 0, 0, ParagraphKind.Numbered),
            F(0, ParagraphKind.Numbered));
    }

    [TestMethod]
    public void Toggle_SelectionEndingAfterALineBreak_LeavesTheNextLineAlone()
    {
        AssertFormats(
            ParagraphFormats.Toggle([], "ab\ncd", 0, 3, ParagraphKind.Heading),
            F(0, ParagraphKind.Heading));
    }

    [TestMethod]
    public void Toggle_CollapsedCaret_AppliesToItsLine()
    {
        AssertFormats(
            ParagraphFormats.Toggle([], "ab\ncd", 4, 4, ParagraphKind.Heading),
            F(1, ParagraphKind.Heading));
    }

    [TestMethod]
    public void Toggle_Normal_ClearsTheTouchedLines()
    {
        AssertFormats(
            ParagraphFormats.Toggle([F(0, ParagraphKind.Heading), F(1, ParagraphKind.Bullet)], "ab\ncd", 0, 5, ParagraphKind.Normal));
    }

    [TestMethod]
    public void Toggle_EmptyText_MakesTheEmptyLineAListItem()
    {
        AssertFormats(
            ParagraphFormats.Toggle([], "", 0, 0, ParagraphKind.Bullet),
            F(0, ParagraphKind.Bullet));
    }

    [TestMethod]
    public void Toggle_LeavesUntouchedLinesAlone()
    {
        AssertFormats(
            ParagraphFormats.Toggle([F(2, ParagraphKind.Heading)], "a\nb\nc", 0, 0, ParagraphKind.Bullet),
            F(0, ParagraphKind.Bullet), F(2, ParagraphKind.Heading));
    }

    // ---------- Numbering ----------

    [TestMethod]
    public void Numbering_CountsConsecutiveNumberedLines()
    {
        var numbers = ParagraphFormats.Numbering(
            [F(0, ParagraphKind.Numbered), F(1, ParagraphKind.Numbered), F(2, ParagraphKind.Bullet), F(3, ParagraphKind.Numbered), F(4, ParagraphKind.Numbered)],
            "a\nb\nc\nd\ne");
        CollectionAssert.AreEqual(new[] { 1, 2, 0, 1, 2 }, numbers.ToArray());
    }

    [TestMethod]
    public void Numbering_RestartsAfterANormalLine()
    {
        var numbers = ParagraphFormats.Numbering([F(0, ParagraphKind.Numbered), F(2, ParagraphKind.Numbered)], "a\nb\nc");
        CollectionAssert.AreEqual(new[] { 1, 0, 1 }, numbers.ToArray());
    }

    [TestMethod]
    public void Numbering_EmptyText_IsOneLine()
    {
        CollectionAssert.AreEqual(new[] { 1 }, ParagraphFormats.Numbering([F(0, ParagraphKind.Numbered)], "").ToArray());
        CollectionAssert.AreEqual(new[] { 0 }, ParagraphFormats.Numbering([], "").ToArray());
    }
}
