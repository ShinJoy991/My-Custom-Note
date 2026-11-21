using Microsoft.VisualStudio.TestTools.UnitTesting;
using MyCustomNote.Core.Formatting;
using MyCustomNote.Core.IO;

namespace MyCustomNote.Core.Tests.Formatting;

[TestClass]
public class ListPrefixesTests
{
    private static StyleSpan Bold(int start, int end) => new(start, end, TextStyleKind.Bold);

    private static StyleSpan Italic(int start, int end) => new(start, end, TextStyleKind.Italic);

    private static ParagraphFormat Para(int line, ParagraphKind kind) => new(line, kind);

    // ---------- SplitLine ----------

    [TestMethod]
    public void SplitLine_RecognisesTheThreePrefixes()
    {
        Assert.AreEqual((ParagraphKind.Heading, 2), ListPrefixes.SplitLine("# a"));
        Assert.AreEqual((ParagraphKind.Bullet, 2), ListPrefixes.SplitLine("- a"));
        Assert.AreEqual((ParagraphKind.Bullet, 2), ListPrefixes.SplitLine("+ a"));
        Assert.AreEqual((ParagraphKind.Numbered, 3), ListPrefixes.SplitLine("1. a"));
        Assert.AreEqual((ParagraphKind.Numbered, 4), ListPrefixes.SplitLine("12. a"));
    }

    [TestMethod]
    public void SplitLine_AnItemWithNothingAfterThePrefixStillCounts()
    {
        Assert.AreEqual((ParagraphKind.Numbered, 3), ListPrefixes.SplitLine("1. "));
        Assert.AreEqual((ParagraphKind.Bullet, 2), ListPrefixes.SplitLine("- "));
        Assert.AreEqual((ParagraphKind.Heading, 2), ListPrefixes.SplitLine("# "));
    }

    [TestMethod]
    public void SplitLine_NeedsTheSpaceAndTheStartOfTheLine()
    {
        foreach (string line in new[] { "", "-a", "#a", "1.a", "1.", "1", "text - x", " - x", "x. y" })
        {
            Assert.AreEqual((ParagraphKind.Normal, 0), ListPrefixes.SplitLine(line), $"\"{line}\"");
        }
    }

    // ---------- Extract ----------

    [TestMethod]
    public void Extract_TurnsPrefixesIntoParagraphKinds()
    {
        var (body, spans, paragraphs) = ListPrefixes.Extract("# T\n- a\n1. b\n2. c\nplain", []);
        Assert.AreEqual("T\na\nb\nc\nplain", body);
        Assert.AreEqual(0, spans.Count);
        CollectionAssert.AreEqual(
            new[] { Para(0, ParagraphKind.Heading), Para(1, ParagraphKind.Bullet), Para(2, ParagraphKind.Numbered), Para(3, ParagraphKind.Numbered) },
            paragraphs.ToArray());
    }

    [TestMethod]
    public void Extract_MovesSpansBehindTheRemovedPrefix()
    {
        var (body, spans, _) = ListPrefixes.Extract("x\n3. dwadwdw", [Bold(5, 12)]);
        Assert.AreEqual("x\ndwadwdw", body);
        CollectionAssert.AreEqual(new[] { Bold(2, 9) }, spans.ToArray());
    }

    [TestMethod]
    public void Extract_ASpanThatCoveredThePrefixNowStartsAtTheText()
    {
        var (_, spans, _) = ListPrefixes.Extract("x\n3. dwadwdw", [Bold(2, 12)]);
        CollectionAssert.AreEqual(new[] { Bold(2, 9) }, spans.ToArray());
    }

    [TestMethod]
    public void Extract_ASpanOnlyOnThePrefixDisappears()
    {
        var (_, spans, _) = ListPrefixes.Extract("x\n3. dwadwdw", [Bold(2, 4)]);
        Assert.AreEqual(0, spans.Count);
    }

    [TestMethod]
    public void Extract_ASpanAcrossTwoLines_KeepsItsReach()
    {
        var (body, spans, _) = ListPrefixes.Extract("- a\n- b", [Bold(0, 7)]);
        Assert.AreEqual("a\nb", body);
        CollectionAssert.AreEqual(new[] { Bold(0, 3) }, spans.ToArray());
    }

