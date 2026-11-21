using System.Text;
using System.Windows.Documents;
using MyCustomNote.Core.Formatting;
using MyCustomNote.Editor;

namespace MyCustomNote.EditorSpike;

/// <summary>
/// Automatic checks for the riskiest parts of the spike: does text + spans survive a trip through a
/// WPF RichTextBox, do toolbar toggles on the document match the tested Core logic, do undo and redo work.
/// Uses off-screen editors and a fixed random seed, so a failure can be repeated.
/// </summary>
internal sealed class SelfCheckRunner
{
    private const string Smile = "\U0001F600";

    private readonly StringBuilder _failures = new();
    private readonly StringBuilder _info = new();
    private int _pass;
    private int _fail;

    private readonly SpanEditor _live;

    private SelfCheckRunner(SpanEditor liveEditor)
    {
        _live = liveEditor;
    }

    /// <param name="liveEditor">An editor that is visible in a window. Undo and redo only work there (off-screen editors report CanUndo = false), so that test uses it and replaces its content.</param>
    public static string RunAll(SpanEditor liveEditor) => new SelfCheckRunner(liveEditor).Run();

    private string Run()
    {
        RoundTripFixedCases();
        RoundTripRandom();
        SelectionOffsetsAndToggleRandom();
        UndoRedo();
        TypingRule();
        FormatBeforeTyping();
        ListsRoundTripFixedCases();
        ListsRoundTripRandom();
        ListToggleAgainstModel();
        ListEnterAndBackspace();
        ListUndo();
        HeadingUndoAndBold();
        SingleLineEditor();
        Deleting();
        TypingBehaviour();

        var report = new StringBuilder();
        report.AppendLine($"SELF-CHECK   PASS {_pass}   FAIL {_fail}");
        report.AppendLine();
        if (_fail > 0)
        {
            report.AppendLine("Failures (first 40):");
            report.Append(_failures);
            report.AppendLine();
        }

        report.AppendLine("Info (WPF behaviour, not pass/fail):");
        report.Append(_info);
        return report.ToString();
    }

    // ---------- round trip: text + spans -> document -> text + spans ----------

    private void RoundTripFixedCases()
    {
        var editor = new SpanEditor(singleLine: false);
        var cases = new (string Name, string Text, StyleSpan[] Spans)[]
        {
            ("empty", "", []),
            ("plain", "hello", []),
            ("bold whole", "hello", [Span(0, 5, TextStyleKind.Bold)]),
            ("all four kinds overlapping", "hello world",
                [Span(0, 5, TextStyleKind.Bold), Span(2, 8, TextStyleKind.Italic), Span(6, 11, TextStyleKind.Underline), Span(0, 11, TextStyleKind.Strikethrough)]),
            ("three lines, span across all", "one\ntwo\nthree", [Span(0, 13, TextStyleKind.Bold)]),
            ("empty line in the middle", "a\n\nb", [Span(0, 4, TextStyleKind.Italic)]),
            ("trailing newline", "trailing\n", [Span(0, 9, TextStyleKind.Bold)]),
            ("leading newline", "\nx", [Span(0, 2, TextStyleKind.Underline)]),
            ("emoji", "a" + Smile + "b", [Span(1, 3, TextStyleKind.Bold)]),
            ("two emoji", Smile + Smile, [Span(0, 2, TextStyleKind.Underline), Span(2, 4, TextStyleKind.Strikethrough)]),
            ("asterisks", "****", [Span(0, 4, TextStyleKind.Bold)]),
            ("out of range span", "abc", [Span(1, 99, TextStyleKind.Bold)]),
        };

        foreach (var c in cases)
        {
            editor.SetModel(c.Text, c.Spans);
            var (text, spans) = editor.GetModel();
            var expected = StyleSpans.Normalize(c.Spans, c.Text);
            Check($"round trip: {c.Name}", text == c.Text && Same(spans, expected),
                () => $"expected {Describe(c.Text, expected)}\n      got      {Describe(text, spans)}");
        }
    }

    private void RoundTripRandom()
    {
        var random = new Random(12345);
        var editor = new SpanEditor(singleLine: false);
        for (int i = 0; i < 300; i++)
        {
            string text = RandomText(random, 0);
            var input = RandomSpans(random, text.Length);
            editor.SetModel(text, input);
            var (readText, readSpans) = editor.GetModel();
            var expected = StyleSpans.Normalize(input, text);
            Check($"random round trip #{i}", readText == text && Same(readSpans, expected),
                () => $"input    {Describe(text, input)}\n      expected {Describe(text, expected)}\n      got      {Describe(readText, readSpans)}");
        }
    }

