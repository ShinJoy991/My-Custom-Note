using Microsoft.VisualStudio.TestTools.UnitTesting;
using MyCustomNote.Core.Formatting;

namespace MyCustomNote.Core.Tests.Formatting;

[TestClass]
public class StyleSpansTests
{
    // "a" + one emoji (two UTF-16 chars: index 1 high, index 2 low) + "b" => length 4
    private const string WithEmoji = "a\U0001F600b";

    private static StyleSpan Bold(int start, int end) => new(start, end, TextStyleKind.Bold);

    private static StyleSpan Italic(int start, int end) => new(start, end, TextStyleKind.Italic);

    private static void AssertSpans(IReadOnlyList<StyleSpan> actual, params StyleSpan[] expected) =>
        CollectionAssert.AreEqual(expected, actual.ToArray());

    // ---------- Normalize ----------

    [TestMethod]
    public void Normalize_EmptyText_DropsEverything()
    {
        AssertSpans(StyleSpans.Normalize([Bold(0, 3)], ""));
    }

    [TestMethod]
    public void Normalize_ClampsEndToTextLength()
    {
        AssertSpans(StyleSpans.Normalize([Bold(2, 99)], "hello"), Bold(2, 5));
    }

    [TestMethod]
    public void Normalize_ClampsNegativeStartToZero()
    {
        AssertSpans(StyleSpans.Normalize([Bold(-3, 2)], "hello"), Bold(0, 2));
    }

    [TestMethod]
    public void Normalize_DropsEmptyAndReversedSpans()
    {
        AssertSpans(StyleSpans.Normalize([Bold(3, 3), Bold(4, 2)], "hello world"));
    }

    [TestMethod]
    public void Normalize_MergesOverlappingSpansOfSameKind()
    {
        AssertSpans(StyleSpans.Normalize([Bold(0, 5), Bold(3, 8)], "0123456789"), Bold(0, 8));
    }

    [TestMethod]
    public void Normalize_MergesTouchingSpansOfSameKind()
    {
        AssertSpans(StyleSpans.Normalize([Bold(0, 3), Bold(3, 6)], "0123456789"), Bold(0, 6));
    }

    [TestMethod]
    public void Normalize_KeepsDifferentKindsSeparate()
    {
        AssertSpans(StyleSpans.Normalize([Italic(2, 7), Bold(0, 5)], "0123456789"), Bold(0, 5), Italic(2, 7));
    }

    [TestMethod]
    public void Normalize_WidensEdgesThatSplitAnEmoji()
    {
        AssertSpans(StyleSpans.Normalize([Bold(2, 3)], WithEmoji), Bold(1, 3));
        AssertSpans(StyleSpans.Normalize([Bold(0, 2)], WithEmoji), Bold(0, 3));
    }

    [TestMethod]
    public void Normalize_EmptySpanInsideEmoji_IsDroppedNotGrown()
    {
        AssertSpans(StyleSpans.Normalize([Bold(2, 2)], WithEmoji));
    }

    // ---------- Toggle ----------

    [TestMethod]
    public void Toggle_EmptyText_ChangesNothing()
    {
        AssertSpans(StyleSpans.Toggle([], "", 0, 0, TextStyleKind.Bold));
    }

    [TestMethod]
    public void Toggle_CollapsedSelection_ChangesNothing()
    {
        AssertSpans(StyleSpans.Toggle([], "hello", 2, 2, TextStyleKind.Bold));
    }

    [TestMethod]
    public void Toggle_AddsStyleOverSelection()
    {
        AssertSpans(StyleSpans.Toggle([], "hello world", 0, 5, TextStyleKind.Bold), Bold(0, 5));
    }

    [TestMethod]
    public void Toggle_WholeText()
    {
        AssertSpans(StyleSpans.Toggle([], "hello", 0, 5, TextStyleKind.Bold), Bold(0, 5));
    }

    [TestMethod]
    public void Toggle_ReversedSelection_IsOrdered()
    {
        AssertSpans(StyleSpans.Toggle([], "hello", 5, 0, TextStyleKind.Bold), Bold(0, 5));
    }

    [TestMethod]
    public void Toggle_OutOfRangeSelection_IsClamped()
    {
        AssertSpans(StyleSpans.Toggle([], "hello", -4, 99, TextStyleKind.Bold), Bold(0, 5));
    }

    [TestMethod]
    public void Toggle_RemovesStyleWhenSelectionFullyCovered()
    {
        AssertSpans(StyleSpans.Toggle([Bold(0, 5)], "hello world", 0, 5, TextStyleKind.Bold));
    }

