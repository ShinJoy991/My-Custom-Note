using Microsoft.VisualStudio.TestTools.UnitTesting;
using MyCustomNote.Core.Formatting;

namespace MyCustomNote.Core.Tests.Formatting;

[TestClass]
public class RenderersTests
{
    private static StyleSpan Span(int start, int end, TextStyleKind kind) => new(start, end, kind);

    private static ParagraphFormat Para(int line, ParagraphKind kind) => new(line, kind);

    // ---------- NoteLayout ----------

    [TestMethod]
    public void Layout_SplitsRunsAtSpanEdges()
    {
        var lines = NoteLayout.Build("abc def", [Span(0, 3, TextStyleKind.Bold)]);
        Assert.AreEqual(1, lines.Count);
        CollectionAssert.AreEqual(
            new[] { new StyledRun("abc", TextStyles.Bold), new StyledRun(" def", TextStyles.None) },
            lines[0].Runs.ToArray());
    }

    [TestMethod]
    public void Layout_OverlappingSpans_CombineTheirStyles()
    {
        var lines = NoteLayout.Build("abcdef", [Span(0, 4, TextStyleKind.Bold), Span(2, 6, TextStyleKind.Italic)]);
        CollectionAssert.AreEqual(
            new[]
            {
                new StyledRun("ab", TextStyles.Bold),
                new StyledRun("cd", TextStyles.Bold | TextStyles.Italic),
                new StyledRun("ef", TextStyles.Italic)
            },
            lines[0].Runs.ToArray());
    }

    [TestMethod]
    public void Layout_KindsAndNumbersFollowTheParagraphFormats()
    {
        var lines = NoteLayout.Build("a\nb\nc", [], [Para(0, ParagraphKind.Numbered), Para(1, ParagraphKind.Numbered)]);
        Assert.AreEqual(3, lines.Count);
        Assert.AreEqual((ParagraphKind.Numbered, 1), (lines[0].Kind, lines[0].Number));
        Assert.AreEqual((ParagraphKind.Numbered, 2), (lines[1].Kind, lines[1].Number));
        Assert.AreEqual((ParagraphKind.Normal, 0), (lines[2].Kind, lines[2].Number));
    }

    [TestMethod]
    public void Layout_EmptyLineHasNoRuns()
    {
        var lines = NoteLayout.Build("a\n\nb", []);
        Assert.AreEqual(3, lines.Count);
        Assert.AreEqual(0, lines[1].Runs.Count);
    }

    [TestMethod]
    public void Layout_SpanAcrossLines_IsSplitPerLine()
    {
        var lines = NoteLayout.Build("ab\ncd", [Span(0, 5, TextStyleKind.Bold)]);
        CollectionAssert.AreEqual(new[] { new StyledRun("ab", TextStyles.Bold) }, lines[0].Runs.ToArray());
        CollectionAssert.AreEqual(new[] { new StyledRun("cd", TextStyles.Bold) }, lines[1].Runs.ToArray());
    }

    [TestMethod]
    public void Layout_EmptyText_IsOneEmptyLine()
    {
        var lines = NoteLayout.Build("", []);
        Assert.AreEqual(1, lines.Count);
        Assert.AreEqual(0, lines[0].Runs.Count);
    }

    // ---------- plain text ----------

    [TestMethod]
    public void PlainText_AddsPrefixesForParagraphKinds()
    {
        string result = PlainTextRenderer.Render(
            "t\na\nb\nc", [Span(0, 1, TextStyleKind.Bold)],
            [Para(0, ParagraphKind.Heading), Para(1, ParagraphKind.Bullet), Para(2, ParagraphKind.Numbered), Para(3, ParagraphKind.Numbered)]);
        Assert.AreEqual("# t\n- a\n1. b\n2. c", result);
    }

    // ---------- Markdown ----------

    [TestMethod]
    public void Markdown_WritesEachStyle()
    {
        string result = MarkdownRenderer.Render(
            "ab cd ef gh",
            [Span(0, 2, TextStyleKind.Bold), Span(3, 5, TextStyleKind.Italic), Span(6, 8, TextStyleKind.Underline), Span(9, 11, TextStyleKind.Strikethrough)]);
        Assert.AreEqual("**ab** *cd* <u>ef</u> ~~gh~~", result);
    }