    [TestMethod]
    public void Extract_TextWithoutPrefixes_IsUnchanged()
    {
        var (body, spans, paragraphs) = ListPrefixes.Extract("a\nb", [Bold(0, 3)]);
        Assert.AreEqual("a\nb", body);
        CollectionAssert.AreEqual(new[] { Bold(0, 3) }, spans.ToArray());
        Assert.AreEqual(0, paragraphs.Count);
    }

    [TestMethod]
    public void Extract_EmptyNumberedItems()
    {
        var (body, _, paragraphs) = ListPrefixes.Extract("1. \n2. x", []);
        Assert.AreEqual("\nx", body);
        CollectionAssert.AreEqual(new[] { Para(0, ParagraphKind.Numbered), Para(1, ParagraphKind.Numbered) }, paragraphs.ToArray());
    }

    [TestMethod]
    public void Extract_TheNoteTheUserSentFromTheirPhone_ComesBackAsTheWindowsNote()
    {
        // The Windows note "312on" (body below) after the phone app turned its lists into text prefixes.
        const string phone =
            "# 2312 kd sssjffff daw\n- dwadwa\n- \n- \n1. \n2. \n3. dwadwdw\ndwad daw\nda\n1. \n2. caece\n- dw\n- dwadwa\ndaw \nfwhhd\n dadwad\n- awdawdawdwadwdwad\n1. dadaw";
        const string windows =
            "2312 kd sssjffff daw\ndwadwa\n\n\n\n\ndwadwdw\ndwad daw\nda\n\ncaece\ndw\ndwadwa\ndaw \nfwhhd\n dadwad\nawdawdawdwadwdwad\ndadaw";

        var (body, spans, paragraphs) = ListPrefixes.Extract(phone, [Bold(13, 22), Bold(49, 56), Bold(57, 62), Italic(62, 65)]);

        Assert.AreEqual(windows, body);
        CollectionAssert.AreEqual(new[] { Bold(11, 20), Bold(32, 39), Bold(40, 45), Italic(45, 48) }, spans.ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                Para(0, ParagraphKind.Heading), Para(1, ParagraphKind.Bullet), Para(2, ParagraphKind.Bullet), Para(3, ParagraphKind.Bullet),
                Para(4, ParagraphKind.Numbered), Para(5, ParagraphKind.Numbered), Para(6, ParagraphKind.Numbered),
                Para(9, ParagraphKind.Numbered), Para(10, ParagraphKind.Numbered),
                Para(11, ParagraphKind.Bullet), Para(12, ParagraphKind.Bullet),
                Para(16, ParagraphKind.Bullet), Para(17, ParagraphKind.Numbered)
            },
            paragraphs.ToArray());
    }

    // ---------- NoteJson: which files get their prefixes read as lists ----------

    [TestMethod]
    public void NoteJson_WithoutTheParagraphsKey_IsAPhoneFile_AndItsPrefixesBecomeLists()
    {
        const string json = "{\"title\":\"t\",\"body\":\"- a\\n1. b\\nplain\",\"bodyStylesJson\":[{\"start\":2,\"end\":3,\"kind\":\"BOLD\"}]}";
        var bundle = NoteJson.Read(json, "fallback", 0);

        Assert.AreEqual("a\nb\nplain", bundle.Note.Body);
        CollectionAssert.AreEqual(new[] { Para(0, ParagraphKind.Bullet), Para(1, ParagraphKind.Numbered) }, bundle.Note.Paragraphs.ToArray());
        CollectionAssert.AreEqual(new[] { Bold(0, 1) }, bundle.Note.BodyStyles.ToArray());
    }

    [TestMethod]
    public void NoteJson_WithTheParagraphsKey_KeepsTheTextAsItIs()
    {
        // A Windows file: a line that starts with "- " there is text the user typed, not a list.
        const string json = "{\"title\":\"t\",\"body\":\"- a\\n1. b\",\"paragraphs\":[]}";
        var bundle = NoteJson.Read(json, "fallback", 0);

        Assert.AreEqual("- a\n1. b", bundle.Note.Body);
        Assert.AreEqual(0, bundle.Note.Paragraphs.Count);
    }
}
