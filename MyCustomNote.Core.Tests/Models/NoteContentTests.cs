using Microsoft.VisualStudio.TestTools.UnitTesting;
using MyCustomNote.Core.Formatting;
using MyCustomNote.Core.Models;

namespace MyCustomNote.Core.Tests.Models;

[TestClass]
public class NoteContentTests
{
    private static NoteContent Content(
        string title = "t",
        StyleSpan[]? titleStyles = null,
        string body = "b",
        StyleSpan[]? bodyStyles = null,
        ParagraphFormat[]? paragraphs = null) =>
        new(title, titleStyles ?? [], body, bodyStyles ?? [], paragraphs ?? []);

    [TestMethod]
    public void SameContent_IsTheSame_EvenWithDifferentListObjects()
    {
        var a = Content(bodyStyles: [new StyleSpan(0, 1, TextStyleKind.Bold)], paragraphs: [new ParagraphFormat(0, ParagraphKind.Bullet)]);
        var b = Content(bodyStyles: [new StyleSpan(0, 1, TextStyleKind.Bold)], paragraphs: [new ParagraphFormat(0, ParagraphKind.Bullet)]);

        Assert.IsTrue(a.SameAs(b));
        Assert.IsTrue(b.SameAs(a));
        Assert.IsTrue(a.SameAs(a));
    }

    [TestMethod]
    public void AnyDifference_IsDetected()
    {
        var baseline = Content(
            titleStyles: [new StyleSpan(0, 1, TextStyleKind.Bold)],
            bodyStyles: [new StyleSpan(0, 1, TextStyleKind.Italic)],
            paragraphs: [new ParagraphFormat(0, ParagraphKind.Heading)]);

        Assert.IsFalse(baseline.SameAs(Content(title: "other", titleStyles: [new StyleSpan(0, 1, TextStyleKind.Bold)], bodyStyles: [new StyleSpan(0, 1, TextStyleKind.Italic)], paragraphs: [new ParagraphFormat(0, ParagraphKind.Heading)])), "title");
        Assert.IsFalse(baseline.SameAs(Content(titleStyles: [new StyleSpan(0, 1, TextStyleKind.Bold)], body: "other", bodyStyles: [new StyleSpan(0, 1, TextStyleKind.Italic)], paragraphs: [new ParagraphFormat(0, ParagraphKind.Heading)])), "body");
        Assert.IsFalse(baseline.SameAs(Content(bodyStyles: [new StyleSpan(0, 1, TextStyleKind.Italic)], paragraphs: [new ParagraphFormat(0, ParagraphKind.Heading)])), "title styles");
        Assert.IsFalse(baseline.SameAs(Content(titleStyles: [new StyleSpan(0, 1, TextStyleKind.Bold)], bodyStyles: [new StyleSpan(0, 1, TextStyleKind.Underline)], paragraphs: [new ParagraphFormat(0, ParagraphKind.Heading)])), "body styles");
        Assert.IsFalse(baseline.SameAs(Content(titleStyles: [new StyleSpan(0, 1, TextStyleKind.Bold)], bodyStyles: [new StyleSpan(0, 1, TextStyleKind.Italic)], paragraphs: [new ParagraphFormat(0, ParagraphKind.Bullet)])), "paragraph kind");
        Assert.IsFalse(baseline.SameAs(Content(titleStyles: [new StyleSpan(0, 1, TextStyleKind.Bold)], bodyStyles: [new StyleSpan(0, 1, TextStyleKind.Italic)])), "paragraphs");
        Assert.IsFalse(baseline.SameAs(null), "null");
    }

    [TestMethod]
    public void From_TakesOnlyTheContentOfANote()
    {
        var note = new Note
        {
            Id = 5, Title = "T", Body = "B", IsPinned = true, IsArchived = true,
            BodyStyles = [new StyleSpan(0, 1, TextStyleKind.Bold)],
            TitleStyles = [new StyleSpan(0, 1, TextStyleKind.Italic)],
            Paragraphs = [new ParagraphFormat(0, ParagraphKind.Numbered)],
        };

        var content = NoteContent.From(note);

        Assert.AreEqual("T", content.Title);
        Assert.AreEqual("B", content.Body);
        Assert.IsTrue(content.SameAs(new NoteContent("T", note.TitleStyles, "B", note.BodyStyles, note.Paragraphs)));
    }
}

[TestClass]
public class TextStatsTests
{
    [TestMethod]
    public void EmptyText_HasNoWordsAndNoCharacters()
    {
        Assert.AreEqual(new TextStats(0, 0), TextStats.Count(""));
    }

    [TestMethod]
    public void OnlyWhiteSpace_HasNoWords_ButSpacesAreCharacters()
    {
        Assert.AreEqual(new TextStats(0, 3), TextStats.Count("   "));
        Assert.AreEqual(new TextStats(0, 0), TextStats.Count("\n\n\r\n"), "line breaks are not counted");
    }

    [TestMethod]
    public void Words_AreSeparatedByAnyWhiteSpace()
    {
        Assert.AreEqual(1, TextStats.Count("hello").Words);
        Assert.AreEqual(2, TextStats.Count("hello world").Words);
        Assert.AreEqual(2, TextStats.Count("  hello \t world  ").Words);
        Assert.AreEqual(3, TextStats.Count("one\ntwo\r\nthree").Words);
        Assert.AreEqual(2, TextStats.Count("hello,world! again").Words, "punctuation does not split a word");
    }

    [TestMethod]
    public void Characters_IncludeSpacesButNotLineBreaks()
    {
        Assert.AreEqual(11, TextStats.Count("hello world").Characters);
        Assert.AreEqual(10, TextStats.Count("hello\nworld").Characters);
    }

    [TestMethod]
    public void AnEmoji_IsOneCharacterAndPartOfAWord()
    {
        var stats = TextStats.Count("hi \U0001F600 ok\U0001F600");

        Assert.AreEqual(3, stats.Words);
        Assert.AreEqual(8, stats.Characters);
    }

    [TestMethod]
    public void VietnameseText_IsCountedByLetters()
    {
        var stats = TextStats.Count("Xin ch\u00E0o Vi\u1EC7t Nam"); // precomposed letters

        Assert.AreEqual(4, stats.Words);
        Assert.AreEqual(17, stats.Characters);
    }
}