    // ---------- selection offsets and toolbar toggles against the Core logic ----------

    private void SelectionOffsetsAndToggleRandom()
    {
        var random = new Random(67890);
        var editor = new SpanEditor(singleLine: false);
        for (int i = 0; i < 300; i++)
        {
            string text = RandomText(random, 1);
            var input = RandomSpans(random, text.Length);
            editor.SetModel(text, input);
            var before = StyleSpans.Normalize(input, text);

            var boundaries = Boundaries(text);
            if (boundaries.Count < 2)
            {
                continue;
            }

            int a = boundaries[random.Next(boundaries.Count)];
            int b = boundaries[random.Next(boundaries.Count)];
            if (a == b)
            {
                continue;
            }

            int start = Math.Min(a, b);
            int end = Math.Max(a, b);
            var kind = (TextStyleKind)random.Next(4);

            editor.SelectOffsets(start, end);
            var selected = editor.GetSelectionOffsets();
            Check($"selection round trip #{i}", selected == (start, end),
                () => $"{Escape(text)}: asked ({start},{end}) got ({selected.Start},{selected.End})");

            var expected = StyleSpans.Toggle(before, text, selected.Start, selected.End, kind);
            bool styledBefore = editor.IsStyled(kind);
            editor.ToggleStyle(kind);
            var (readText, readSpans) = editor.GetModel();
            Check($"toggle #{i} {kind}", readText == text && Same(readSpans, expected),
                () => $"before   {Describe(text, before)}\n      toggle   {kind} on ({selected.Start},{selected.End}) styledBefore={styledBefore}\n      expected {Describe(text, expected)}\n      got      {Describe(readText, readSpans)}");
        }
    }

    // ---------- undo and redo ----------

    private void UndoRedo()
    {
        var editor = _live;
        editor.SetModel("hello world", []);

        editor.SelectOffsets(0, 5);
        editor.ToggleStyle(TextStyleKind.Bold);
        var afterBold = editor.GetModel().Spans;
        Check("undo: bold applied", Same(afterBold, [Span(0, 5, TextStyleKind.Bold)]), () => Describe("hello world", afterBold));
        Check("undo: CanUndo after a toggle", editor.CanUndo, () => "CanUndo is false");

        editor.SelectOffsets(2, 8);
        editor.ToggleStyle(TextStyleKind.Italic);
        var afterItalic = editor.GetModel().Spans;
        Check("undo: italic applied", Same(afterItalic, [Span(0, 5, TextStyleKind.Bold), Span(2, 8, TextStyleKind.Italic)]), () => Describe("hello world", afterItalic));

        editor.Undo();
        var undo1 = editor.GetModel().Spans;
        Check("undo: first undo removes only the italic", Same(undo1, [Span(0, 5, TextStyleKind.Bold)]), () => Describe("hello world", undo1));

        editor.Undo();
        var undo2 = editor.GetModel().Spans;
        Check("undo: second undo removes the bold", Same(undo2, []), () => Describe("hello world", undo2));

        editor.Redo();
        var redo1 = editor.GetModel().Spans;
        Check("undo: redo brings the bold back", Same(redo1, [Span(0, 5, TextStyleKind.Bold)]), () => Describe("hello world", redo1));
    }

    // ---------- typing rule like Word (needs the visible editor: simulated keyboard input) ----------

