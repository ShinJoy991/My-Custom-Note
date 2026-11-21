using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using MyCustomNote.Core.Formatting;

namespace MyCustomNote.Editor;

/// <summary>
/// RichTextBox that edits "plain text + style spans". The document is the single source of truth:
/// the model is read from it on demand (<see cref="GetModel"/>), never kept in a second copy.
/// Style changes are computed by the tested Core logic and then applied to the existing document
/// in place, so the control's own undo and redo keep working.
/// </summary>
public sealed class SpanEditor : RichTextBox
{
    public static readonly RoutedUICommand ToggleStrikethroughCommand =
        new("Strikethrough", nameof(ToggleStrikethroughCommand), typeof(SpanEditor));

    public SpanEditor(bool singleLine)
    {
        SingleLine = singleLine;
        AcceptsReturn = !singleLine;
        AcceptsTab = false;
        Padding = new Thickness(6);
        VerticalScrollBarVisibility = singleLine ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;

        BindToggle(EditingCommands.ToggleBold, TextStyleKind.Bold);
        BindToggle(EditingCommands.ToggleItalic, TextStyleKind.Italic);
        BindToggle(EditingCommands.ToggleUnderline, TextStyleKind.Underline);
        BindToggle(ToggleStrikethroughCommand, TextStyleKind.Strikethrough);
        InputBindings.Add(new KeyBinding(ToggleStrikethroughCommand, Key.X, ModifierKeys.Control | ModifierKeys.Shift));

        // Built-in formatting commands we do not support are switched off, so that no shortcut can change
        // formatting that would never be saved: alignment, font size, sub/superscript, indentation.
        // The list commands stay on in the body (WPF's own Enter and Backspace handling in lists is what we
        // want) and are off in the one-line title.
        var disabled = new List<RoutedCommand>
        {
            EditingCommands.AlignCenter, EditingCommands.AlignJustify, EditingCommands.AlignLeft, EditingCommands.AlignRight,
            EditingCommands.IncreaseFontSize, EditingCommands.DecreaseFontSize,
            EditingCommands.ToggleSubscript, EditingCommands.ToggleSuperscript,
            EditingCommands.IncreaseIndentation, EditingCommands.DecreaseIndentation
        };
        if (singleLine)
        {
            disabled.Add(EditingCommands.ToggleBullets);
            disabled.Add(EditingCommands.ToggleNumbering);
        }

        foreach (var command in disabled)
        {
            CommandBindings.Add(new CommandBinding(
                command,
                (_, e) => e.Handled = true,
                (_, e) => { e.CanExecute = false; e.Handled = true; }));
        }

        // Shift+Enter would insert a LineBreak; treat it as a normal paragraph break instead.
        CommandBindings.Add(new CommandBinding(
            EditingCommands.EnterLineBreak,
            (_, e) =>
            {
                if (!SingleLine)
                {
                    EditingCommands.EnterParagraphBreak.Execute(null, this);
                }

                e.Handled = true;
            },
            (_, e) => { e.CanExecute = !SingleLine; e.Handled = true; }));

        CommandManager.AddPreviewExecutedHandler(this, OnPreviewCommand);

        DataObject.AddPastingHandler(this, OnPasting);

        // Paragraphs and lists that WPF creates itself (Enter, the list commands) get WPF's default spacing
        // between paragraphs. The ones this class builds have none, so give the others none as well.
        Resources.Add(typeof(Paragraph), new Style(typeof(Paragraph))
        {
            Setters = { new Setter(Block.MarginProperty, new Thickness(0)) }
        });
        Resources.Add(typeof(List), new Style(typeof(List))
        {
            Setters =
            {
                new Setter(Block.MarginProperty, new Thickness(0)),
                new Setter(Block.PaddingProperty, new Thickness(24, 0, 0, 0))
            }
        });

        SetModel("", []);
    }

    public bool SingleLine { get; }