    [TestMethod]
    public void Toggle_RemovingFromTheMiddle_SplitsTheSpan()
    {
        AssertSpans(
            StyleSpans.Toggle([Bold(0, 10)], "0123456789", 3, 6, TextStyleKind.Bold),
            Bold(0, 3), Bold(6, 10));
    }

    [TestMethod]
    public void Toggle_PartialOverlap_AddsInsteadOfRemoving()
    {
        AssertSpans(StyleSpans.Toggle([Bold(3, 8)], "hello world", 0, 5, TextStyleKind.Bold), Bold(0, 8));
    }

    [TestMethod]
    public void Toggle_NextToExistingSpan_MergesThem()
    {
        AssertSpans(StyleSpans.Toggle([Bold(0, 3)], "hello world", 3, 6, TextStyleKind.Bold), Bold(0, 6));
    }

    [TestMethod]
    public void Toggle_LeavesOtherKindsAlone()
    {
        AssertSpans(
            StyleSpans.Toggle([Italic(0, 5)], "hello world", 0, 5, TextStyleKind.Bold),
            Bold(0, 5), Italic(0, 5));
    }

    [TestMethod]
    public void Toggle_ThreeOperationsInARow_StayConsistent()
    {
        // Android regression: a second or third formatting action in one session corrupted the display.
        const string text = "hello world";

        var spans = StyleSpans.Toggle([], text, 0, 5, TextStyleKind.Bold);
        AssertSpans(spans, Bold(0, 5));

        spans = StyleSpans.Toggle(spans, text, 2, 8, TextStyleKind.Italic);
        AssertSpans(spans, Bold(0, 5), Italic(2, 8));

        spans = StyleSpans.Toggle(spans, text, 0, 5, TextStyleKind.Bold);
        AssertSpans(spans, Italic(2, 8));
    }

    [TestMethod]
    public void Toggle_TextMadeOfAsterisks_IsJustText()
    {
        // Android regression: the string "****" crashed the marker-based renderer.
        AssertSpans(StyleSpans.Toggle([], "****", 0, 4, TextStyleKind.Bold), Bold(0, 4));
    }

    [TestMethod]
    public void Toggle_SelectionInsideEmoji_WidensToWholeEmoji()
    {
        AssertSpans(StyleSpans.Toggle([], WithEmoji, 2, 3, TextStyleKind.Bold), Bold(1, 3));
        AssertSpans(StyleSpans.Toggle([], WithEmoji, 1, 2, TextStyleKind.Bold), Bold(1, 3));
    }

    // ---------- Line breaks ----------

    [TestMethod]
    public void Normalize_SplitsSpansAtNewlines()
    {
        AssertSpans(StyleSpans.Normalize([Bold(0, 7)], "one\ntwo"), Bold(0, 3), Bold(4, 7));
    }

    [TestMethod]
    public void Normalize_DropsSpanMadeOnlyOfNewlines()
    {
        AssertSpans(StyleSpans.Normalize([Bold(1, 3)], "a\n\nb"));
    }

    [TestMethod]
    public void Normalize_KeepsSpansOnEachSideOfANewlineSeparate()
    {
        AssertSpans(StyleSpans.Normalize([Bold(0, 2), Bold(3, 5)], "ab\ncd"), Bold(0, 2), Bold(3, 5));
    }

    [TestMethod]
    public void Toggle_AcrossLines_AddsOneSpanPerLine()
    {
        AssertSpans(StyleSpans.Toggle([], "ab\ncd", 0, 5, TextStyleKind.Bold), Bold(0, 2), Bold(3, 5));
    }

    [TestMethod]
    public void Toggle_AcrossLines_SecondToggleRemovesIt()
    {
        var spans = StyleSpans.Toggle([], "ab\ncd", 0, 5, TextStyleKind.Bold);
        AssertSpans(StyleSpans.Toggle(spans, "ab\ncd", 0, 5, TextStyleKind.Bold));
    }

    [TestMethod]
    public void Toggle_OnlyANewlineSelected_ChangesNothing()
    {
        AssertSpans(StyleSpans.Toggle([], "a\nb", 1, 2, TextStyleKind.Bold));
    }