    private void TypingRule()
    {
        var editor = _live;
        editor.Focus();

        var cases = new (string Name, string Text, StyleSpan[] Spans, int Caret, string ExpectedText, StyleSpan[] Expected)[]
        {
            ("start of a bold word after a space: plain", "say hello", [Span(4, 9, TextStyleKind.Bold)], 4,
                "say Xhello", [Span(5, 10, TextStyleKind.Bold)]),
            ("end of a bold word: bold", "hello world", [Span(0, 5, TextStyleKind.Bold)], 5,
                "helloX world", [Span(0, 6, TextStyleKind.Bold)]),
            ("inside a bold word: bold", "hello world", [Span(0, 5, TextStyleKind.Bold)], 2,
                "heXllo world", [Span(0, 6, TextStyleKind.Bold)]),
            ("after the space that follows bold: plain", "hello world", [Span(0, 5, TextStyleKind.Bold)], 6,
                "hello Xworld", [Span(0, 5, TextStyleKind.Bold)]),
            ("start of the document: takes the next character", "hello", [Span(0, 5, TextStyleKind.Bold)], 0,
                "Xhello", [Span(0, 6, TextStyleKind.Bold)]),
            ("start of an italic word after a space: plain", "a b", [Span(2, 3, TextStyleKind.Italic)], 2,
                "a Xb", [Span(3, 4, TextStyleKind.Italic)]),
            ("end of an underlined word: underlined", "ab cd", [Span(0, 2, TextStyleKind.Underline)], 2,
                "abX cd", [Span(0, 3, TextStyleKind.Underline)]),
        };

        bool inconclusive = false;
        foreach (var c in cases)
        {
            editor.SetModel(c.Text, c.Spans);
            editor.SelectOffsets(c.Caret, c.Caret);
            editor.SimulateTyping("X");
            var (text, spans) = editor.GetModel();

            if (text == c.Text)
            {
                inconclusive = true; // the simulated keyboard input did not reach the editor
                continue;
            }

            Check($"typing rule: {c.Name}", text == c.ExpectedText && Same(spans, c.Expected),
                () => $"expected {Describe(c.ExpectedText, c.Expected)}\n      got      {Describe(text, spans)}");
        }

        if (inconclusive)
        {
            _info.AppendLine("  typing rule: the simulated typing did not insert text for some cases, so they were NOT checked. Try them by hand.");
        }
    }

    // "Format before typing": with nothing selected a style button sets the style of the next typed text.
    private void FormatBeforeTyping()
    {
        var editor = _live;
        editor.Focus();

        var cases = new (string Name, string Text, StyleSpan[] Spans, int Caret, TextStyleKind Style, string Typed, string ExpectedText, StyleSpan[] Expected)[]
        {
            ("bold on at the end of plain text", "hello", [], 5, TextStyleKind.Bold, "X",
                "helloX", [Span(5, 6, TextStyleKind.Bold)]),
            ("bold off at the end of a bold word", "hello", [Span(0, 5, TextStyleKind.Bold)], 5, TextStyleKind.Bold, "X",
                "helloX", [Span(0, 5, TextStyleKind.Bold)]),
            ("italic on in the middle of plain text", "abcd", [], 2, TextStyleKind.Italic, "X",
                "abXcd", [Span(2, 3, TextStyleKind.Italic)]),
            ("underline on in an empty note", "", [], 0, TextStyleKind.Underline, "X",
                "X", [Span(0, 1, TextStyleKind.Underline)]),
            ("bold on at the start of a line", "a\nb", [], 2, TextStyleKind.Bold, "X",
                "a\nXb", [Span(2, 3, TextStyleKind.Bold)]),
        };

        bool inconclusive = false;
        foreach (var c in cases)
        {
            editor.SetModel(c.Text, c.Spans);
            editor.SelectOffsets(c.Caret, c.Caret);
            editor.ToggleStyle(c.Style);
            bool shownOn = editor.IsStyled(c.Style);
            editor.SimulateTyping(c.Typed);
            var (text, spans) = editor.GetModel();

            if (text == c.Text)
            {
                inconclusive = true; // the simulated keyboard input did not reach the editor
                continue;
            }

            Check($"format before typing: {c.Name}", text == c.ExpectedText && Same(spans, c.Expected),
                () => $"expected {Describe(c.ExpectedText, c.Expected)}\n      got      {Describe(text, spans)} (button showed {(shownOn ? "on" : "off")} before typing)");
        }

        if (inconclusive)
        {
            _info.AppendLine("  format before typing: the simulated typing did not insert text for some cases, so they were NOT checked. Try them by hand.");
        }
    }

    private void SingleLineEditor()
    {
        var title = new SpanEditor(singleLine: true);
        title.SetModel("a\nb", []);
        string text = title.GetModel().Text;
        Check("single line: newline becomes a space", text == "a b", () => Escape(text));
    }

    // ---------- deleting must match Core.ApplyEdit exactly ----------

