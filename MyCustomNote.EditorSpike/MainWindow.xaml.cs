using System.Windows;
using System.Windows.Controls;
using MyCustomNote.Core.Formatting;
using MyCustomNote.Editor;

namespace MyCustomNote.EditorSpike;

public partial class MainWindow : Window
{
    private readonly SpanEditor _title = new(singleLine: true);
    private readonly SpanEditor _body = new(singleLine: false);
    private SpanEditor _active;

    public MainWindow()
    {
        InitializeComponent();

        _active = _body;
        TitleHost.Child = _title;
        BodyHost.Child = _body;

        foreach (var editor in new[] { _title, _body })
        {
            editor.GotKeyboardFocus += (_, _) => { _active = editor; RefreshPanels(); };
            editor.SelectionChanged += (_, _) => { if (editor == _active) RefreshPanels(); };
            editor.TextChanged += (_, _) => { if (editor == _active) RefreshPanels(); };
        }

        LoadSample();
    }

    private void LoadSample()
    {
        _title.SetModel("Shopping list", [new StyleSpan(0, 8, TextStyleKind.Bold)]);

        // Lines: 0 "Buy milk and eggs.", 1 "Call mom today.", 2 "", 3 "Emoji test...", 4 "",
        //        5 "Milk" (bullet), 6 "Eggs" (bullet), 7 "", 8 "First" (numbered), 9 "Second" (numbered), 10 "A heading" (heading)
        _body.SetModel(
            "Buy milk and eggs.\nCall mom today.\n\nEmoji test: \U0001F600 stays in one piece?\n\nMilk\nEggs\n\nFirst\nSecond\nA heading",
            [
                new StyleSpan(0, 3, TextStyleKind.Bold),
                new StyleSpan(9, 13, TextStyleKind.Italic),
                new StyleSpan(19, 23, TextStyleKind.Underline),
                new StyleSpan(24, 27, TextStyleKind.Strikethrough)
            ],
            [
                new ParagraphFormat(5, ParagraphKind.Bullet),
                new ParagraphFormat(6, ParagraphKind.Bullet),
                new ParagraphFormat(8, ParagraphKind.Numbered),
                new ParagraphFormat(9, ParagraphKind.Numbered),
                new ParagraphFormat(10, ParagraphKind.Heading)
            ]);
        RefreshPanels();
    }

    private void RefreshPanels()
    {
        BoldButton.IsChecked = _active.IsStyled(TextStyleKind.Bold);
        ItalicButton.IsChecked = _active.IsStyled(TextStyleKind.Italic);
        UnderlineButton.IsChecked = _active.IsStyled(TextStyleKind.Underline);
        StrikeButton.IsChecked = _active.IsStyled(TextStyleKind.Strikethrough);
        HeadingButton.IsChecked = _active.IsParagraphKind(ParagraphKind.Heading);
        BulletButton.IsChecked = _active.IsParagraphKind(ParagraphKind.Bullet);
        NumberedButton.IsChecked = _active.IsParagraphKind(ParagraphKind.Numbered);

        var (text, spans) = _active.GetModel();
        var (selectionStart, selectionEnd) = _active.GetSelectionOffsets();
        var paragraphs = _active.GetParagraphFormats();
        var numbers = _active.GetDisplayedNumbers();
        LiveBox.Text =
            $"field: {(_active == _title ? "title" : "body")}\r\n" +
            $"selection: ({selectionStart}, {selectionEnd})\r\n" +
            $"text ({text.Length} chars): \"{text.Replace("\n", "\\n")}\"\r\n" +
            "spans:\r\n" +
            (spans.Count == 0
                ? "  (none)"
                : string.Join("\r\n", spans.Select(s => $"  {s.Kind} [{s.Start}, {s.End})"))) +
            "\r\nparagraph kinds (line: kind):\r\n" +
            (paragraphs.Count == 0
                ? "  (none)"
                : string.Join("\r\n", paragraphs.Select(p => $"  {p.Line}: {p.Kind}"))) +
            $"\r\nnumbers shown by WPF per line: [{string.Join(", ", numbers)}]";
    }

    private void Format_Click(object sender, RoutedEventArgs e)
    {
        var kind = (TextStyleKind)((FrameworkElement)sender).Tag;
        _active.ToggleStyle(kind);
        _active.Focus();
        RefreshPanels();
    }

    private void Paragraph_Click(object sender, RoutedEventArgs e)
    {
        var kind = (ParagraphKind)((FrameworkElement)sender).Tag;
        _active.ToggleParagraph(kind);
        _active.Focus();
        RefreshPanels();
    }

    private void LoadSample_Click(object sender, RoutedEventArgs e) => LoadSample();

    private void SelfCheck_Click(object sender, RoutedEventArgs e)
    {
        SelfCheckBox.Text = "Running...";
        Tabs.SelectedIndex = 1;
        SelfCheckBox.Text = SelfCheckRunner.RunAll(_body).Replace("\n", "\r\n");
        LoadSample(); // the undo, typing and list tests replaced the body content
        RefreshPanels();
    }
}
