using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using MyCustomNote.Core.IO;
using MyCustomNote.Data;
using MyCustomNote.Core.Models;
using MyCustomNote.Services;
using MyCustomNote.ViewModels;

namespace MyCustomNote;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly SettingsService _settings;
    private readonly SecurityService _security;

    // App lock: the window content is hidden and a PIN is asked after minimizing and after a time without input.
    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private DateTime _lastInput = DateTime.UtcNow;
    private bool _appLocked;
    private bool _promptingUnlock;

    internal MainWindow(MainViewModel viewModel, SettingsService settings, SecurityService security, NoteRepository notes, TagRepository tags, ChecklistRepository checklists)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settings = settings;
        _security = security;
        DataContext = viewModel;
        _viewModel.NotesPinPrompt = () => PinDialogs.AskNotesPin(this, _security);

        PreviewMouseMove += (_, _) => _lastInput = DateTime.UtcNow;
        PreviewMouseDown += (_, _) => _lastInput = DateTime.UtcNow;
        PreviewKeyDown += (_, _) => _lastInput = DateTime.UtcNow;
        _idleTimer.Tick += (_, _) =>
        {
            int minutes = _security.Data.LockAfterMinutes;
            if (!_appLocked && _security.HasAppPin && minutes > 0 && DateTime.UtcNow - _lastInput > TimeSpan.FromMinutes(minutes))
            {
                LockApp();
            }
        };
        _idleTimer.Start();
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
            {
                LockApp();
            }
            else if (_appLocked)
            {
                PromptUnlock();
            }
        };
        SearchBox.KeyDown += SearchBox_KeyDown;

        // The editor shows the selected note. It tells us when it saved (the list then shows the new modified
        // time), and we tell it when the selection is final; during a rebuild of the list the selection flickers.
        NoteEditor.Initialize(notes, tags, checklists, () => _security.NotesKey);
        NoteEditor.ContentSaved += (_, _) => _viewModel.RefreshAfterSave();
        NoteEditor.TagsChanged += (_, _) =>
        {
            _viewModel.LoadNav();
            _viewModel.RefreshAfterSave();
        };
        _viewModel.NotesReloaded += (_, _) => SyncEditor();
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedNote) && !_viewModel.IsReloading)
            {
                SyncEditor();
            }
        };
        Deactivated += (_, _) => NoteEditor.Flush();
        SyncEditor();

        BuildSortChips();
        NoteList.PreviewMouseRightButtonDown += RememberRightClickedItem;
        NavList.PreviewMouseRightButtonDown += RememberRightClickedItem;
        NoteList.AllowDrop = true;
        NoteList.PreviewMouseLeftButtonDown += NoteList_PreviewMouseLeftButtonDown;
        NoteList.PreviewMouseMove += NoteList_PreviewMouseMove;
        NoteList.Drop += NoteList_Drop;
        NoteList.PreviewKeyDown += NoteList_PreviewKeyDown;
        NoteList.SelectionChanged += (_, _) => UpdateExportButton();
        _viewModel.NotesReloaded += (_, _) => UpdateExportButton();
        UpdateExportButton();
        AllowDrop = true; // files dropped on the window are imported
        DragOver += Window_DragOver;
        Drop += Window_Drop;

        RestoreGeometry();

        // Headers ("Folders", "Tags") and the "No folders yet" lines are shown in the list but cannot be selected.
        NavList.ItemContainerGenerator.StatusChanged += (_, _) =>
        {
            if (NavList.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated)
            {
                UpdateNavContainers();
            }
        };
    }

    // ---------- drag and drop (manual order) ----------

    private Point _dragStart;
    private NoteListItem? _dragItem;

    private NoteListItem? NoteUnder(object originalSource) =>
        originalSource is DependencyObject source
        && ItemsControl.ContainerFromElement(NoteList, source) is ListBoxItem container
            ? container.DataContext as NoteListItem
            : null;

    private void NoteList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragItem = NoteUnder(e.OriginalSource);
    }

    // Dragging a note changes the order only in the Manual sort (other sorts decide the order themselves).
    private void NoteList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem is null || !_viewModel.IsManualSort)
        {
            return;
        }

        var moved = e.GetPosition(null) - _dragStart;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var item = _dragItem;
        _dragItem = null;
        DragDrop.DoDragDrop(NoteList, new DataObject(typeof(NoteListItem), item), DragDropEffects.Move);
    }

    private void NoteList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(NoteListItem)) is not NoteListItem dragged || NoteUnder(e.OriginalSource) is not { } target)
        {
            return;
        }

        int index = _viewModel.Notes.IndexOf(target);
        if (index >= 0)
        {
            Act(() => _viewModel.MoveTo(dragged, index));
        }
    }

    private void SyncEditor() => NoteEditor.Load(_viewModel.SelectedNote?.Note);

    // ---------- app lock and the Hidden screen ----------

    /// <summary>Hides the window content, forgets the notes key and asks for the app PIN (after minimizing, or a time without input).</summary>
    private void LockApp()
    {
        if (_appLocked || !_security.HasAppPin)
        {
            return;
        }

        _appLocked = true;
        NoteEditor.Flush();
        _viewModel.LeaveSecureViews();
        ((UIElement)Content).Visibility = Visibility.Collapsed;
        if (WindowState != WindowState.Minimized)
        {
            PromptUnlock();
        }
    }

    private void PromptUnlock()
    {
        if (_promptingUnlock)
        {
            return;
        }

        _promptingUnlock = true;
        try
        {
            if (PinDialogs.UnlockApp(this, _security) is not null)
            {
                _appLocked = false;
                ((UIElement)Content).Visibility = Visibility.Visible;
                _lastInput = DateTime.UtcNow;
            }
            else
            {
                Application.Current.Shutdown();
            }
        }
        finally
        {
            _promptingUnlock = false;
        }
    }

    // Typing 786 in the search box and pressing Enter opens the Hidden screen. There is no menu entry for it.
    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || SearchBox.Text.Trim() != "786")
        {
            return;
        }

        e.Handled = true;
        _viewModel.ClearSearchCommand.Execute(null);
        NoteEditor.Flush();
        if (!_viewModel.IsHiddenView && PinDialogs.EnsureNotesKey(this, _security))
        {
            _viewModel.OpenHidden();
        }
    }

    private void UpdateNavContainers()
    {
        foreach (var item in NavList.Items)
        {
            if (item is NavItem nav && NavList.ItemContainerGenerator.ContainerFromItem(nav) is ListBoxItem container)
            {
                container.IsEnabled = nav.IsSelectable;
                container.Focusable = nav.IsSelectable;
            }
        }
    }

    private void Find_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void Save_Executed(object sender, ExecutedRoutedEventArgs e) => NoteEditor.Flush();

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import notes",
            Multiselect = true,
            Filter = "Notes (*.md;*.txt;*.json)|*.md;*.txt;*.json|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        ImportFiles(dialog.FileNames);
    }

    // Used by the Import button and by dropping files (or a folder of files) on the window.
    private void ImportFiles(IEnumerable<string> paths)
    {
        string[] accepted = [".md", ".txt", ".json"];
        var files = paths
            .SelectMany(p => Directory.Exists(p)
                ? Directory.EnumerateFiles(p).Where(f => accepted.Contains(Path.GetExtension(f).ToLowerInvariant()))
                : [p])
            .ToList();

        NoteEditor.Flush();
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long? lastImported = null;
        var failed = new List<string>();

        foreach (string path in files)
        {
            string name = Path.GetFileName(path);
            try
            {
                if (new FileInfo(path).Length > 20_000_000)
                {
                    failed.Add($"{name}: the file is larger than 20 MB.");
                    continue;
                }

                var bundle = NoteImporter.Import(File.ReadAllText(path, Encoding.UTF8), name, now);
                lastImported = _viewModel.ImportBundle(bundle);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or NotSupportedException or SqliteException)
            {
                failed.Add($"{name}: {ex.Message}");
            }
        }

        _viewModel.FinishImport(lastImported);

        if (failed.Count > 0)
        {
            MessageBox.Show(this, "These files could not be imported:\n\n" + string.Join("\n", failed), "MyCustomNote", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (_appLocked || e.Data.GetData(DataFormats.FileDrop) is not string[] dropped)
        {
            return;
        }

        e.Handled = true;
        ImportFiles(dropped);
    }

    private void CopyText(NoteListItem note)
    {
        try
        {
            Clipboard.SetText(NoteExporter.Export(_viewModel.BuildBundle(note), NoteFileFormat.Text));
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            MessageBox.Show(this, "The clipboard is in use by another program. Try again.", "MyCustomNote", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // Delete on the note list asks first. Enter confirms a move to the trash (the Yes button is the default); a
    // permanent delete defaults to No, because it cannot be undone.
    private void NoteList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete)
        {
            return;
        }

        var selected = NoteList.SelectedItems.Cast<NoteListItem>().ToList();
        if (selected.Count == 0)
        {
            return;
        }

        e.Handled = true;
        if (_viewModel.CurrentView == NavKind.Locked && !_viewModel.IsHiddenView)
        {
            MessageBox.Show(this, "Unlock a locked note (right-click it) before deleting it.", "MyCustomNote", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        bool permanent = _viewModel.IsHiddenView || _viewModel.CurrentView == NavKind.Trash;
        string what = selected.Count == 1 ? $"\"{selected[0].Title}\"" : $"{selected.Count} notes";
        string text = permanent ? $"Delete {what} for good? This cannot be undone." : $"Move {what} to the trash?";
        var answer = MessageBox.Show(
            this,
            text,
            "MyCustomNote",
            MessageBoxButton.YesNo,
            permanent ? MessageBoxImage.Warning : MessageBoxImage.Question,
            permanent ? MessageBoxResult.No : MessageBoxResult.Yes);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        Act(() =>
        {
            if (permanent)
            {
                _viewModel.DeleteMany(selected);
            }
            else
            {
                _viewModel.TrashMany(selected);
            }
        });
    }

    // ---------- export ----------

    private static readonly (string Label, NoteFileFormat Format)[] ExportChoices =
    [
        ("Markdown (.md)", NoteFileFormat.Markdown),
        ("Plain text (.txt)", NoteFileFormat.Text),
        ("HTML page (.html)", NoteFileFormat.Html),
        ("JSON, keeps everything (.json)", NoteFileFormat.Json),
    ];

    // The Export button works on the selected notes; it needs at least one, and a locked note must be unlocked first.
    private void UpdateExportButton() =>
        ExportButton.IsEnabled = NoteList.SelectedItems.Count > 0
            && !(_viewModel.CurrentView == NavKind.Locked && !_viewModel.IsHiddenView);

    // A small menu under the button asks for the format; one note then goes through a Save dialog, several notes
    // are written as one file each into a folder.
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (Button)sender, Placement = PlacementMode.Bottom };
        foreach (var (label, format) in ExportChoices)
        {
            var chosen = format;
            menu.Items.Add(Item(label, () => Act(() => ExportSelected(chosen))));
        }

        menu.IsOpen = true;
    }

    private void ExportSelected(NoteFileFormat format)
    {
        var selected = NoteList.SelectedItems.Cast<NoteListItem>().ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Select one or more notes in the list first.", "MyCustomNote", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_viewModel.CurrentView == NavKind.Locked && !_viewModel.IsHiddenView)
        {
            MessageBox.Show(this, "Unlock a locked note (right-click it) before exporting it.", "MyCustomNote", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (selected.Count == 1)
        {
            ExportNote(selected[0], format);
            return;
        }

        var folder = new OpenFolderDialog { Title = $"Export {selected.Count} notes: choose a folder" };
        if (folder.ShowDialog(this) != true)
        {
            return;
        }

        string extension = NoteExporter.Extension(format);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int written = 0;
        var failed = new List<string>();

        foreach (var note in selected)
        {
            try
            {
                var bundle = _viewModel.BuildBundle(note);
                string fileName = UniqueFileName(folder.FolderName, NoteExporter.SafeFileName(bundle.Note.Title), extension, usedNames);
                File.WriteAllText(Path.Combine(folder.FolderName, fileName), NoteExporter.Export(bundle, format), new UTF8Encoding(false));
                written++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add($"{note.Title}: {ex.Message}");
            }
        }

        string summary = $"{written} of {selected.Count} notes exported to:\n{folder.FolderName}";
        if (failed.Count > 0)
        {
            summary += "\n\nThese could not be saved:\n" + string.Join("\n", failed);
        }

        MessageBox.Show(this, summary, "MyCustomNote", MessageBoxButton.OK, failed.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    // Never overwrites: a name that is taken (on disk, or by an earlier note of this export) gets " (2)", " (3)", ...
    private static string UniqueFileName(string folder, string baseName, string extension, HashSet<string> used)
    {
        string name = $"{baseName}.{extension}";
        for (int n = 2; used.Contains(name) || File.Exists(Path.Combine(folder, name)); n++)
        {
            name = $"{baseName} ({n}).{extension}";
        }

        used.Add(name);
        return name;
    }

    private void ExportNote(NoteListItem note, NoteFileFormat preferred = NoteFileFormat.Markdown)
    {
        var bundle = _viewModel.BuildBundle(note);
        var dialog = new SaveFileDialog
        {
            Title = "Export note",
            FileName = NoteExporter.SafeFileName(bundle.Note.Title),
            Filter = "Markdown (*.md)|*.md|Plain text (*.txt)|*.txt|HTML page (*.html)|*.html|JSON, keeps everything (*.json)|*.json",
            AddExtension = true,
            DefaultExt = NoteExporter.Extension(preferred),
            FilterIndex = preferred switch
            {
                NoteFileFormat.Text => 2,
                NoteFileFormat.Html => 3,
                NoteFileFormat.Json => 4,
                _ => 1,
            },
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var format = dialog.FilterIndex switch
        {
            2 => NoteFileFormat.Text,
            3 => NoteFileFormat.Html,
            4 => NoteFileFormat.Json,
            _ => NoteFileFormat.Markdown,
        };

        try
        {
            File.WriteAllText(dialog.FileName, NoteExporter.Export(bundle, format), new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "The note could not be saved:\n\n" + ex.Message, "MyCustomNote", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        string? name = PromptWindow.Ask(this, "New folder", "Folder name:");
        if (name is not null)
        {
            _viewModel.CreateFolder(name);
        }
    }

    // ---------- sort chips ----------

    private static readonly (string Label, SortOption Value)[] SortChoices =
    [
        ("Date created", SortOption.DateCreated),
        ("Date modified", SortOption.DateModified),
        ("Title", SortOption.Title),
        ("Last opened", SortOption.LastOpened),
        ("Manual", SortOption.Manual),
    ];

    private void BuildSortChips()
    {
        foreach (var (label, value) in SortChoices)
        {
            var chip = new ToggleButton
            {
                Tag = value,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(10, 3, 10, 3),
                Focusable = false,
            };
            chip.Click += (_, _) => _settings.Update(s => s with { Sort = value });
            SortChips.Children.Add(chip);
        }

        _settings.Changed += (_, _) => RefreshSortChips();
        RefreshSortChips();
    }

    // The active chip shows a check mark; clicking the active one keeps it active.
    private void RefreshSortChips()
    {
        foreach (ToggleButton chip in SortChips.Children)
        {
            var value = (SortOption)chip.Tag;
            bool active = _settings.Current.Sort == value;
            chip.IsChecked = active;
            chip.Content = (active ? "\u2713 " : "") + SortChoices.First(c => c.Value == value).Label;
        }
    }

    // ---------- context menus ----------

    private object? _contextTarget;

    // A right click selects the item under the mouse and builds the menu for it. The menu is built here, before
    // WPF decides whether to open one: on empty space the menu is null, so nothing opens.
    private void RememberRightClickedItem(object sender, MouseButtonEventArgs e)
    {
        _contextTarget = null;
        var list = (ListBox)sender;
        if (e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(list, source) is ListBoxItem { IsEnabled: true } container)
        {
            // Right-clicking a note that is part of a multiple selection keeps the selection (the menu then acts on all).
            if (!container.IsSelected)
            {
                if (list.SelectionMode != SelectionMode.Single)
                {
                    list.SelectedItems.Clear();
                }

                container.IsSelected = true;
            }

            _contextTarget = container.DataContext;
        }

        list.ContextMenu = list == NoteList ? BuildNoteMenu(_contextTarget) : BuildNavMenu(_contextTarget);
    }

    private bool Confirm(string text) =>
        MessageBox.Show(this, text, "MyCustomNote", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
        == MessageBoxResult.Yes;

    // Pending editor text is saved first, so an action never works on an old copy of the note.
    private void Act(Action action)
    {
        NoteEditor.Flush();
        action();
    }

    private static MenuItem Item(string header, Action onClick, bool isChecked = false, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsChecked = isChecked, IsEnabled = enabled };
        item.Click += (_, _) => onClick();
        return item;
    }

    private ContextMenu? BuildNoteMenu(object? target)
    {
        if (target is not NoteListItem note)
        {
            return null;
        }

        var selected = NoteList.SelectedItems.Cast<NoteListItem>().ToList();
        if (selected.Count > 1 && selected.Contains(note))
        {
            return BuildBatchMenu(selected);
        }

        if (_viewModel.IsHiddenView)
        {
            var hiddenMenu = new ContextMenu();
            hiddenMenu.Items.Add(Item("Unhide note", () => Act(() => _viewModel.SetHidden(note, false))));
            hiddenMenu.Items.Add(Item("Delete permanently...", () => Act(() =>
            {
                if (Confirm($"Delete \"{note.Title}\" for good? This cannot be undone."))
                {
                    _viewModel.DeletePermanently(note);
                }
            })));
            return hiddenMenu;
        }

        if (_viewModel.CurrentView == NavKind.Locked)
        {
            var lockedMenu = new ContextMenu();
            lockedMenu.Items.Add(Item("Unlock note", () => Act(() =>
            {
                if (PinDialogs.AskNotesPin(this, _security))
                {
                    _viewModel.UnlockNote(note);
                }
            })));
            return lockedMenu;
        }

        var menu = new ContextMenu();

        if (_viewModel.CurrentView == NavKind.Trash)
        {
            menu.Items.Add(Item("Restore", () => Act(() => _viewModel.Restore(note))));
            menu.Items.Add(Item("Delete permanently...", () => Act(() =>
            {
                if (Confirm($"Delete \"{note.Title}\" for good? This cannot be undone."))
                {
                    _viewModel.DeletePermanently(note);
                }
            })));
            return menu;
        }

        menu.Items.Add(Item(note.IsPinned ? "Unpin" : "Pin", () => Act(() => _viewModel.TogglePin(note))));
        menu.Items.Add(Item(
            note.Note.IsArchived ? "Unarchive" : "Archive",
            () => Act(() => _viewModel.SetArchived(note, !note.Note.IsArchived))));

        var folders = new MenuItem { Header = "Move to folder" };
        folders.Items.Add(Item("No folder", () => Act(() => _viewModel.MoveToFolder(note, null)), note.Note.FolderId is null));
        foreach (var folder in _viewModel.GetFolders())
        {
            long id = folder.Id;
            folders.Items.Add(Item(folder.Name, () => Act(() => _viewModel.MoveToFolder(note, id)), note.Note.FolderId == id));
        }

        menu.Items.Add(folders);

        var colors = new MenuItem { Header = "Color" };
        foreach (var color in Enum.GetValues<NoteColorTag>())
        {
            var tag = color;
            var entry = Item(tag.ToString(), () => Act(() => _viewModel.SetColor(note, tag)), (note.Note.ColorTag ?? NoteColorTag.Default) == tag);
            entry.Icon = new Border
            {
                Width = 12,
                Height = 12,
                CornerRadius = new CornerRadius(2),
                Background = NoteColors.BrushFor(tag),
            };
            colors.Items.Add(entry);
        }

        menu.Items.Add(colors);
        menu.Items.Add(Item("Lock note...", () => Act(() =>
        {
            if (PinDialogs.EnsureNotesKey(this, _security))
            {
                _viewModel.LockNote(note);
            }
        })));
        menu.Items.Add(Item("Duplicate note", () => Act(() => _viewModel.DuplicateNote(note))));
        menu.Items.Add(Item("Copy text", () => Act(() => CopyText(note))));
        menu.Items.Add(Item("Export...", () => Act(() => ExportNote(note))));

        if (_viewModel.IsManualSort)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Move up", () => Act(() => _viewModel.MoveInList(note, -1))));
            menu.Items.Add(Item("Move down", () => Act(() => _viewModel.MoveInList(note, 1))));
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Move to trash", () => Act(() => _viewModel.MoveToTrash(note))));
        return menu;
    }

    private ContextMenu? BuildBatchMenu(List<NoteListItem> items)
    {
        var view = _viewModel.CurrentView;
        if (_viewModel.IsHiddenView || view == NavKind.Locked)
        {
            return null; // locked and hidden notes are handled one at a time
        }

        int count = items.Count;
        var menu = new ContextMenu();

        if (view == NavKind.Trash)
        {
            menu.Items.Add(Item($"Restore {count} notes", () => Act(() => _viewModel.RestoreMany(items))));
            menu.Items.Add(Item($"Delete {count} notes permanently...", () => Act(() =>
            {
                if (Confirm($"Delete {count} notes for good? This cannot be undone."))
                {
                    _viewModel.DeleteMany(items);
                }
            })));
            return menu;
        }

        bool archive = view == NavKind.Archive;
        menu.Items.Add(Item(archive ? $"Unarchive {count} notes" : $"Archive {count} notes", () => Act(() => _viewModel.ArchiveMany(items, !archive))));

        var folders = new MenuItem { Header = "Move to folder" };
        folders.Items.Add(Item("No folder", () => Act(() => _viewModel.MoveToFolderMany(items, null))));
        foreach (var folder in _viewModel.GetFolders())
        {
            long id = folder.Id;
            folders.Items.Add(Item(folder.Name, () => Act(() => _viewModel.MoveToFolderMany(items, id))));
        }

        menu.Items.Add(folders);

        var colors = new MenuItem { Header = "Color" };
        foreach (var color in Enum.GetValues<NoteColorTag>())
        {
            var tag = color;
            var entry = Item(tag.ToString(), () => Act(() => _viewModel.SetColorMany(items, tag)));
            entry.Icon = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(2), Background = NoteColors.BrushFor(tag) };
            colors.Items.Add(entry);
        }

        menu.Items.Add(colors);
        menu.Items.Add(Item("Add tag...", () => Act(() =>
        {
            string? name = PromptWindow.Ask(this, "Add tag", $"Tag name for {count} notes:");
            if (name is not null)
            {
                _viewModel.AddTagMany(items, name);
            }
        })));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item($"Move {count} notes to trash", () => Act(() => _viewModel.TrashMany(items))));
        return menu;
    }

    private ContextMenu? BuildNavMenu(object? target)
    {
        if (target is not NavItem { Kind: NavKind.Folder or NavKind.Tag } nav)
        {
            return null;
        }

        string what = nav.Kind == NavKind.Folder ? "folder" : "tag";
        var menu = new ContextMenu();
        menu.Items.Add(Item("Rename...", () =>
        {
            string? name = PromptWindow.Ask(this, $"Rename {what}", "New name:", nav.Title);
            if (name is not null && !_viewModel.RenameNav(nav, name))
            {
                MessageBox.Show(this, $"A {what} with that name already exists.", "MyCustomNote", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }));
        menu.Items.Add(Item("Delete...", () =>
        {
            string detail = nav.Kind == NavKind.Folder
                ? "Its notes are kept, without a folder."
                : "Its notes are kept, without this tag.";
            if (Confirm($"Delete the {what} \"{nav.Title}\"? {detail}"))
            {
                NoteEditor.Flush();
                _viewModel.DeleteNav(nav);
            }
        }));
        return menu;
    }

    // The settings window can encrypt or re-key the database file; it saves the text being typed first.
    private void SettingsButton_Click(object sender, RoutedEventArgs e) =>
        new SettingsWindow(_settings, _security, NoteEditor.Flush) { Owner = this }.ShowDialog();

    /// <summary>Esc: first clears the search, then (or if it is already empty) moves from the search box back to the note list.</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (e.Key != Key.Escape)
        {
            return;
        }

        NoteEditor.Flush();

        if (_viewModel.SearchText.Length > 0)
        {
            _viewModel.ClearSearchCommand.Execute(null);
            NoteList.Focus();
            e.Handled = true;
        }
        else if (_viewModel.IsHiddenView)
        {
            _viewModel.CloseHidden();
            e.Handled = true;
        }
        else if (SearchBox.IsKeyboardFocused)
        {
            NoteList.Focus();
            e.Handled = true;
        }
    }

    // ---------- window size, position and pane widths ----------

    private void RestoreGeometry()
    {
        var saved = _settings.Current.Window;

        if (saved.Width is { } width && saved.Height is { } height && width >= MinWidth && height >= MinHeight)
        {
            Width = width;
            Height = height;
        }

        // Only reuse the saved position if the window would still be on a screen (a monitor may have been unplugged).
        if (saved.Left is { } left && saved.Top is { } top && IsOnScreen(new Rect(left, top, Width, Height)))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }

        if (saved.Maximized)
        {
            WindowState = WindowState.Maximized;
        }

        if (saved.LeftPaneWidth is { } leftPane)
        {
            LeftColumn.Width = new GridLength(Math.Clamp(leftPane, LeftColumn.MinWidth, 500));
        }

        if (saved.MiddlePaneWidth is { } middlePane)
        {
            MiddleColumn.Width = new GridLength(Math.Clamp(middlePane, MiddleColumn.MinWidth, 700));
        }
    }

    private static bool IsOnScreen(Rect window)
    {
        var screen = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);

        var visible = Rect.Intersect(window, screen);
        return !visible.IsEmpty && visible.Width >= 100 && visible.Height >= 50;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        NoteEditor.Flush();

        // RestoreBounds is the normal-size rectangle even while the window is maximized or minimized.
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _settings.Update(
            s => s with
            {
                Window = new Core.Settings.WindowSettings
                {
                    Left = bounds.Left,
                    Top = bounds.Top,
                    Width = bounds.Width,
                    Height = bounds.Height,
                    Maximized = WindowState == WindowState.Maximized,
                    LeftPaneWidth = LeftColumn.ActualWidth,
                    MiddlePaneWidth = MiddleColumn.ActualWidth,
                },
            },
            notify: false);
    }
}
