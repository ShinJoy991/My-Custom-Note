using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MyCustomNote.Core.Formatting;
using MyCustomNote.Core.IO;
using MyCustomNote.Core.Models;

namespace MyCustomNote.Core.Tests.IO;

[TestClass]
public class NoteJsonTests
{
    private static NoteBundle FullBundle() => new(
        new Note
        {
            Title = "Ti\u1EBFng Vi\u1EC7t \"quoted\"",
            Body = "line one\nline \U0001F600 two",
            IsPinned = true,
            IsArchived = true,
            ColorTag = NoteColorTag.Purple,
            CreatedAt = 111,
            ModifiedAt = 222,
            TitleStyles = [new StyleSpan(0, 4, TextStyleKind.Bold)],
            BodyStyles = [new StyleSpan(0, 4, TextStyleKind.Italic), new StyleSpan(9, 12, TextStyleKind.Underline)],
            Paragraphs = [new ParagraphFormat(0, ParagraphKind.Heading), new ParagraphFormat(1, ParagraphKind.Bullet)],
        },
        "Work",
        ["alpha", "beta"],
        [new ChecklistItem(0, 0, "one", false, 0), new ChecklistItem(0, 0, "two", true, 1)]);

    private static void AssertSame(NoteBundle expected, NoteBundle actual)
    {
        Assert.AreEqual(expected.Note.Title, actual.Note.Title);
        Assert.AreEqual(expected.Note.Body, actual.Note.Body);
        Assert.AreEqual(expected.Note.IsPinned, actual.Note.IsPinned);
        Assert.AreEqual(expected.Note.IsArchived, actual.Note.IsArchived);
        Assert.AreEqual(expected.Note.ColorTag, actual.Note.ColorTag);
        Assert.AreEqual(expected.Note.CreatedAt, actual.Note.CreatedAt);
        Assert.AreEqual(expected.Note.ModifiedAt, actual.Note.ModifiedAt);
        CollectionAssert.AreEqual(expected.Note.TitleStyles.ToList(), actual.Note.TitleStyles.ToList());
        CollectionAssert.AreEqual(expected.Note.BodyStyles.ToList(), actual.Note.BodyStyles.ToList());
        CollectionAssert.AreEqual(expected.Note.Paragraphs.ToList(), actual.Note.Paragraphs.ToList());
        Assert.AreEqual(expected.FolderName, actual.FolderName);
        CollectionAssert.AreEqual(expected.Tags.ToList(), actual.Tags.ToList());
        CollectionAssert.AreEqual(
            expected.ChecklistItems.Select(i => (i.Text, i.IsChecked)).ToList(),
            actual.ChecklistItems.Select(i => (i.Text, i.IsChecked)).ToList());
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void WriteThenRead_KeepsEverything(bool indented)
    {
        var bundle = FullBundle();

        var read = NoteJson.Read(NoteJson.Write(bundle, indented), "fallback", 999);

        AssertSame(bundle, read);
    }

    [TestMethod]
    public void Write_UsesTheAndroidKeysAndVersionFour()
    {
        string json = NoteJson.Write(FullBundle());

        foreach (string key in new[] { "\"format\": \"MyCustomNote\"", "\"version\": 4", "\"title\"", "\"body\"", "\"createdAt\"", "\"modifiedAt\"", "\"isPinned\"", "\"isArchived\"", "\"colorTag\": \"PURPLE\"", "\"folderName\": \"Work\"", "\"tags\"", "\"checklistItems\"", "\"bodyStylesJson\"", "\"titleStylesJson\"", "\"paragraphs\"" })
        {
            StringAssert.Contains(json, key);
        }

        StringAssert.Contains(json, "\"kind\": \"BOLD\"");
        StringAssert.Contains(json, "Ti\u1EBFng Vi\u1EC7t", "non-ASCII letters are written as they are");
    }

    [TestMethod]
    public void Write_LeavesOutTheColorAndFolderWhenThereAreNone()
    {
        string json = NoteJson.Write(new NoteBundle(new Note { Title = "t" }));

        Assert.IsFalse(json.Contains("colorTag"));
        Assert.IsFalse(json.Contains("folderName"));
    }

    [TestMethod]
    public void Read_AcceptsAnAndroidFileVersionThree()
    {
        const string android = """
            {
              "format": "MyCustomNote", "version": 3,
              "title": "Shopping", "body": "milk\neggs",
              "createdAt": 1700000000000, "modifiedAt": 1700000005000,
              "isPinned": true, "isArchived": false, "colorTag": "ORANGE",
              "folderName": "Home", "tags": ["food", "weekly"],
              "checklistItems": [ { "text": "milk", "isChecked": true }, { "text": "eggs", "isChecked": false } ],
              "bodyStylesJson": [ { "start": 0, "end": 4, "kind": "BOLD" } ],
              "titleStylesJson": []
            }
            """;

        var bundle = NoteJson.Read(android, "x", 5);

        Assert.AreEqual("Shopping", bundle.Note.Title);
        Assert.AreEqual("milk\neggs", bundle.Note.Body);
        Assert.IsTrue(bundle.Note.IsPinned);
        Assert.AreEqual(NoteColorTag.Orange, bundle.Note.ColorTag);
        Assert.AreEqual(1700000000000, bundle.Note.CreatedAt);
        Assert.AreEqual("Home", bundle.FolderName);
        CollectionAssert.AreEqual(new[] { "food", "weekly" }, bundle.Tags.ToList());
        Assert.IsTrue(bundle.ChecklistItems[0].IsChecked);
        Assert.AreEqual(new StyleSpan(0, 4, TextStyleKind.Bold), bundle.Note.BodyStyles.Single());
        Assert.AreEqual(0, bundle.Note.Paragraphs.Count, "Android has no paragraph kinds");
    }

    [TestMethod]
    public void Read_FillsInDefaultsForMissingParts()
    {
        var bundle = NoteJson.Read("{}", "File name", 777);

        Assert.AreEqual("File name", bundle.Note.Title);
        Assert.AreEqual("", bundle.Note.Body);
        Assert.AreEqual(777, bundle.Note.CreatedAt);
        Assert.AreEqual(777, bundle.Note.ModifiedAt);
        Assert.IsNull(bundle.Note.ColorTag);
        Assert.IsNull(bundle.FolderName);
        Assert.AreEqual(0, bundle.Tags.Count);
        Assert.AreEqual(0, bundle.ChecklistItems.Count);
    }

    [TestMethod]
    public void Read_AcceptsTheSpansAsText_AndSkipsDamagedOnes()
    {
        const string json = """
            { "body": "hello world",
              "bodyStylesJson": "[{\"start\":0,\"end\":5,\"kind\":\"BOLD\"},{\"start\":\"x\"},{\"start\":6,\"end\":11,\"kind\":\"SPARKLY\"}]",
              "titleStylesJson": "not json" }
            """;

        var bundle = NoteJson.Read(json, "t", 1);

        Assert.AreEqual(new StyleSpan(0, 5, TextStyleKind.Bold), bundle.Note.BodyStyles.Single());
        Assert.AreEqual(0, bundle.Note.TitleStyles.Count);
    }

    [TestMethod]
    public void Read_CleansSpansAgainstTheText()
    {
        var bundle = NoteJson.Read("{ \"body\": \"ab\\ncd\", \"bodyStylesJson\": [{\"start\":0,\"end\":99,\"kind\":\"BOLD\"}] }", "t", 1);

        CollectionAssert.AreEqual(
            new[] { new StyleSpan(0, 2, TextStyleKind.Bold), new StyleSpan(3, 5, TextStyleKind.Bold) },
            bundle.Note.BodyStyles.ToList());
    }

    [TestMethod]
    public void Read_IgnoresBlankAndDuplicateTagsAndEmptyChecklistItems()
    {
        const string json = """{ "tags": ["a", " a ", "", "b"], "checklistItems": [ {"text": ""}, {"text": "x"} ], "colorTag": "DEFAULT" }""";

        var bundle = NoteJson.Read(json, "t", 1);

        CollectionAssert.AreEqual(new[] { "a", "b" }, bundle.Tags.ToList());
        Assert.AreEqual("x", bundle.ChecklistItems.Single().Text);
        Assert.IsNull(bundle.Note.ColorTag);
    }

    [TestMethod]
    [DataRow("not json")]
    [DataRow("")]
    [DataRow("[1,2]")]
    [DataRow("\"text\"")]
    [DataRow("42")]
    public void Read_RejectsWhatIsNotAJsonObject(string text)
    {
        Assert.ThrowsExactly<FormatException>(() => NoteJson.Read(text, "t", 1));
    }
}

[TestClass]
public class NoteExporterTests
{
    private static NoteBundle Sample() => new(new Note
    {
        Title = "My title",
        Body = "Heading\nitem one\nitem two\nplain *star*",
        TitleStyles = [new StyleSpan(0, 2, TextStyleKind.Bold)],
        BodyStyles = [new StyleSpan(0, 7, TextStyleKind.Bold)],
        Paragraphs = [new ParagraphFormat(0, ParagraphKind.Heading), new ParagraphFormat(1, ParagraphKind.Bullet), new ParagraphFormat(2, ParagraphKind.Bullet)],
    });