    private void Deleting()
    {
        var cases = new (string Name, string Text, StyleSpan[] Spans, int Start, int End)[]
        {
            ("inside a span", "hello world", [Span(0, 5, TextStyleKind.Bold), Span(3, 8, TextStyleKind.Italic)], 2, 6),
            ("across lines", "ab\ncd", [Span(0, 2, TextStyleKind.Bold), Span(3, 5, TextStyleKind.Bold)], 1, 4),
            ("whole span", "hello world", [Span(2, 5, TextStyleKind.Underline)], 1, 7),
            ("before a span", "hello world", [Span(6, 11, TextStyleKind.Strikethrough)], 0, 3),
        };

        var editor = new SpanEditor(singleLine: false);
        foreach (var c in cases)
        {
            editor.SetModel(c.Text, c.Spans);
            editor.DeleteRange(c.Start, c.End);
            var (text, spans) = editor.GetModel();
            string expectedText = c.Text.Remove(c.Start, c.End - c.Start);
            var expected = StyleSpans.Normalize(StyleSpans.ApplyEdit(StyleSpans.Normalize(c.Spans, c.Text), c.Start, c.End - c.Start, 0), expectedText);
            Check($"delete: {c.Name}", text == expectedText && Same(spans, expected),
                () => $"expected {Describe(expectedText, expected)}\n      got      {Describe(text, spans)}");
        }
    }

    // ---------- typing: report what WPF does, it is a policy question, not pass/fail ----------

    private void TypingBehaviour()
    {
        foreach (int position in new[] { 0, 2, 5 })
        {
            var editor = new SpanEditor(singleLine: false);
            var start = new[] { Span(0, 5, TextStyleKind.Bold) };
            editor.SetModel("hello", start);
            editor.InsertTextAt(position, "X");
            var (text, spans) = editor.GetModel();
            string expectedText = "hello".Insert(position, "X");
            var predicted = StyleSpans.Normalize(StyleSpans.ApplyEdit(start, position, 0, 1), expectedText);
            string verdict = text == expectedText && Same(spans, predicted) ? "same as Core.ApplyEdit" : "DIFFERENT from Core.ApplyEdit";
            _info.AppendLine($"  type 'X' at {position} into bold \"hello\": WPF {Describe(text, spans)}  |  Core {Describe(expectedText, predicted)}  =>  {verdict}");
        }

        // Start of a bold word in the middle of a line (the loop above only tried the very start of the document).
        {
            var editor = new SpanEditor(singleLine: false);
            var start = new[] { Span(4, 9, TextStyleKind.Bold) };
            editor.SetModel("say hello", start);
            editor.InsertTextAt(4, "X");
            var (text, spans) = editor.GetModel();
            string expectedText = "say Xhello";
            var predicted = StyleSpans.Normalize(StyleSpans.ApplyEdit(start, 4, 0, 1), expectedText);
            string verdict = text == expectedText && Same(spans, predicted) ? "same as Core.ApplyEdit" : "DIFFERENT from Core.ApplyEdit";
            _info.AppendLine($"  type 'X' at 4 (start of bold word after a space) in \"say hello\": WPF {Describe(text, spans)}  |  Core {Describe(expectedText, predicted)}  =>  {verdict}");
        }
    }

    // ---------- lists (step 1.4b): load, read back, toolbar toggles, Enter, Backspace, undo ----------

    private void ListsRoundTripFixedCases()
    {
        var editor = new SpanEditor(singleLine: false);
        var cases = new (string Name, string Text, StyleSpan[] Spans, ParagraphFormat[] Paragraphs)[]
        {
            ("bullets", "a\nb\nc", [], [Para(0, ParagraphKind.Bullet), Para(1, ParagraphKind.Bullet)]),
            ("numbered", "a\nb\nc", [], [Para(0, ParagraphKind.Numbered), Para(1, ParagraphKind.Numbered), Para(2, ParagraphKind.Numbered)]),
            ("numbering restarts after a normal line", "a\nb\nc", [], [Para(0, ParagraphKind.Numbered), Para(2, ParagraphKind.Numbered)]),
            ("bullet list next to a numbered list", "a\nb\nc\nd", [],
                [Para(0, ParagraphKind.Bullet), Para(1, ParagraphKind.Bullet), Para(2, ParagraphKind.Numbered), Para(3, ParagraphKind.Numbered)]),
            ("empty list items", "a\n\nb", [], [Para(0, ParagraphKind.Bullet), Para(1, ParagraphKind.Bullet), Para(2, ParagraphKind.Bullet)]),
            ("styled text inside a list", "bold item\nplain", [Span(0, 4, TextStyleKind.Bold)], [Para(0, ParagraphKind.Bullet)]),
            ("list on an empty note", "", [], [Para(0, ParagraphKind.Bullet)]),
            ("emoji in a numbered list", "a" + Smile + "b\nc", [Span(1, 3, TextStyleKind.Underline)], [Para(0, ParagraphKind.Numbered), Para(1, ParagraphKind.Numbered)]),
            ("heading between normal lines", "a\nb\nc", [], [Para(1, ParagraphKind.Heading)]),
            ("bold text inside a heading is a Bold span, the heading itself is not", "title\nbody", [Span(0, 5, TextStyleKind.Bold)], [Para(0, ParagraphKind.Heading)]),
            ("heading next to lists", "a\nb\nc\nd", [], [Para(0, ParagraphKind.Bullet), Para(1, ParagraphKind.Heading), Para(2, ParagraphKind.Numbered), Para(3, ParagraphKind.Heading)]),
            ("empty heading", "\nx", [], [Para(0, ParagraphKind.Heading)]),
        };

        foreach (var c in cases)
        {
            editor.SetModel(c.Text, c.Spans, c.Paragraphs);
            CheckFullModel($"lists round trip: {c.Name}", editor, c.Text, c.Spans, c.Paragraphs);
        }
    }