    /// <summary>Replaces the whole content. Clears undo history.</summary>
    public void SetModel(string text, IEnumerable<StyleSpan> spans, IEnumerable<ParagraphFormat>? paragraphs = null)
    {
        text = SingleLine
            ? text.Replace('\r', ' ').Replace('\n', ' ')
            : text.Replace("\r\n", "\n").Replace('\r', '\n');

        // A Heading line is a paragraph with a larger font size (and no bold, so it is not read back as a Bold span).
        var kinds = ParagraphFormats.Normalize(paragraphs ?? Array.Empty<ParagraphFormat>(), text)
            .ToDictionary(f => f.Line, f => f.Kind);

        var document = new FlowDocument();
        List? currentList = null;
        var currentKind = ParagraphKind.Normal;
        string[] lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var paragraph = new Paragraph { Margin = new Thickness(0) };
            if (lines[i].Length > 0)
            {
                paragraph.Inlines.Add(new Run(lines[i]));
            }

            var kind = kinds.GetValueOrDefault(i, ParagraphKind.Normal);
            if (kind is ParagraphKind.Bullet or ParagraphKind.Numbered)
            {
                if (currentList is null || currentKind != kind)
                {
                    currentList = new List
                    {
                        MarkerStyle = kind == ParagraphKind.Numbered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                        Margin = new Thickness(0),
                        Padding = new Thickness(24, 0, 0, 0)
                    };
                    currentKind = kind;
                    document.Blocks.Add(currentList);
                }

                currentList.ListItems.Add(new ListItem(paragraph));
            }
            else
            {
                currentList = null;
                currentKind = ParagraphKind.Normal;
                if (kind == ParagraphKind.Heading)
                {
                    paragraph.FontSize = DocumentMap.HeadingFontSize;
                    paragraph.SetResourceReference(TextElement.ForegroundProperty, HeadingBrushKey);
                }

                document.Blocks.Add(paragraph);
            }
        }

        Document = document;
        ApplySpans(spans, clearFirst: false);