    [TestMethod]
    public void Markdown_BoldAndItalicOnTheSameText()
    {
        Assert.AreEqual("***ab***", MarkdownRenderer.Render("ab", [Span(0, 2, TextStyleKind.Bold), Span(0, 2, TextStyleKind.Italic)]));
    }

    [TestMethod]
    public void Markdown_MovesSpacesOutsideTheMarkers()
    {
        Assert.AreEqual("a **b** c", MarkdownRenderer.Render("a b c", [Span(1, 4, TextStyleKind.Bold)]));
    }

    [TestMethod]
    public void Markdown_EscapesCharactersThatWouldBeFormatting()
    {
        Assert.AreEqual("a\\*b\\_c", MarkdownRenderer.Render("a*b_c", []));
        Assert.AreEqual("x\\\\y", MarkdownRenderer.Render("x\\y", []));
        Assert.AreEqual("\\<b\\> \\& \\[x\\]", MarkdownRenderer.Render("<b> & [x]", []));
    }

    [TestMethod]
    public void Markdown_EscapesNormalLinesThatLookLikeHeadingsOrLists()
    {
        string result = MarkdownRenderer.Render("# not heading\n- not list\n1. not numbered", []);
        Assert.AreEqual("\\# not heading  \n\\- not list  \n1\\. not numbered", result);
    }

    [TestMethod]
    public void Markdown_AddsPrefixesForParagraphKinds()
    {
        string result = MarkdownRenderer.Render(
            "t\na\nb", [], [Para(0, ParagraphKind.Heading), Para(1, ParagraphKind.Bullet), Para(2, ParagraphKind.Numbered)]);
        Assert.AreEqual("# t\n- a\n1. b", result);
    }

    [TestMethod]
    public void Markdown_HardBreakOnlyBetweenTwoNonEmptyNormalLines()
    {
        Assert.AreEqual("a  \nb\n\nc", MarkdownRenderer.Render("a\nb\n\nc", []));
    }

    [TestMethod]
    public void Markdown_StyleInsideAListItem()
    {
        Assert.AreEqual("- **a**b", MarkdownRenderer.Render("ab", [Span(0, 1, TextStyleKind.Bold)], [Para(0, ParagraphKind.Bullet)]));
    }

    // ---------- HTML ----------

    [TestMethod]
    public void Html_EscapesTextAndWrapsStyles()
    {
        string result = HtmlRenderer.Render("a<b & \"c\"", [Span(0, 10, TextStyleKind.Bold)]);
        Assert.AreEqual("<p><strong>a&lt;b &amp; &quot;c&quot;</strong></p>\n", result);
    }

    [TestMethod]
    public void Html_NestsAllFourStylesInAFixedOrder()
    {
        string result = HtmlRenderer.Render(
            "ab",
            [Span(0, 2, TextStyleKind.Bold), Span(0, 2, TextStyleKind.Italic), Span(0, 2, TextStyleKind.Underline), Span(0, 2, TextStyleKind.Strikethrough)]);
        Assert.AreEqual("<p><strong><em><u><s>ab</s></u></em></strong></p>\n", result);
    }

    [TestMethod]
    public void Html_GroupsConsecutiveListLines()
    {
        string result = HtmlRenderer.Render(
            "a\nb\nc\nd", [],
            [Para(0, ParagraphKind.Numbered), Para(1, ParagraphKind.Numbered), Para(3, ParagraphKind.Bullet)]);
        Assert.AreEqual("<ol>\n<li>a</li>\n<li>b</li>\n</ol>\n<p>c</p>\n<ul>\n<li>d</li>\n</ul>\n", result);
    }

    [TestMethod]
    public void Html_EmptyLineAndHeading()
    {
        string result = HtmlRenderer.Render("x\n\ny", [], [Para(0, ParagraphKind.Heading)]);
        Assert.AreEqual("<h2>x</h2>\n<p><br></p>\n<p>y</p>\n", result);
    }

    [TestMethod]
    public void Html_DocumentHasEscapedTitleAndBody()
    {
        var lines = NoteLayout.Build("body", []);
        string result = HtmlRenderer.RenderDocument("Ti<tle", lines);
        StringAssert.Contains(result, "<title>Ti&lt;tle</title>");
        StringAssert.Contains(result, "<h1 class=\"note-title\">Ti&lt;tle</h1>");
        StringAssert.Contains(result, "<p>body</p>");
    }
}