    [TestMethod]
    public void Text_HasTheTitleABlankLineAndThePrefixedBody()
    {
        Assert.AreEqual("My title\n\n# Heading\n- item one\n- item two\nplain *star*", NoteExporter.Export(Sample(), NoteFileFormat.Text));
    }

    [TestMethod]
    public void Markdown_ShowsTheMarkup_AndEndsWithTheEmbeddedNote()
    {
        string markdown = NoteExporter.Export(Sample(), NoteFileFormat.Markdown);

        StringAssert.StartsWith(markdown, "# **My** title\n\n# **Heading**\n- item one\n- item two\nplain \\*star\\*");
        StringAssert.Contains(markdown, "<!--MYCUSTOMNOTE:");
        StringAssert.EndsWith(markdown, "-->");
    }

    [TestMethod]
    public void Html_IsACompletePage()
    {
        string html = NoteExporter.Export(Sample(), NoteFileFormat.Html);

        StringAssert.Contains(html, "<h1 class=\"note-title\">My title</h1>");
        StringAssert.Contains(html, "<h2><strong>Heading</strong></h2>");
        StringAssert.Contains(html, "<ul>");
        StringAssert.Contains(html, "<li>item one</li>");
    }

    [TestMethod]
    public void Json_IsTheJsonFormat()
    {
        Assert.AreEqual(NoteJson.Write(Sample()), NoteExporter.Export(Sample(), NoteFileFormat.Json));
    }

