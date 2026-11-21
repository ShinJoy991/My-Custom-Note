using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using MyCustomNote.Core.Formatting;
using MyCustomNote.Core.Models;
using MyCustomNote.Core.Security;
using MyCustomNote.Data;
using MyCustomNote.Editor;

namespace MyCustomNote;

/// <summary>
/// The note editor: formatting toolbar, one-line title, body, word count and autosave.
/// The two <see cref="SpanEditor"/>s hold the only copy of the text while a note is open. Autosave reads them
/// 0.7 seconds after the last change, and only writes if the content really differs from what was saved last.
/// Saving is done on the UI thread, one call at a time, so two saves can never overlap.
/// </summary>
public partial class NoteEditorView : UserControl
{
    private readonly SpanEditor _title = new(singleLine: true);
    private readonly SpanEditor _body = new(singleLine: false);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(700) };

    private SpanEditor _active;
    private NoteRepository? _notes;
    private TagRepository? _tags;
    private ChecklistRepository? _checklists;
    private Func<VaultKey?> _notesKey = () => null;
    private IReadOnlyList<(string Text, bool IsChecked)> _savedChecklist = [];
    private long? _noteId;
    private NoteContent? _saved;
    private bool _loading;
    private bool _saving;
    private bool _toolbarRefreshPending;

    public NoteEditorView()
    {
        InitializeComponent();
        _active = _body;

        Configure(_title, TitleHost, fontSize: 24, foregroundKey: "Brush.HeadingText");
        Configure(_body, BodyHost, fontSize: 15, foregroundKey: "Brush.BodyText");

        _timer.Tick += (_, _) =>
        {
            UpdateStats();
            Flush();
        };

        UpdatePlaceholders();
        UpdateStats();
    }

    /// <summary>Raised after a change was written to the database (the list then shows the new modified time).</summary>
    public event EventHandler? ContentSaved;

    /// <summary>Raised after a tag was added to or removed from the note (the left pane and the list show tags).</summary>
    public event EventHandler? TagsChanged;

    public void Initialize(NoteRepository notes, TagRepository tags, ChecklistRepository checklists, Func<VaultKey?> notesKey)
    {
        _notes = notes;
        _tags = tags;
        _checklists = checklists;
        _notesKey = notesKey;
    }

    /// <summary>Shows a note (or nothing, for null). A pending change of the note shown before is saved first.</summary>
    public void Load(Note? note)
    {
        // The list is rebuilt after every save and hands us the same note again: keep what the user is typing.
        if (note is not null && note.Id == _noteId)
        {
            return;
        }

        Flush();

        // Saving makes the list rebuild, which may already have shown this very note again.
        if (note is not null && note.Id == _noteId)
        {
            return;
        }

        _noteId = note?.Id;

        _loading = true;
        try
        {
            _title.SetModel(note?.Title ?? "", note?.TitleStyles ?? []);
            _body.SetModel(note?.Body ?? "", note?.BodyStyles ?? [], note?.Paragraphs);
            LoadChecklist(note);
        }
        finally
        {
            _loading = false;
        }

        // What the editor reads back is the baseline: an edit that changes nothing is never written.
        _saved = note is null ? null : ReadContent();
        _savedChecklist = ReadChecklist();
        RefreshTags();

        // A note in the trash can be read but not changed (restore it first).
        bool readOnly = note?.IsTrashed == true;
        _title.IsReadOnly = _body.IsReadOnly = readOnly;
        ToolbarBorder.IsEnabled = TagPanel.IsEnabled = ChecklistBorder.IsEnabled = !readOnly;
        SaveText.Text = "";
        UpdatePlaceholders();
        UpdateStats();
        ScheduleToolbarRefresh();

        if (note is not null && note.Title.Length == 0 && note.Body.Length == 0)
        {
            Dispatcher.InvokeAsync(() => _title.Focus(), DispatcherPriority.Input);
        }
    }

    /// <summary>Writes a pending change now (Ctrl+S, switching notes, Esc, closing the window).</summary>
    public void Flush()
    {
        _timer.Stop();

        if (_notes is null || _noteId is not { } id || _saved is null || _saving)
        {
            return;
        }

        var current = ReadContent();
        var checklist = ReadChecklist();
        bool checklistChanged = !checklist.SequenceEqual(_savedChecklist);
        if (current.SameAs(_saved) && !checklistChanged)
        {
            return;
        }

        _saving = true;
        try
        {
            if (checklistChanged)
            {
                _checklists!.ReplaceItems(id, checklist.Select(i => new ChecklistItem(0, id, i.Text, i.IsChecked)), _notesKey());
                _savedChecklist = checklist;
            }

            // Also written when only the checklist changed: that sets the modified time.
            // A locked note is written encrypted, which needs the key; without it nothing is written.
            if (_notes.UpdateContent(id, current, _notesKey()))
            {
                _saved = current;
                SaveText.Text = "Saved";
            }
            else
            {
                SaveText.Text = "Not saved";
                return;
            }
        }
        catch (Exception ex) when (ex is SqliteException or IOException or InvalidOperationException)
        {
            // The text stays in the editor and the next edit or Ctrl+S tries again.
            SaveText.Text = "Not saved";
            return;
        }
        finally
        {
            _saving = false;
        }

        ContentSaved?.Invoke(this, EventArgs.Empty);
    }

    private NoteContent ReadContent()
    {
        var (title, titleStyles) = _title.GetModel();
        var (body, bodyStyles) = _body.GetModel();
        return new NoteContent(title, titleStyles, body, bodyStyles, _body.GetParagraphFormats());
    }

    // ---------- tags ----------

    private void RefreshTags()
    {
        TagPanel.Children.Clear();
        if (_tags is null || _noteId is not { } id)
        {
            return;
        }

        foreach (var tag in _tags.GetForNote(id))
        {
            long tagId = tag.Id;
            var chip = new Button
            {
                Content = $"#{tag.Name}  \u00d7",
                ToolTip = "Click to remove this tag",
                Margin = new Thickness(0, 0, 6, 4),
                Padding = new Thickness(8, 2, 8, 2),
                Focusable = false,
            };
            chip.Click += (_, _) =>
            {
                _tags.RemoveFromNote(id, tagId);
                RefreshTags();
                TagsChanged?.Invoke(this, EventArgs.Empty);
            };
            TagPanel.Children.Add(chip);
        }

        var add = new Button { Content = "+ Tag", Margin = new Thickness(0, 0, 6, 4), Padding = new Thickness(8, 2, 8, 2), Focusable = false };
        add.Click += (_, _) =>
        {
            string? name = PromptWindow.Ask(Window.GetWindow(this), "Add tag", "Tag name:");
            if (name is null)
            {
                return;
            }

            _tags.AddToNote(id, _tags.Create(name));
            RefreshTags();
            TagsChanged?.Invoke(this, EventArgs.Empty);
        };
        TagPanel.Children.Add(add);
    }

    // ---------- checklist ----------

    private void LoadChecklist(Note? note)
    {
        ChecklistPanel.Children.Clear();
        foreach (var item in note?.ChecklistItems ?? [])
        {
            AddChecklistRow(item.Text, item.IsChecked);
        }
    }

    private void AddChecklistItem_Click(object sender, RoutedEventArgs e) => AddChecklistRow("", false, focus: true);

    private void AddChecklistRow(string text, bool isChecked, bool focus = false)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        var check = new CheckBox { IsChecked = isChecked, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 6, 0) };
        var remove = new Button { Content = "\u00d7", Padding = new Thickness(8, 0, 8, 0), Focusable = false, ToolTip = "Remove", VerticalAlignment = VerticalAlignment.Top };
        // A long item wraps onto more lines instead of running past the edge; the check box and the remove button stay at the top.
        var box = new TextBox { Text = text, Padding = new Thickness(4, 3, 4, 3), BorderThickness = new Thickness(0), Background = Brushes.Transparent, TextWrapping = TextWrapping.Wrap };

        DockPanel.SetDock(check, Dock.Left);
        DockPanel.SetDock(remove, Dock.Right);
        row.Children.Add(check);
        row.Children.Add(remove);
        row.Children.Add(box); // the last child fills the rest

        check.Click += (_, _) => OnEdited();
        box.TextChanged += (_, _) => OnEdited();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                AddChecklistRow("", false, focus: true);
                e.Handled = true;
            }
        };
        remove.Click += (_, _) =>
        {
            ChecklistPanel.Children.Remove(row);
            OnEdited();
        };

        ChecklistPanel.Children.Add(row);
        if (focus)
        {
            box.Focus();
            OnEdited();
        }
    }

    // Rows with no text are not stored.
    private IReadOnlyList<(string Text, bool IsChecked)> ReadChecklist()
    {
        var items = new List<(string, bool)>();
        foreach (DockPanel row in ChecklistPanel.Children)
        {
            var check = (CheckBox)row.Children[0];
            var box = (TextBox)row.Children[2];
            string text = box.Text.Trim();
            if (text.Length > 0)
            {
                items.Add((text, check.IsChecked == true));
            }
        }

        return items;
    }

    // ---------- the two editors ----------

    private void Configure(SpanEditor editor, Grid host, double fontSize, string foregroundKey)
    {
        editor.BorderThickness = new Thickness(0);
        editor.Background = Brushes.Transparent;
        editor.FontSize = fontSize;
        editor.SetResourceReference(FontFamilyProperty, "Font.Note");
        editor.SetResourceReference(ForegroundProperty, foregroundKey);

        // Index 0: below the placeholder text, which must stay visible on top of the empty editor.
        host.Children.Insert(0, editor);

        editor.TextChanged += (_, _) => OnEdited();
        editor.SelectionChanged += (_, _) => ScheduleToolbarRefresh();
        editor.GotKeyboardFocus += (_, _) =>
        {
            _active = editor;
            ScheduleToolbarRefresh();
        };
    }

    private void OnEdited()
    {
        if (_loading)
        {
            return;
        }

        UpdatePlaceholders();
        SaveText.Text = "";
        _timer.Stop();
        _timer.Start();
    }

    private void UpdatePlaceholders()
    {
        TitlePlaceholder.Visibility = IsEmpty(_title) ? Visibility.Visible : Visibility.Collapsed;
        BodyPlaceholder.Visibility = IsEmpty(_body) ? Visibility.Visible : Visibility.Collapsed;
    }

    // Cheap on purpose (runs on every keystroke): only a document of one empty paragraph is empty.
    private static bool IsEmpty(SpanEditor editor) =>
        editor.Document.Blocks.Count == 1
        && editor.Document.Blocks.FirstBlock is Paragraph paragraph
        && new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.Length == 0;

    private void UpdateStats()
    {
        var stats = TextStats.Count(_body.GetModel().Text);
        StatsText.Text =
            $"{stats.Words} {(stats.Words == 1 ? "word" : "words")}  ·  {stats.Characters} {(stats.Characters == 1 ? "character" : "characters")}";
    }

    // ---------- toolbar ----------

    private void Format_Click(object sender, RoutedEventArgs e)
    {
        var kind = (TextStyleKind)((FrameworkElement)sender).Tag;
        _active.ToggleStyle(kind);
        _active.Focus();
        ScheduleToolbarRefresh();
    }

    private void Paragraph_Click(object sender, RoutedEventArgs e)
    {
        var kind = (ParagraphKind)((FrameworkElement)sender).Tag;
        _active.ToggleParagraph(kind); // does nothing in the one-line title
        _active.Focus();
        ScheduleToolbarRefresh();
    }

    // Reading the toolbar state walks the whole document, so many caret moves in a row are merged into one refresh.
    private void ScheduleToolbarRefresh()
    {
        if (_toolbarRefreshPending)
        {
            return;
        }

        _toolbarRefreshPending = true;
        Dispatcher.InvokeAsync(
            () =>
            {
                _toolbarRefreshPending = false;
                RefreshToolbar();
            },
            DispatcherPriority.Background);
    }

    private void RefreshToolbar()
    {
        BoldButton.IsChecked = _active.IsStyled(TextStyleKind.Bold);
        ItalicButton.IsChecked = _active.IsStyled(TextStyleKind.Italic);
        UnderlineButton.IsChecked = _active.IsStyled(TextStyleKind.Underline);
        StrikeButton.IsChecked = _active.IsStyled(TextStyleKind.Strikethrough);

        bool paragraphsAllowed = !_active.SingleLine;
        HeadingButton.IsEnabled = BulletButton.IsEnabled = NumberedButton.IsEnabled = paragraphsAllowed;
        HeadingButton.IsChecked = paragraphsAllowed && _active.IsParagraphKind(ParagraphKind.Heading);
        BulletButton.IsChecked = paragraphsAllowed && _active.IsParagraphKind(ParagraphKind.Bullet);
        NumberedButton.IsChecked = paragraphsAllowed && _active.IsParagraphKind(ParagraphKind.Numbered);
    }
}