    private void ListsRoundTripRandom()
    {
        var random = new Random(13579);
        var editor = new SpanEditor(singleLine: false);
        for (int i = 0; i < 300; i++)
        {
            string text = RandomText(random, 0);
            var spans = RandomSpans(random, text.Length);
            var paragraphs = RandomParagraphs(random, text);
            editor.SetModel(text, spans, paragraphs);
            CheckFullModel($"lists random round trip #{i}", editor, text, spans, paragraphs);
        }
    }

    // WPF's own list commands against the Core function ParagraphFormats.Toggle (the specification).
    // Runs on the visible editor, because commands need a live control.
    private void ListToggleAgainstModel()
    {
        var random = new Random(24680);
        var editor = _live;
        for (int i = 0; i < 200; i++)
        {
            string text = RandomText(random, 1);
            var spans = RandomSpans(random, text.Length);
            var paragraphs = RandomParagraphs(random, text);
            editor.SetModel(text, spans, paragraphs);

            var boundaries = Boundaries(text);
            int a = boundaries[random.Next(boundaries.Count)];
            int b = random.Next(3) == 0 ? a : boundaries[random.Next(boundaries.Count)];
            editor.SelectOffsets(a, b);
            var selected = editor.GetSelectionOffsets();
            var kind = (ParagraphKind)random.Next(1, 4); // Heading, Bullet or Numbered

            var before = editor.GetParagraphFormats();
            var spansBefore = editor.GetModel().Spans;
            var expected = ParagraphFormats.Toggle(before, text, selected.Start, selected.End, kind);

            editor.ToggleParagraph(kind);

            var (readText, readSpans) = editor.GetModel();
            var actual = editor.GetParagraphFormats();
            var numbers = editor.GetDisplayedNumbers();
            bool ok = readText == text
                && Same(readSpans, spansBefore)
                && actual.SequenceEqual(expected)
                && numbers.SequenceEqual(ParagraphFormats.Numbering(actual, text));
            Check($"list toggle #{i} {kind}", ok,
                () => $"{Escape(text)} selection ({selected.Start},{selected.End})\n      before   {DescribeParagraphs(before)}\n      expected {DescribeParagraphs(expected)}\n      got      {DescribeParagraphs(actual)} numbers [{string.Join(",", numbers)}] text {(readText == text ? "same" : "CHANGED " + Escape(readText))} spans {(Same(readSpans, spansBefore) ? "same" : "CHANGED")}");
        }
    }