    [TestMethod]
    public void Extensions_AreTheUsualOnes()
    {
        Assert.AreEqual("txt", NoteExporter.Extension(NoteFileFormat.Text));
        Assert.AreEqual("md", NoteExporter.Extension(NoteFileFormat.Markdown));
        Assert.AreEqual("html", NoteExporter.Extension(NoteFileFormat.Html));
        Assert.AreEqual("json", NoteExporter.Extension(NoteFileFormat.Json));
    }

    [TestMethod]
    [DataRow("Shopping list", "Shopping list")]
    [DataRow("a/b\\c:d*e?f", "a_b_c_d_e_f")]
    [DataRow("   ", "Untitled")]
    [DataRow("...", "Untitled")]
    [DataRow("  name.  ", "name")]
    public void SafeFileName_RemovesWhatWindowsDoesNotAllow(string title, string expected)
    {
        Assert.AreEqual(expected, NoteExporter.SafeFileName(title));
    }

    [TestMethod]
    public void SafeFileName_ShortensVeryLongTitles()
    {
        Assert.IsTrue(NoteExporter.SafeFileName(new string('a', 300)).Length <= 80);
    }
}

[TestClass]
public class NoteImporterTests
{
    private static Note Import(string text, string fileName = "file.md") => NoteImporter.Import(text, fileName, 1000).Note;