    [TestMethod]
    public void IsStyled_IgnoresNewlines()
    {
        Assert.IsTrue(StyleSpans.IsStyled([Bold(0, 2), Bold(3, 5)], "ab\ncd", 0, 5, TextStyleKind.Bold));
        Assert.IsFalse(StyleSpans.IsStyled([Bold(0, 2)], "ab\ncd", 0, 5, TextStyleKind.Bold));
        Assert.IsFalse(StyleSpans.IsStyled([], "a\nb", 1, 2, TextStyleKind.Bold));
    }

    // ---------- IsFullyCovered ----------

    [TestMethod]
    public void IsFullyCovered_TouchingSpansCoverTheRange()
    {
        Assert.IsTrue(StyleSpans.IsFullyCovered([Bold(0, 3), Bold(3, 6)], 1, 5, TextStyleKind.Bold));
    }

    [TestMethod]
    public void IsFullyCovered_GapMeansNotCovered()
    {
        Assert.IsFalse(StyleSpans.IsFullyCovered([Bold(0, 3), Bold(4, 6)], 1, 5, TextStyleKind.Bold));
    }

    [TestMethod]
    public void IsFullyCovered_EmptyRange_IsNotCovered()
    {
        Assert.IsFalse(StyleSpans.IsFullyCovered([Bold(0, 10)], 4, 4, TextStyleKind.Bold));
    }

    [TestMethod]
    public void IsFullyCovered_OtherKindDoesNotCount()
    {
        Assert.IsFalse(StyleSpans.IsFullyCovered([Italic(0, 10)], 2, 5, TextStyleKind.Bold));
    }

    // ---------- ApplyEdit ----------

    [TestMethod]
    public void ApplyEdit_InsertBeforeSpan_ShiftsIt()
    {
        AssertSpans(StyleSpans.ApplyEdit([Bold(5, 8)], 0, 0, 2), Bold(7, 10));
    }

    [TestMethod]
    public void ApplyEdit_InsertAtSpanStart_ShiftsItSoNewTextIsPlain()
    {
        AssertSpans(StyleSpans.ApplyEdit([Bold(5, 8)], 5, 0, 3), Bold(8, 11));
    }

    [TestMethod]
    public void ApplyEdit_InsertInsideSpan_ExtendsIt()
    {
        AssertSpans(StyleSpans.ApplyEdit([Bold(2, 6)], 4, 0, 2), Bold(2, 8));
    }

    [TestMethod]
    public void ApplyEdit_InsertAtSpanEnd_ExtendsIt()
    {
        AssertSpans(StyleSpans.ApplyEdit([Bold(2, 6)], 6, 0, 1), Bold(2, 7));
    }

    [TestMethod]
    public void ApplyEdit_InsertAfterSpan_LeavesItAlone()
    {
        AssertSpans(StyleSpans.ApplyEdit([Bold(2, 6)], 7, 0, 1), Bold(2, 6));
    }

    [TestMethod]
    public void ApplyEdit_DeleteBeforeSpan_ShiftsItLeft()
    {
        AssertSpans(StyleSpans.ApplyEdit([Bold(5, 8)], 1, 2, 0), Bold(3, 6));
    }

    [TestMethod]
    public void ApplyEdit_DeleteInsideSpan_ShrinksIt()
    {
        AssertSpans(StyleSpans.ApplyEdit([Bold(2, 10)], 4, 3, 0), Bold(2, 7));
    }

    [TestMethod]
    public void ApplyEdit_DeleteAcrossSpanStart_TrimsTheFront()
    {
        AssertSpans(StyleSpans.ApplyEdit([Bold(5, 10)], 3, 4, 0), Bold(3, 6));
    }

    [TestMethod]
    public void ApplyEdit_DeleteAcrossSpanEnd_TrimsTheBack()
    {
        AssertSpans(StyleSpans.ApplyEdit([Bold(2, 6)], 4, 4, 0), Bold(2, 4));
    }

    [TestMethod]
    public void ApplyEdit_DeleteWholeSpan_RemovesIt()
    {
        AssertSpans(StyleSpans.ApplyEdit([Bold(2, 5)], 1, 6, 0));
    }

    [TestMethod]
    public void ApplyEdit_ReplaceInsideSpan_KeepsTheStyle()
    {
        // "hello" bold, replace "ell" with "a" => "hao", still all bold
        AssertSpans(StyleSpans.ApplyEdit([Bold(0, 5)], 1, 3, 1), Bold(0, 3));
    }

    [TestMethod]
    public void ApplyEdit_NegativeArguments_Throw()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => StyleSpans.ApplyEdit([], -1, 0, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => StyleSpans.ApplyEdit([], 0, -1, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => StyleSpans.ApplyEdit([], 0, 0, -1));
    }
}