    // What the user expects from Enter and Backspace in a list (like Word). These are my assumptions
    // from step 1.4; a FAIL here means WPF behaves differently and the editor needs a handler.
    private void ListEnterAndBackspace()
    {
        var editor = _live;
        editor.Focus();

        var cases = new (string Name, string Text, ParagraphFormat[] Before, int Caret, Action<SpanEditor> Act, string ExpectedText, ParagraphFormat[] Expected)[]
        {
            ("Enter at the end of a bullet item continues the list", "a", [Para(0, ParagraphKind.Bullet)], 1, PressEnter,
                "a\n", [Para(0, ParagraphKind.Bullet), Para(1, ParagraphKind.Bullet)]),
            ("Enter in the middle of a bullet item splits it", "ab", [Para(0, ParagraphKind.Bullet)], 1, PressEnter,
                "a\nb", [Para(0, ParagraphKind.Bullet), Para(1, ParagraphKind.Bullet)]),
            ("Enter on an empty list item starts another list item", "a\n", [Para(0, ParagraphKind.Bullet), Para(1, ParagraphKind.Bullet)], 2, PressEnter,
                "a\n\n", [Para(0, ParagraphKind.Bullet), Para(1, ParagraphKind.Bullet), Para(2, ParagraphKind.Bullet)]),
            ("Enter twice on a numbered item keeps numbering", "a\n", [Para(0, ParagraphKind.Numbered), Para(1, ParagraphKind.Numbered)], 2, PressEnter,
                "a\n\n", [Para(0, ParagraphKind.Numbered), Para(1, ParagraphKind.Numbered), Para(2, ParagraphKind.Numbered)]),
            ("Enter at the end of a numbered item continues the numbering", "a", [Para(0, ParagraphKind.Numbered)], 1, PressEnter,
                "a\n", [Para(0, ParagraphKind.Numbered), Para(1, ParagraphKind.Numbered)]),
            ("Backspace at the start of a bullet item removes the bullet, keeps the text", "a", [Para(0, ParagraphKind.Bullet)], 0, PressBackspace,
                "a", []),
            ("Enter in a normal paragraph stays normal", "a", [], 1, PressEnter,
                "a\n", []),
            ("Enter at the end of a heading starts a normal line", "a", [Para(0, ParagraphKind.Heading)], 1, PressEnter,
                "a\n", [Para(0, ParagraphKind.Heading)]),
            ("Enter on an empty heading starts a normal line", "", [Para(0, ParagraphKind.Heading)], 0, PressEnter,
                "\n", [Para(0, ParagraphKind.Heading)]),
            ("Enter in the middle of a heading splits it into two headings", "ab", [Para(0, ParagraphKind.Heading)], 1, PressEnter,
                "a\nb", [Para(0, ParagraphKind.Heading), Para(1, ParagraphKind.Heading)]),
            ("Backspace at the start of a heading removes the heading, keeps the text", "a", [Para(0, ParagraphKind.Heading)], 0, PressBackspace,
                "a", []),
            ("Backspace at the start of a heading on the second line", "x\na", [Para(1, ParagraphKind.Heading)], 2, PressBackspace,
                "x\na", []),
        };

        foreach (var c in cases)
        {
            editor.SetModel(c.Text, [], c.Before);
            editor.SelectOffsets(c.Caret, c.Caret);
            c.Act(editor);

            var (text, _) = editor.GetModel();
            var actual = editor.GetParagraphFormats();
            var numbers = editor.GetDisplayedNumbers();
            var expected = ParagraphFormats.Normalize(c.Expected, c.ExpectedText);
            bool ok = text == c.ExpectedText
                && actual.SequenceEqual(expected)
                && numbers.SequenceEqual(ParagraphFormats.Numbering(actual, text));
            Check($"list keys: {c.Name}", ok,
                () => $"expected {Escape(c.ExpectedText)} {DescribeParagraphs(expected)}\n      got      {Escape(text)} {DescribeParagraphs(actual)} numbers [{string.Join(",", numbers)}]");
        }
    }

    private void ListUndo()
    {
        var editor = _live;
        editor.SetModel("a\nb", [], [Para(0, ParagraphKind.Bullet)]);
        editor.SelectOffsets(0, 3);
        editor.ToggleParagraph(ParagraphKind.Bullet);

        var bothBullets = new[] { Para(0, ParagraphKind.Bullet), Para(1, ParagraphKind.Bullet) };
        var afterToggle = editor.GetParagraphFormats();
        Check("list undo: toggle makes both lines bullets", afterToggle.SequenceEqual(bothBullets), () => DescribeParagraphs(afterToggle));

        editor.Undo();
        var afterUndo = editor.GetParagraphFormats();
        Check("list undo: undo restores the first line only", afterUndo.SequenceEqual([Para(0, ParagraphKind.Bullet)]), () => DescribeParagraphs(afterUndo));

        editor.Redo();
        var afterRedo = editor.GetParagraphFormats();
        Check("list undo: redo makes both bullets again", afterRedo.SequenceEqual(bothBullets), () => DescribeParagraphs(afterRedo));
    }