    [TestMethod]
    public void AnExportedMarkdownFile_ComesBackWithEverything()
    {
        var original = new NoteBundle(
            new Note
            {
                Title = "Plan *1*",
                Body = "Heading\nitem\n1 < 2 & **not bold**",
                IsPinned = true,
                ColorTag = NoteColorTag.Green,
                CreatedAt = 5,
                ModifiedAt = 6,
                BodyStyles = [new StyleSpan(0, 7, TextStyleKind.Bold)],
                Paragraphs = [new ParagraphFormat(0, ParagraphKind.Heading), new ParagraphFormat(1, ParagraphKind.Numbered)],
            },
            "Folder",
            ["x", "y"],
            [new ChecklistItem(0, 0, "todo", true, 0)]);

        var back = NoteImporter.Import(NoteExporter.Export(original, NoteFileFormat.Markdown), "plan.md", 999);

        Assert.AreEqual("Plan *1*", back.Note.Title);
        Assert.AreEqual(original.Note.Body, back.Note.Body);
        Assert.AreEqual(NoteColorTag.Green, back.Note.ColorTag);
        Assert.AreEqual(5, back.Note.CreatedAt);
        CollectionAssert.AreEqual(original.Note.BodyStyles.ToList(), back.Note.BodyStyles.ToList());
        CollectionAssert.AreEqual(original.Note.Paragraphs.ToList(), back.Note.Paragraphs.ToList());
        Assert.AreEqual("Folder", back.FolderName);
        CollectionAssert.AreEqual(new[] { "x", "y" }, back.Tags.ToList());
        Assert.AreEqual("todo", back.ChecklistItems.Single().Text);
    }

    [TestMethod]
    public void AJsonFile_IsReadByItsExtension()
    {
        string json = NoteJson.Write(new NoteBundle(new Note { Title = "J", Body = "b", CreatedAt = 1, ModifiedAt = 2 }));

        var note = NoteImporter.Import(json, "anything.JSON", 9).Note;

        Assert.AreEqual("J", note.Title);
        Assert.AreEqual(2, note.ModifiedAt);
    }

    [TestMethod]
    public void ADamagedEmbeddedBlock_FallsBackToTheVisibleMarkdown()
    {
        var note = Import("# Title\n\nsome **bold** text\n\n<!--MYCUSTOMNOTE:@@@not base64@@@-->");

        Assert.AreEqual("Title", note.Title);
        Assert.IsTrue(note.Body.StartsWith("some bold text"));
        Assert.AreEqual(new StyleSpan(5, 9, TextStyleKind.Bold), note.BodyStyles.First());
    }

    [TestMethod]
    public void Markdown_HeadingTitle_BoldItalicStrikeUnderline()
    {
        var note = Import("# My *great* note\n\n**bold** and *italic* and ~~gone~~ and <u>under</u>\n");

        Assert.AreEqual("My great note", note.Title);
        Assert.AreEqual(new StyleSpan(3, 8, TextStyleKind.Italic), note.TitleStyles.Single());
        Assert.AreEqual("bold and italic and gone and under\n", note.Body);
        CollectionAssert.AreEquivalent(
            new[]
            {
                new StyleSpan(0, 4, TextStyleKind.Bold),
                new StyleSpan(9, 15, TextStyleKind.Italic),
                new StyleSpan(20, 24, TextStyleKind.Strikethrough),
                new StyleSpan(29, 34, TextStyleKind.Underline),
            },
            note.BodyStyles.ToList());
    }

    [TestMethod]
    public void Markdown_NestedAndTripleMarkers()
    {
        var note = Import("# T\n\n<u>**word**</u> ***both***");

        CollectionAssert.AreEquivalent(
            new[]
            {
                new StyleSpan(0, 4, TextStyleKind.Underline),
                new StyleSpan(0, 4, TextStyleKind.Bold),
                new StyleSpan(5, 9, TextStyleKind.Bold),
                new StyleSpan(5, 9, TextStyleKind.Italic),
            },
            note.BodyStyles.ToList());
        Assert.AreEqual("word both", note.Body);
    }

    [TestMethod]
    public void Markdown_AMarkerWithoutAnEnd_StaysAsText()
    {
        var note = Import("# T\n\n2 * 3 and ~~open and <u>never closed");

        Assert.AreEqual("2 * 3 and ~~open and <u>never closed", note.Body);
        Assert.AreEqual(0, note.BodyStyles.Count);
    }

    [TestMethod]
    public void Markdown_ABackslashMakesACharacterPlain()
    {
        var note = Import("# T\n\n\\*not italic\\* and \\# and 1\\. and \\- x");

        Assert.AreEqual("*not italic* and # and 1. and - x", note.Body);
        Assert.AreEqual(0, note.BodyStyles.Count);
        Assert.AreEqual(0, note.Paragraphs.Count);
    }