        IsUndoEnabled = false;
        IsUndoEnabled = true;
    }

    /// <summary>The current text and canonical spans, read from the document.</summary>
    public (string Text, IReadOnlyList<StyleSpan> Spans) GetModel()
    {
        var map = DocumentMap.Build(Document);
        return (map.Text, map.ReadSpans());
    }

    public (int Start, int End) GetSelectionOffsets()
    {
        var map = DocumentMap.Build(Document);
        return (map.OffsetOf(Selection.Start), map.OffsetOf(Selection.End));
    }

    public void SelectOffsets(int start, int end)
    {
        var map = DocumentMap.Build(Document);
        Selection.Select(map.PointerAt(start), map.PointerAt(end));
    }

    /// <summary>Toolbar state for the current selection.</summary>
    public bool IsStyled(TextStyleKind kind)
    {
        var map = DocumentMap.Build(Document);
        int start = map.OffsetOf(Selection.Start);
        int end = map.OffsetOf(Selection.End);
        if (start == end)
        {
            // Nothing selected: the button shows the style the next typed text will get.
            return (TypingFlags() & DocumentMap.FlagOf(kind)) != 0;
        }

        return StyleSpans.IsStyled(map.ReadSpans(), map.Text, start, end, kind);
    }

    /// <summary>The paragraph kinds (list items) of the document, one entry per line that is not Normal.</summary>
    public IReadOnlyList<ParagraphFormat> GetParagraphFormats() => DocumentMap.Build(Document).ReadParagraphs();

    /// <summary>The number WPF shows on each line (0 for lines that are not numbered).</summary>
    public IReadOnlyList<int> GetDisplayedNumbers() => DocumentMap.Build(Document).DisplayedNumbers();

    /// <summary>Toolbar state: true when every line the selection touches is of this kind.</summary>
    public bool IsParagraphKind(ParagraphKind kind)
    {
        var map = DocumentMap.Build(Document);
        var (first, last) = ParagraphFormats.LinesTouched(map.Text, map.OffsetOf(Selection.Start), map.OffsetOf(Selection.End));
        var kinds = map.ReadParagraphs().ToDictionary(f => f.Line, f => f.Kind);
        for (int line = first; line <= last; line++)
        {
            if (kinds.GetValueOrDefault(line, ParagraphKind.Normal) != kind)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Toolbar action for lists: WPF's own list commands do the work (they apply to every paragraph the
    /// selection touches, are undoable, and give the Word-like Enter and Backspace behaviour in lists).
    /// WPF's command does nothing when the touched lines are a mix (some in a list, some not, or lists of
    /// different kinds), so for a mix the lines are first taken out of their lists and then the command is
    /// run once on the whole selection; all of it is one undo step.
    /// The Core function <see cref="ParagraphFormats.Toggle"/> is the specification the self-check compares them with.
    /// </summary>
    public void ToggleParagraph(ParagraphKind kind)
    {
        if (SingleLine || kind == ParagraphKind.Normal)
        {
            return;
        }

        var map = DocumentMap.Build(Document);
        int selectionStart = map.OffsetOf(Selection.Start);
        int selectionEnd = map.OffsetOf(Selection.End);
        var (first, last) = ParagraphFormats.LinesTouched(map.Text, selectionStart, selectionEnd);
        var kinds = map.Paragraphs.Select(p => p.Kind).ToList();
        last = Math.Min(last, kinds.Count - 1);

        bool allNormal = true;
        bool allTarget = true;
        for (int line = first; line <= last; line++)
        {
            allNormal &= kinds[line] == ParagraphKind.Normal;
            allTarget &= kinds[line] == kind;
        }

        BeginChange();
        try
        {
            if (kind == ParagraphKind.Heading)
            {
                if (!allTarget)
                {
                    ClearTouched(first, last, kinds); // a line cannot be a heading and a list item at once
                }

                SetHeading(first, last, !allTarget);
                SelectOffsets(selectionStart, selectionEnd);
                return;
            }

            if (allNormal || allTarget)
            {
                RunListCommand(kind); // WPF handles these two cases on its own
                MergeAdjacentLists();
                SelectOffsets(selectionStart, selectionEnd);
                return;
            }

            ClearTouched(first, last, kinds);

            SelectOffsets(selectionStart, selectionEnd);
            RunListCommand(kind); // every touched line is Normal now, so this puts them all in one list
            MergeAdjacentLists();
            SelectOffsets(selectionStart, selectionEnd);
        }
        finally
        {
            EndChange();
        }
    }

    /// <summary>
    /// WPF puts a newly listed line into a list of its own, even when the line above or below is already in a
    /// list of the same kind (then the numbers would restart at 1). The Core model says consecutive lines of one
    /// kind are one list, so neighbouring lists of the same marker style are joined here.
    /// </summary>
    private void MergeAdjacentLists()
    {
        Block? block = Document.Blocks.FirstBlock;
        while (block is not null)
        {
            if (block is List list && list.NextBlock is List next && list.MarkerStyle == next.MarkerStyle)
            {
                foreach (var item in next.ListItems.ToList())
                {
                    next.ListItems.Remove(item);
                    list.ListItems.Add(item);
                }

                Document.Blocks.Remove(next);
                continue; // the joined list may now touch another list
            }

            block = block.NextBlock;
        }
    }

    /// <summary>Makes every line from first to last Normal: list lines leave their lists, headings lose the larger font.</summary>
    private void ClearTouched(int first, int last, IReadOnlyList<ParagraphKind> kinds)
    {
        int line = first;
        while (line <= last)
        {
            var current = kinds[line];
            if (current == ParagraphKind.Normal)
            {
                line++;
                continue;
            }

            int runEnd = line;
            while (runEnd + 1 <= last && kinds[runEnd + 1] == current)
            {
                runEnd++;
            }

            if (current == ParagraphKind.Heading)
            {
                SetHeading(line, runEnd, false);
            }
            else
            {
                var fresh = DocumentMap.Build(Document); // each removal rebuilds the blocks; lines keep their numbers
                SelectOffsets(fresh.Paragraphs[line].Start, fresh.Paragraphs[runEnd].End);
                RunListCommand(current); // all lines of this run are one kind, so this takes them out of the list
            }

            line = runEnd + 1;
        }
    }

    private void SetHeading(int first, int last, bool on)
    {
        var map = DocumentMap.Build(Document);
        for (int line = first; line <= last && line < map.Paragraphs.Count; line++)
        {
            var paragraph = map.Paragraphs[line].Paragraph;
            if (paragraph is null)
            {
                continue;
            }

            ClearInlineFontSizes(paragraph.Inlines);

            if (on)
            {
                paragraph.FontSize = DocumentMap.HeadingFontSize;
                paragraph.SetResourceReference(TextElement.ForegroundProperty, HeadingBrushKey);
            }
            else
            {
                paragraph.ClearValue(TextElement.FontSizeProperty);
                paragraph.ClearValue(TextElement.ForegroundProperty);
            }
        }
    }

    // Text typed into a paragraph can get a font size of its own, which would beat the paragraph's size:
    // the line would be a heading in the model but look small. The size of a line is the paragraph's only.
    private static void ClearInlineFontSizes(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            inline.ClearValue(TextElement.FontSizeProperty);
            if (inline is Span span)
            {
                ClearInlineFontSizes(span.Inlines);
            }
        }
    }

    /// <summary>
    /// WPF's own Enter and Backspace are kept, except: Enter at the end of a heading starts a Normal line
    /// (WPF would copy the heading look), Enter on an empty list item starts another list item (WPF would leave
    /// the list), and Backspace at the start of a heading removes the heading and keeps the text.
    /// Backspace at the start of a list item is WPF's own: it takes the line out of the list.
    /// </summary>
    private void OnPreviewCommand(object sender, ExecutedRoutedEventArgs e)
    {
        if (SingleLine || !Selection.IsEmpty)
        {
            return; // WPF's own handling runs
        }

        if (e.Command == EditingCommands.EnterParagraphBreak && HandleEnter())
        {
            e.Handled = true;
        }
        else if (e.Command == EditingCommands.Backspace && HandleBackspaceAtHeading())
        {
            e.Handled = true;
        }
    }

    private bool HandleEnter()
    {
        var map = DocumentMap.Build(Document);
        int offset = map.OffsetOf(Selection.Start);
        var info = map.Paragraphs.FirstOrDefault(p => offset >= p.Start && offset <= p.End);
        if (info?.Paragraph is not Paragraph paragraph)
        {
            return false;
        }

        if (info.Kind == ParagraphKind.Heading && offset == info.End && paragraph.Parent is FlowDocument)
        {
            BeginChange();
            try
            {
                var created = new Paragraph { Margin = new Thickness(0) };
                Document.Blocks.InsertAfter(paragraph, created);
                CaretPosition = created.ContentStart;
            }
            finally
            {
                EndChange();
            }

            return true;
        }

        if (info.Start == info.End && paragraph.Parent is ListItem item && item.Parent is List list)
        {
            BeginChange();
            try
            {
                var created = new Paragraph { Margin = new Thickness(0) };
                list.ListItems.InsertAfter(item, new ListItem(created));
                CaretPosition = created.ContentStart;
            }
            finally
            {
                EndChange();
            }

            return true;
        }

        return false;
    }

    private bool HandleBackspaceAtHeading()
    {
        var map = DocumentMap.Build(Document);
        int offset = map.OffsetOf(Selection.Start);
        var info = map.Paragraphs.FirstOrDefault(p => p.Start == offset);
        if (info is null || info.Kind != ParagraphKind.Heading)
        {
            return false;
        }

        int line = map.Paragraphs.IndexOf(info);
        BeginChange();
        try
        {
            SetHeading(line, line, false);
        }
        finally
        {
            EndChange();
        }

        return true;
    }

    private void RunListCommand(ParagraphKind kind)
    {
        switch (kind)
        {
            case ParagraphKind.Bullet:
                EditingCommands.ToggleBullets.Execute(null, this);
                break;
            case ParagraphKind.Numbered:
                EditingCommands.ToggleNumbering.Execute(null, this);
                break;
        }
    }

    public void ToggleStyle(TextStyleKind kind)
    {
        var map = DocumentMap.Build(Document);
        int start = map.OffsetOf(Selection.Start);
        int end = map.OffsetOf(Selection.End);
        if (start == end)
        {
            // Nothing selected: "format before typing". The button changes the style of the NEXT typed text.
            ToggleTypingStyle(kind, start);
            return;
        }

        var updated = StyleSpans.Toggle(map.ReadSpans(), map.Text, start, end, kind);
        ApplySpans(updated, clearFirst: true);
        SelectOffsets(start, end);
    }

    /// <summary>Test helper: type text at an offset the way the caret would (inserts into the Run there).</summary>
    public void InsertTextAt(int offset, string text)
    {
        DocumentMap.Build(Document).PointerAt(offset).InsertTextInRun(text);
    }

    /// <summary>Test helper: delete the characters in [start, end).</summary>
    public void DeleteRange(int start, int end)
    {
        var map = DocumentMap.Build(Document);
        new TextRange(map.PointerAt(start), map.PointerAt(end)).Text = "";
    }

    /// <summary>Test helper: raise a text-input event as if the user typed <paramref name="text"/> at the caret.</summary>
    public void SimulateTyping(string text)
    {
        var composition = new TextComposition(InputManager.Current, this, text);
        var args = new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition)
        {
            RoutedEvent = TextCompositionManager.TextInputEvent
        };
        RaiseEvent(args);
    }

    // ---------- typing rule (like Word) ----------
    // Text typed at the caret takes the style of the character BEFORE the caret. WPF takes the style of the
    // character after it, which differs at the start of a styled word. At the start of a paragraph (or the
    // document) there is no character before, so WPF's own behaviour is kept. The fix is "spring-loaded"
    // formatting: it only changes what the next typed character looks like, so typing keeps WPF's normal
    // undo grouping.

    /// <summary>
    /// A heading line gets the heading color of the theme (the application resource with this key, if the host app
    /// has one; the test harness has none and keeps the default color), like the title does.
    /// </summary>
    public const string HeadingBrushKey = "Brush.HeadingText";

    private bool _aligningTypingStyle;
    private bool _fixingHeadingSizes;

    // Text typed into a heading can come out with a font size of its own (the line would be a heading in the
    // model but look small). After every change the text of heading lines gets the paragraph's size back.
    // Only a line that really has such a size is touched, so normal typing is not affected.
    protected override void OnTextChanged(TextChangedEventArgs e)
    {
        base.OnTextChanged(e);
        if (!SingleLine && !_fixingHeadingSizes)
        {
            FixHeadingFontSizes();
        }
    }

    private void FixHeadingFontSizes()
    {
        foreach (var paragraph in Document.Blocks.OfType<Paragraph>())
        {
            if (paragraph.ReadLocalValue(TextElement.FontSizeProperty) is double size
                && size == DocumentMap.HeadingFontSize
                && HasInlineFontSize(paragraph.Inlines))
            {
                _fixingHeadingSizes = true;
                try
                {
                    ClearInlineFontSizes(paragraph.Inlines);
                }
                finally
                {
                    _fixingHeadingSizes = false;
                }
            }
        }
    }

    private static bool HasInlineFontSize(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline.ReadLocalValue(TextElement.FontSizeProperty) != DependencyProperty.UnsetValue
                || (inline is Span span && HasInlineFontSize(span.Inlines)))
            {
                return true;
            }
        }

        return false;
    }

    protected override void OnSelectionChanged(RoutedEventArgs e)
    {
        base.OnSelectionChanged(e);
        if (!_aligningTypingStyle && Selection.IsEmpty)
        {
            AlignTypingStyle();
        }
    }

    // "Format before typing": with nothing selected, a style button (or Ctrl+B and so on) sets the style of the next
    // typed text. It is remembered together with the caret position; as soon as the caret is anywhere else it is dropped.
    private StyleFlags? _typingOverride;
    private int _typingOverrideOffset;

    private void ToggleTypingStyle(TextStyleKind kind, int offset)
    {
        var current = _typingOverride is { } pending && _typingOverrideOffset == offset ? pending : TypingFlags();
        var desired = current ^ DocumentMap.FlagOf(kind);
        _typingOverride = desired;
        _typingOverrideOffset = offset;
        ApplyTypingFlags(desired);
    }

    public void AlignTypingStyle()
    {
        var map = DocumentMap.Build(Document);
        int offset = map.OffsetOf(Selection.Start);

        if (_typingOverride is { } pending)
        {
            if (_typingOverrideOffset == offset)
            {
                ApplyTypingFlags(pending); // the user asked for this style for the next text; it beats Word's rule
                return;
            }

            _typingOverride = null; // the caret moved away
        }

        if (offset <= 0 || map.Text[offset - 1] == '\n')
        {
            return;
        }

        ApplyTypingFlags(FlagsAt(map.ReadSpans(), offset - 1));
    }

    // Sets what the next typed character looks like (WPF's spring-loaded formatting) to exactly these styles.
    private void ApplyTypingFlags(StyleFlags desired)
    {
        var different = desired ^ TypingFlags();
        if (different == StyleFlags.None)
        {
            return;
        }

        _aligningTypingStyle = true;
        try
        {
            if ((different & StyleFlags.Bold) != 0)
            {
                Selection.ApplyPropertyValue(
                    TextElement.FontWeightProperty, (desired & StyleFlags.Bold) != 0 ? FontWeights.Bold : FontWeights.Normal);
            }

            if ((different & StyleFlags.Italic) != 0)
            {
                Selection.ApplyPropertyValue(
                    TextElement.FontStyleProperty, (desired & StyleFlags.Italic) != 0 ? FontStyles.Italic : FontStyles.Normal);
            }

            if ((different & (StyleFlags.Underline | StyleFlags.Strikethrough)) != 0)
            {
                Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, DecorationsFor(desired));
            }
        }
        finally
        {
            _aligningTypingStyle = false;
        }
    }

    // The style the next typed character would get (WPF's value at the caret, including spring-loaded formatting).
    private StyleFlags TypingFlags()
    {
        var flags = StyleFlags.None;

        if (Selection.GetPropertyValue(TextElement.FontWeightProperty) is FontWeight weight && weight >= FontWeights.Bold)
        {
            flags |= StyleFlags.Bold;
        }

        if (Selection.GetPropertyValue(TextElement.FontStyleProperty) is FontStyle style
            && (style == FontStyles.Italic || style == FontStyles.Oblique))
        {
            flags |= StyleFlags.Italic;
        }

        if (Selection.GetPropertyValue(Inline.TextDecorationsProperty) is TextDecorationCollection decorations)
        {
            foreach (TextDecoration decoration in decorations)
            {
                if (decoration.Location == TextDecorationLocation.Underline)
                {
                    flags |= StyleFlags.Underline;
                }
                else if (decoration.Location == TextDecorationLocation.Strikethrough)
                {
                    flags |= StyleFlags.Strikethrough;
                }
            }
        }

        return flags;
    }

    private static StyleFlags FlagsAt(IReadOnlyList<StyleSpan> spans, int index)
    {
        var flags = StyleFlags.None;
        foreach (var span in spans)
        {
            if (span.Start <= index && index < span.End)
            {
                flags |= DocumentMap.FlagOf(span.Kind);
            }
        }

        return flags;
    }

    private void ApplySpans(IEnumerable<StyleSpan> spans, bool clearFirst)
    {
        string text = DocumentMap.Build(Document).Text;
        var canonical = StyleSpans.Normalize(spans, text);

        BeginChange();
        try
        {
            if (clearFirst)
            {
                var all = new TextRange(Document.ContentStart, Document.ContentEnd);
                all.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
                all.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Normal);
                all.ApplyPropertyValue(Inline.TextDecorationsProperty, new TextDecorationCollection());
            }

            foreach (var (start, end, flags) in Segments(canonical))
            {
                // A fresh map for every segment: each ApplyPropertyValue splits Runs.
                var map = DocumentMap.Build(Document);
                var range = new TextRange(map.PointerAt(start), map.PointerAt(end));

                if ((flags & StyleFlags.Bold) != 0)
                {
                    range.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Bold);
                }

                if ((flags & StyleFlags.Italic) != 0)
                {
                    range.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Italic);
                }

                if ((flags & (StyleFlags.Underline | StyleFlags.Strikethrough)) != 0)
                {
                    range.ApplyPropertyValue(Inline.TextDecorationsProperty, DecorationsFor(flags));
                }
            }
        }
        finally
        {
            EndChange();
        }
    }

    // Splits the text at every span edge; each returned piece has one fixed set of styles.
    private static IEnumerable<(int Start, int End, StyleFlags Flags)> Segments(IReadOnlyList<StyleSpan> spans)
    {
        var points = spans.SelectMany(s => new[] { s.Start, s.End }).Distinct().OrderBy(p => p).ToList();
        for (int i = 0; i + 1 < points.Count; i++)
        {
            int a = points[i];
            int b = points[i + 1];
            var flags = StyleFlags.None;
            foreach (var span in spans)
            {
                if (span.Start <= a && span.End >= b)
                {
                    flags |= DocumentMap.FlagOf(span.Kind);
                }
            }

            if (flags != StyleFlags.None)
            {
                yield return (a, b, flags);
            }
        }
    }

    private static TextDecorationCollection DecorationsFor(StyleFlags flags)
    {
        var collection = new TextDecorationCollection();
        if ((flags & StyleFlags.Underline) != 0)
        {
            foreach (TextDecoration decoration in System.Windows.TextDecorations.Underline)
            {
                collection.Add(decoration);
            }
        }

        if ((flags & StyleFlags.Strikethrough) != 0)
        {
            foreach (TextDecoration decoration in System.Windows.TextDecorations.Strikethrough)
            {
                collection.Add(decoration);
            }
        }

        return collection;
    }

    private void BindToggle(RoutedCommand command, TextStyleKind kind)
    {
        CommandBindings.Add(new CommandBinding(
            command,
            (_, e) => { ToggleStyle(kind); e.Handled = true; },
            (_, e) => { e.CanExecute = true; e.Handled = true; }));
    }

    // Paste as plain text only: foreign fonts, colors and sizes never enter the document.
    private void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        string? text = e.DataObject.GetDataPresent(DataFormats.UnicodeText)
            ? e.DataObject.GetData(DataFormats.UnicodeText) as string
            : null;

        e.CancelCommand();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        text = SingleLine ? text.Replace('\n', ' ') : text.Replace("\n", "\r\n");

        Selection.Text = text;
        Selection.Select(Selection.End, Selection.End);
    }
}