    private void HeadingUndoAndBold()
    {
        var editor = _live;

        // Text with a font size of its own (what typing can leave behind) must still look like a heading.
        editor.SetModel("abc", [], []);
        new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd)
            .ApplyPropertyValue(TextElement.FontSizeProperty, 12.0);
        editor.SelectOffsets(0, 3);
        editor.ToggleParagraph(ParagraphKind.Heading);
        object size = new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd)
            .GetPropertyValue(TextElement.FontSizeProperty);
        Check("heading: text with its own font size is shown at heading size", size is double d && d == DocumentMap.HeadingFontSize,
            () => "size " + size);

        // Text that gets a size of its own while typing into a heading is put back to the heading size.
        editor.SetModel("a", [], [Para(0, ParagraphKind.Heading)]);
        ((Paragraph)editor.Document.Blocks.FirstBlock).Inlines.FirstInline.FontSize = 12;
        editor.InsertTextAt(1, "b");
        object typedSize = new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd)
            .GetPropertyValue(TextElement.FontSizeProperty);
        Check("heading: text typed into a heading keeps the heading size", typedSize is double t && t == DocumentMap.HeadingFontSize,
            () => "size " + typedSize);

        // An empty heading line, then typing into it.
        editor.SetModel("", [], [Para(0, ParagraphKind.Heading)]);
        editor.InsertTextAt(0, "x");
        object emptyTypedSize = new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd)
            .GetPropertyValue(TextElement.FontSizeProperty);
        Check("heading: typing into an empty heading line shows the heading size", emptyTypedSize is double u && u == DocumentMap.HeadingFontSize,
            () => "size " + emptyTypedSize);

        editor.SetModel("abc\ndef", [], []);
        editor.SelectOffsets(0, 7);
        editor.ToggleParagraph(ParagraphKind.Heading);
        var both = new[] { Para(0, ParagraphKind.Heading), Para(1, ParagraphKind.Heading) };
        var afterToggle = editor.GetParagraphFormats();
        Check("heading: toggle makes both lines headings", afterToggle.SequenceEqual(both), () => DescribeParagraphs(afterToggle));
        var spans = editor.GetModel().Spans;
        Check("heading: the heading look is not read back as Bold", spans.Count == 0, () => Describe("abc\ndef", spans));

        editor.SelectOffsets(0, 3);
        editor.ToggleStyle(TextStyleKind.Bold);
        var afterBold = editor.GetModel().Spans;
        var stillHeadings = editor.GetParagraphFormats();
        Check("heading: bold works inside a heading", Same(afterBold, [Span(0, 3, TextStyleKind.Bold)]) && stillHeadings.SequenceEqual(both),
            () => Describe("abc\ndef", afterBold) + " " + DescribeParagraphs(stillHeadings));

        editor.Undo();
        editor.Undo();
        var afterUndo = editor.GetParagraphFormats();
        Check("heading: undo of the toggle returns both lines to normal", afterUndo.Count == 0, () => DescribeParagraphs(afterUndo));

        editor.Redo();
        var afterRedo = editor.GetParagraphFormats();
        Check("heading: redo makes both headings again", afterRedo.SequenceEqual(both), () => DescribeParagraphs(afterRedo));

        // Enter at the end of a heading, then undo.
        editor.SetModel("a", [], [Para(0, ParagraphKind.Heading)]);
        editor.SelectOffsets(1, 1);
        PressEnter(editor);
        editor.Undo();
        var (text, _) = editor.GetModel();
        var formats = editor.GetParagraphFormats();
        Check("heading: undo of Enter at the end of a heading", text == "a" && formats.SequenceEqual([Para(0, ParagraphKind.Heading)]),
            () => Escape(text) + " " + DescribeParagraphs(formats));

        // Enter on an empty list item, then undo.
        editor.SetModel("a\n", [], [Para(0, ParagraphKind.Bullet), Para(1, ParagraphKind.Bullet)]);
        editor.SelectOffsets(2, 2);
        PressEnter(editor);
        editor.Undo();
        var (text2, _) = editor.GetModel();
        var formats2 = editor.GetParagraphFormats();
        Check("list undo: undo of Enter on an empty list item", text2 == "a\n" && formats2.SequenceEqual([Para(0, ParagraphKind.Bullet), Para(1, ParagraphKind.Bullet)]),
            () => Escape(text2) + " " + DescribeParagraphs(formats2));
    }

    private static void PressEnter(SpanEditor editor) => EditingCommands.EnterParagraphBreak.Execute(null, editor);

    private static void PressBackspace(SpanEditor editor) => EditingCommands.Backspace.Execute(null, editor);

    private void CheckFullModel(string name, SpanEditor editor, string text, IEnumerable<StyleSpan> spans, IEnumerable<ParagraphFormat> paragraphs)
    {
        var (readText, readSpans) = editor.GetModel();
        var readParagraphs = editor.GetParagraphFormats();
        var numbers = editor.GetDisplayedNumbers();

        var expectedSpans = StyleSpans.Normalize(spans, text);
        var expectedParagraphs = ParagraphFormats.Normalize(paragraphs, text);
        var expectedNumbers = ParagraphFormats.Numbering(expectedParagraphs, text);

        bool ok = readText == text
            && Same(readSpans, expectedSpans)
            && readParagraphs.SequenceEqual(expectedParagraphs)
            && numbers.SequenceEqual(expectedNumbers);
        Check(name, ok,
            () => $"{Describe(text, expectedSpans)} {DescribeParagraphs(expectedParagraphs)} numbers [{string.Join(",", expectedNumbers)}]\n      got      {Describe(readText, readSpans)} {DescribeParagraphs(readParagraphs)} numbers [{string.Join(",", numbers)}]");
    }

    private static ParagraphFormat Para(int line, ParagraphKind kind) => new(line, kind);

    private static string DescribeParagraphs(IEnumerable<ParagraphFormat> paragraphs) =>
        paragraphs.Any() ? string.Join(", ", paragraphs.Select(p => $"{p.Kind}@{p.Line}")) : "(no list lines)";

    // Each line is a list line or heading with a probability.
    private static List<ParagraphFormat> RandomParagraphs(Random random, string text)
    {
        var formats = new List<ParagraphFormat>();
        int lines = ParagraphFormats.LineCount(text);
        for (int line = 0; line < lines; line++)
        {
            if (random.Next(100) < 45)
            {
                formats.Add(new ParagraphFormat(line, (ParagraphKind)random.Next(1, 4)));
            }
        }

        return formats;
    }

    // ---------- helpers ----------

    private void Check(string name, bool ok, Func<string> detail)
    {
        if (ok)
        {
            _pass++;
            return;
        }

        _fail++;
        if (_fail <= 40)
        {
            _failures.AppendLine($"  FAIL {name}");
            _failures.AppendLine($"      {detail()}");
        }
    }

    private static StyleSpan Span(int start, int end, TextStyleKind kind) => new(start, end, kind);

    private static bool Same(IReadOnlyList<StyleSpan> a, IReadOnlyList<StyleSpan> b) => a.SequenceEqual(b);

    private static string Escape(string text) => "\"" + text.Replace("\n", "\\n") + "\"";

    private static string Describe(string text, IEnumerable<StyleSpan> spans) =>
        Escape(text) + " " + (spans.Any() ? string.Join(", ", spans.Select(s => $"{s.Kind}[{s.Start},{s.End})")) : "(no spans)");

    private static string RandomText(Random random, int minimumLength)
    {
        string[] alphabet = ["a", "b", "c", " ", "\n", Smile];
        var builder = new StringBuilder();
        int count = random.Next(minimumLength, 26);
        for (int i = 0; i < count; i++)
        {
            builder.Append(alphabet[random.Next(alphabet.Length)]);
        }

        return builder.ToString();
    }

    private static List<StyleSpan> RandomSpans(Random random, int textLength)
    {
        var spans = new List<StyleSpan>();
        int count = random.Next(0, 5);
        for (int i = 0; i < count; i++)
        {
            int a = random.Next(textLength + 1);
            int b = random.Next(textLength + 1);
            spans.Add(new StyleSpan(Math.Min(a, b), Math.Max(a, b), (TextStyleKind)random.Next(4)));
        }

        return spans;
    }

    // Offsets that do not fall between the two halves of an emoji.
    private static List<int> Boundaries(string text)
    {
        var list = new List<int>();
        for (int i = 0; i <= text.Length; i++)
        {
            bool insidePair = i > 0 && i < text.Length && char.IsLowSurrogate(text[i]) && char.IsHighSurrogate(text[i - 1]);
            if (!insidePair)
            {
                list.Add(i);
            }
        }

        return list;
    }
}