    [TestMethod]
    public void Markdown_LinePrefixesBecomeParagraphKinds()
    {
        var note = Import("# Title\n\n# Heading\n- first\n+ second\n1. one\n12. two\nplain\n");

        Assert.AreEqual("Heading\nfirst\nsecond\none\ntwo\nplain\n", note.Body);
        CollectionAssert.AreEqual(
            new[]
            {
                new ParagraphFormat(0, ParagraphKind.Heading),
                new ParagraphFormat(1, ParagraphKind.Bullet),
                new ParagraphFormat(2, ParagraphKind.Bullet),
                new ParagraphFormat(3, ParagraphKind.Numbered),
                new ParagraphFormat(4, ParagraphKind.Numbered),
            },
            note.Paragraphs.ToList());
    }

    [TestMethod]
    public void Markdown_TwoTrailingSpacesAreAHardLineBreakNotText()
    {
        var note = Import("# T\n\nline one  \nline two");

        Assert.AreEqual("line one\nline two", note.Body);
    }

    [TestMethod]
    public void Spans_NeverCoverALineBreak()
    {
        var note = Import("# T\n\n**one\ntwo**");

        // the marker pair is split over two lines, so it is not a pair on one line: it stays as text
        Assert.AreEqual("**one\ntwo**", note.Body);
    }

    [TestMethod]
    public void PlainText_FirstLineBeforeABlankLineIsTheTitle()
    {
        var note = Import("Shopping\n\nmilk\neggs", "list.txt");

        Assert.AreEqual("Shopping", note.Title);
        Assert.AreEqual("milk\neggs", note.Body);
    }

    [TestMethod]
    public void PlainText_WithoutATitleLine_UsesTheFileNameAndKeepsAllText()
    {
        var note = Import("just some text\nsecond line", "My notes.txt");

        Assert.AreEqual("My notes", note.Title);
        Assert.AreEqual("just some text\nsecond line", note.Body);
    }

    [TestMethod]
    public void WindowsLineEndings_AreHandled()
    {
        var note = Import("# T\r\n\r\nline one\r\nline two\r\n");

        Assert.AreEqual("line one\nline two\n", note.Body);
    }

    [TestMethod]
    public void EmptyFile_GivesAnEmptyNoteWithTheFileNameAsTitle()
    {
        var note = Import("", "Empty one.txt");

        Assert.AreEqual("Empty one", note.Title);
        Assert.AreEqual("", note.Body);
    }

    [TestMethod]
    public void TimesAreTheImportTime()
    {
        var note = Import("# T\n\nx");

        Assert.AreEqual(1000, note.CreatedAt);
        Assert.AreEqual(1000, note.ModifiedAt);
    }

    [TestMethod]
    public void Html_IsNotSupported()
    {
        Assert.ThrowsExactly<NotSupportedException>(() => NoteImporter.Import("<p>x</p>", "page.html", 1));
        Assert.ThrowsExactly<NotSupportedException>(() => NoteImporter.Import("<p>x</p>", "page.htm", 1));
    }

    [TestMethod]
    public void BrokenJson_IsAFormatError()
    {
        Assert.ThrowsExactly<FormatException>(() => NoteImporter.Import("{ nope", "a.json", 1));
    }

    [TestMethod]
    public void VietnameseAndEmoji_SurviveAnExportAndImport()
    {
        string body = "Xin ch\u00E0o \U0001F600 **\u0111\u1EADm**";
        var bundle = new NoteBundle(new Note { Title = "Vi\u1EC7t", Body = body, CreatedAt = 1, ModifiedAt = 1 });

        foreach (var format in new[] { NoteFileFormat.Markdown, NoteFileFormat.Json })
        {
            var back = NoteImporter.Import(NoteExporter.Export(bundle, format), "n." + NoteExporter.Extension(format), 5).Note;

            Assert.AreEqual(body, back.Body, format.ToString());
            Assert.AreEqual("Vi\u1EC7t", back.Title, format.ToString());
        }

        Assert.IsTrue(Encoding.UTF8.GetByteCount(NoteExporter.Export(bundle, NoteFileFormat.Text)) > 0);
    }
}
