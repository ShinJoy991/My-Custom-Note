using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyCustomNote.Core.Models;
using MyCustomNote.Core.IO;
using MyCustomNote.Core.Sorting;
using MyCustomNote.Data;
using MyCustomNote.Services;

namespace MyCustomNote.ViewModels;

/// <summary>
/// The main window: the left pane (sections, folders, tags), the middle pane (the note list) and the selected note.
/// Step 4 only shows what is in the database and creates empty notes; the editor, folder and tag management and
/// the list actions come in section 5.
/// </summary>
internal sealed partial class MainViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly NoteRepository _notes;
    private readonly FolderRepository _folders;
    private readonly TagRepository _tags;
    private readonly ChecklistRepository _checklists;
    private readonly SecurityService _security;
    private bool _hiddenMode;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModeLabel), nameof(HasModeLabel))]
    private NavItem? _selectedNav;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(HasNoSelection))]
    private NoteListItem? _selectedNote;

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private string _emptyMessage = "";

    public MainViewModel(SettingsService settings, NoteRepository notes, FolderRepository folders, TagRepository tags, ChecklistRepository checklists, SecurityService security)
    {
        _settings = settings;
        _notes = notes;
        _folders = folders;
        _tags = tags;
        _checklists = checklists;
        _security = security;

        // Changing the sort option in the settings re-orders the list.
        _settings.Changed += (_, _) => LoadNotes();

        LoadNav();
        SelectedNav = NavItems[0];
    }

    public ObservableCollection<NavItem> NavItems { get; } = [];

    public ObservableCollection<NoteListItem> Notes { get; } = [];

    public bool HasSelection => SelectedNote is not null;

    public bool HasNoSelection => SelectedNote is null;

    /// <summary>True while the list is being rebuilt; the selection changes during that are not the user's.</summary>
    public bool IsReloading { get; private set; }

    /// <summary>Raised when the list has been rebuilt and the selection is final (the editor syncs to it).</summary>
    public event EventHandler? NotesReloaded;

    /// <summary>The editor saved a note: its modified time changed, so the list is read again (the selection stays).</summary>
    public void RefreshAfterSave() => LoadNotes();

    partial void OnSelectedNoteChanged(NoteListItem? value)
    {
        // Opening a note (not a rebuild of the list) counts as opening it, for the "Last opened" sort.
        if (!IsReloading && value is not null)
        {
            _notes.UpdateLastOpened(value.Id);
        }
    }

    partial void OnSearchTextChanged(string value) => LoadNotes();

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (value is not { IsSelectable: true })
        {
            return;
        }

        if (_hiddenMode)
        {
            _hiddenMode = false;
            OnPropertyChanged(nameof(IsHiddenView));
        }

        // The Locked Notes list asks for the notes PIN every time it is opened.
        if (value.Kind == NavKind.Locked && _security.HasNotesPin && _security.NotesKey is null && NotesPinPrompt?.Invoke() != true)
        {
            SelectedNav = NavItems[0];
            return;
        }

        LoadNotes(); // the editor saves a locked note it still shows while this runs, so the key is forgotten only afterwards
        if (value.Kind != NavKind.Locked)
        {
            _security.LockNotes();
        }
    }

    /// <summary>Set by the window: asks for the notes PIN and returns true if the notes are unlocked afterwards.</summary>
    public Func<bool>? NotesPinPrompt { get; set; }

    public bool IsHiddenView => _hiddenMode;

    public string ModeLabel => _hiddenMode ? "Hidden notes  \u00b7  Esc to leave" : SelectedNav?.Kind == NavKind.Locked ? "Locked notes" : "";

    public bool HasModeLabel => ModeLabel.Length > 0;

    private bool InSecureView => _hiddenMode || SelectedNav?.Kind == NavKind.Locked;

    [RelayCommand]
    private void NewNote()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        bool hidden = _hiddenMode; // a note made inside the Hidden screen is hidden
        long? folderId = !hidden && SelectedNav is { Kind: NavKind.Folder } folder ? folder.Id : null;
        long id = _notes.Create(new Note { CreatedAt = now, ModifiedAt = now, FolderId = folderId, IsHidden = hidden });

        // A new note is shown in the list it belongs to: its folder, or "All notes".
        SearchText = "";
        if (!hidden && SelectedNav is not { Kind: NavKind.Folder or NavKind.AllNotes })
        {
            SelectedNav = NavItems[0];
        }

        LoadNotes(selectId: id);
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = "";

    // ---------- actions on a note (context menu of the list) ----------

    /// <summary>Which list is open, for deciding which actions make sense (trash: restore and delete only).</summary>
    public NavKind CurrentView => SearchText.Trim().Length > 0 ? NavKind.AllNotes : SelectedNav?.Kind ?? NavKind.AllNotes;

    public bool IsManualSort => _settings.Current.Sort == SortOption.Manual && SearchText.Trim().Length == 0;

    public IReadOnlyList<Folder> GetFolders() => _folders.GetAll();

    public void TogglePin(NoteListItem item)
    {
        _notes.SetPinned(item.Id, !item.IsPinned);
        LoadNotes();
    }

    public void SetArchived(NoteListItem item, bool archived)
    {
        _notes.SetArchived(item.Id, archived);
        LoadNotes();
    }

    public void MoveToTrash(NoteListItem item)
    {
        _notes.MoveToTrash(item.Id);
        LoadNotes();
    }

    public void Restore(NoteListItem item)
    {
        _notes.RestoreFromTrash(item.Id);
        LoadNotes();
    }

    public void DeletePermanently(NoteListItem item)
    {
        _notes.PermanentlyDelete(item.Id);
        LoadNav(); // a tag that was only on this note is no longer listed
        LoadNotes();
    }

    public void MoveToFolder(NoteListItem item, long? folderId)
    {
        _notes.SetFolder(item.Id, folderId);
        LoadNotes();
    }

    public void SetColor(NoteListItem item, NoteColorTag? color)
    {
        _notes.SetColor(item.Id, color);
        LoadNotes();
    }

    /// <summary>Manual order: moves a note one place up (-1) or down (+1) and stores the new order of the shown list.</summary>
    public void MoveInList(NoteListItem item, int delta)
    {
        int index = Notes.IndexOf(item);
        if (index >= 0)
        {
            MoveTo(item, index + delta);
        }
    }

    /// <summary>Manual order: puts a note at a place of the shown list (drag and drop) and stores the new order.</summary>
    public void MoveTo(NoteListItem item, int target)
    {
        int index = Notes.IndexOf(item);
        if (index < 0 || target < 0 || target >= Notes.Count || target == index)
        {
            return;
        }

        var ids = Notes.Select(n => n.Id).ToList();
        ids.RemoveAt(index);
        ids.Insert(target, item.Id);
        _notes.UpdateSortOrder(ids.Select((id, position) => (id, (long)position)));
        LoadNotes();
    }

    // ---------- locked and hidden notes ----------

    private void AfterSecureChange()
    {
        LoadNav(); // a tag used only by locked or hidden notes leaves the tag list
        LoadNotes();
        if (!InSecureView)
        {
            _security.LockNotes();
        }
    }

    /// <summary>Locks a note. The notes key must be available (the window asks for the PIN first).</summary>
    public void LockNote(NoteListItem item)
    {
        if (_security.NotesKey is { } key)
        {
            _notes.Lock(item.Id, key);
            AfterSecureChange();
        }
    }

    public void UnlockNote(NoteListItem item)
    {
        if (_security.NotesKey is { } key)
        {
            _notes.Unlock(item.Id, key);
            AfterSecureChange();
        }
    }

    public void SetHidden(NoteListItem item, bool hidden)
    {
        _notes.SetHidden(item.Id, hidden);
        AfterSecureChange();
    }

    /// <summary>Opens the Hidden screen (the window has already checked the notes PIN).</summary>
    public void OpenHidden()
    {
        _hiddenMode = true;
        SelectedNav = null;
        LoadNotes();
        OnPropertyChanged(nameof(IsHiddenView));
        OnPropertyChanged(nameof(ModeLabel));
        OnPropertyChanged(nameof(HasModeLabel));
    }

    public void CloseHidden()
    {
        if (!_hiddenMode)
        {
            return;
        }

        _hiddenMode = false;
        OnPropertyChanged(nameof(IsHiddenView));
        SelectedNav = NavItems[0]; // loads the list; the key is forgotten after that
    }

    /// <summary>When the app locks: leave the Locked and Hidden screens and forget the notes key.</summary>
    public void LeaveSecureViews()
    {
        if (_hiddenMode)
        {
            CloseHidden();
        }
        else if (SelectedNav?.Kind == NavKind.Locked)
        {
            SelectedNav = NavItems[0];
        }

        _security.LockNotes();
    }

    // ---------- actions on several selected notes ----------

    public void ArchiveMany(IReadOnlyList<NoteListItem> items, bool archived)
    {
        foreach (var item in items)
        {
            _notes.SetArchived(item.Id, archived);
        }

        LoadNotes();
    }

    public void TrashMany(IReadOnlyList<NoteListItem> items)
    {
        foreach (var item in items)
        {
            _notes.MoveToTrash(item.Id);
        }

        LoadNotes();
    }

    public void RestoreMany(IReadOnlyList<NoteListItem> items)
    {
        foreach (var item in items)
        {
            _notes.RestoreFromTrash(item.Id);
        }

        LoadNotes();
    }

    public void DeleteMany(IReadOnlyList<NoteListItem> items)
    {
        foreach (var item in items)
        {
            _notes.PermanentlyDelete(item.Id);
        }

        LoadNav();
        LoadNotes();
    }

    public void MoveToFolderMany(IReadOnlyList<NoteListItem> items, long? folderId)
    {
        foreach (var item in items)
        {
            _notes.SetFolder(item.Id, folderId);
        }

        LoadNotes();
    }

    public void SetColorMany(IReadOnlyList<NoteListItem> items, NoteColorTag? color)
    {
        foreach (var item in items)
        {
            _notes.SetColor(item.Id, color);
        }

        LoadNotes();
    }

    public void AddTagMany(IReadOnlyList<NoteListItem> items, string tagName)
    {
        long tagId = _tags.Create(tagName.Trim());
        foreach (var item in items)
        {
            _tags.AddToNote(item.Id, tagId);
        }

        LoadNav();
        LoadNotes();
    }

    // ---------- export and import ----------

    /// <summary>The note as stored now, with its folder name, tag names and checklist, ready to be written to a file.</summary>
    public NoteBundle BuildBundle(NoteListItem item)
    {
        var note = _notes.GetById(item.Id) ?? item.Note;
        string? folder = note.FolderId is { } folderId ? _folders.GetById(folderId)?.Name : null;
        return new NoteBundle(note, folder, note.Tags.Select(t => t.Name).ToList(), note.ChecklistItems);
    }

    /// <summary>
    /// Stores an imported note as a new note (never over an existing one). The folder and the tags are looked up by
    /// name and created if missing. An imported "archived" note goes to the archive. Returns the new id.
    /// </summary>
    public long ImportBundle(NoteBundle bundle)
    {
        long? folderId = null;
        if (bundle.FolderName is { } name)
        {
            var existing = _folders.GetAll().FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            folderId = existing?.Id ?? _folders.Create(new Folder(0, name, null, _folders.GetAll().Count));
        }

        long id = _notes.Create(bundle.Note with
        {
            FolderId = folderId,
            IsTrashed = false,
            IsLocked = false,
            IsHidden = _hiddenMode, // anything added while the Hidden screen is open (import, drag and drop) is hidden
            LastOpenedAt = null,
            TrashedAt = null,
            SortOrder = 0,
        });

        foreach (string tag in bundle.Tags)
        {
            _tags.AddToNote(id, _tags.Create(tag));
        }

        if (bundle.ChecklistItems.Count > 0)
        {
            _checklists.ReplaceItems(id, bundle.ChecklistItems);
        }

        return id;
    }

    /// <summary>Makes a copy of a note (text, formatting, checklist, tags, color, folder), not pinned, with " (copy)" after the title.</summary>
    public void DuplicateNote(NoteListItem item)
    {
        var bundle = BuildBundle(item);
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string title = bundle.Note.Title.Length == 0 ? "" : bundle.Note.Title + " (copy)";
        var copy = bundle with
        {
            Note = bundle.Note with
            {
                Title = title,
                IsPinned = false,
                CreatedAt = now,
                ModifiedAt = now,
                TitleStyles = bundle.Note.Title.Length == 0 ? [] : bundle.Note.TitleStyles,
            },
        };

        long id = ImportBundle(copy);
        LoadNav();
        LoadNotes(selectId: id);
    }

    /// <summary>After one or more imports: shows "All notes" with the last imported note selected.</summary>
    public void FinishImport(long? lastImportedId)
    {
        LoadNav();
        SearchText = "";
        if (!_hiddenMode && SelectedNav is not { Kind: NavKind.AllNotes })
        {
            SelectedNav = NavItems[0];
        }

        LoadNotes(selectId: lastImportedId);
    }

    // ---------- folders and tags (context menu of the left pane) ----------

    public void CreateFolder(string name)
    {
        name = name.Trim();
        if (name.Length == 0)
        {
            return;
        }

        _folders.Create(new Folder(0, name, null, _folders.GetAll().Count));
        LoadNav();
    }

    /// <summary>Renames a folder or a tag. Returns false if a tag with that name already exists.</summary>
    public bool RenameNav(NavItem nav, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0)
        {
            return false;
        }

        bool ok = nav.Kind switch
        {
            NavKind.Folder => _folders.GetById(nav.Id) is { } folder && _folders.Update(folder with { Name = newName }),
            NavKind.Tag => _tags.Rename(nav.Id, newName),
            _ => false,
        };

        LoadNav();
        LoadNotes();
        return ok;
    }

    /// <summary>Deletes a folder (its notes stay, without a folder) or a tag (its notes stay, without the tag).</summary>
    public void DeleteNav(NavItem nav)
    {
        if (nav.Kind == NavKind.Folder)
        {
            _folders.Delete(nav.Id);
        }
        else if (nav.Kind == NavKind.Tag)
        {
            _tags.Delete(nav.Id);
        }

        LoadNav();
        LoadNotes();
    }

    /// <summary>Reads the folders and the visible tags again (the "All notes", "Archive" and "Trash" lines never change).</summary>
    public void LoadNav()
    {
        var folders = _folders.GetAll();
        var tags = _tags.GetVisible();

        var selected = SelectedNav;
        NavItems.Clear();
        NavItems.Add(new NavItem(NavKind.AllNotes, "All notes"));
        NavItems.Add(new NavItem(NavKind.Archive, "Archive"));
        NavItems.Add(new NavItem(NavKind.Trash, "Trash"));
        NavItems.Add(new NavItem(NavKind.Locked, "Locked notes"));
        NavItems.Add(new NavItem(NavKind.Header, "Folders"));
        if (folders.Count == 0)
        {
            NavItems.Add(new NavItem(NavKind.Placeholder, "No folders yet"));
        }

        foreach (var folder in folders)
        {
            NavItems.Add(new NavItem(NavKind.Folder, folder.Name, folder.Id));
        }

        NavItems.Add(new NavItem(NavKind.Header, "Tags"));
        if (tags.Count == 0)
        {
            NavItems.Add(new NavItem(NavKind.Placeholder, "No tags yet"));
        }

        foreach (var tag in tags)
        {
            NavItems.Add(new NavItem(NavKind.Tag, tag.Name, tag.Id));
        }

        // Keep the same line selected if it still exists.
        SelectedNav = selected is null ? null : NavItems.FirstOrDefault(n => n.Kind == selected.Kind && n.Id == selected.Id) ?? NavItems[0];
    }

    private void LoadNotes(long? selectId = null)
    {
        IsReloading = true;
        try
        {
            LoadNotesCore(selectId);
            NotesReloaded?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsReloading = false;
        }
    }

    private void LoadNotesCore(long? selectId)
    {
        long? keepSelected = selectId ?? SelectedNote?.Id;
        string query = SearchText.Trim();
        bool searching = query.Length > 0;

        IReadOnlyList<Note> found;
        if (_hiddenMode)
        {
            // The Hidden screen is not reachable by the normal search, so a search here only narrows this list.
            found = _notes.GetHidden()
                .Select(n => NoteRepository.Decrypted(n, _security.NotesKey))
                .Where(n => !searching
                    || n.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || n.Body.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        else if (searching)
        {
            found = _notes.Search(query);
        }
        else
        {
            found = (SelectedNav?.Kind) switch
            {
                NavKind.Archive => _notes.GetArchived(),
                NavKind.Trash => _notes.GetTrashed(),
                NavKind.Locked => _notes.GetLocked().Select(n => NoteRepository.Decrypted(n, _security.NotesKey)).ToList(),
                NavKind.Folder => _notes.GetInFolder(SelectedNav!.Id),
                NavKind.Tag => _notes.GetWithTag(SelectedNav!.Id),
                _ => _notes.GetActive(),
            };
        }

        Notes.Clear();
        foreach (var note in SortEngine.Sort(found, _settings.Current.Sort))
        {
            Notes.Add(new NoteListItem(note));
        }

        SelectedNote = keepSelected is { } id ? Notes.FirstOrDefault(n => n.Id == id) : null;
        IsEmpty = Notes.Count == 0;
        EmptyMessage = searching
            ? "No notes match your search."
            : _hiddenMode
                ? "No hidden notes.\nPress Ctrl+N to add one here."
                : SelectedNav?.Kind switch
                {
                    NavKind.Archive => "The archive is empty.",
                    NavKind.Trash => "The trash is empty.",
                    NavKind.Locked => "No locked notes.\nRight-click a note and choose Lock note.",
                    NavKind.Folder => "This folder is empty.",
                    NavKind.Tag => "No notes have this tag.",
                    _ => "No notes yet.\nPress Ctrl+N to create one.",
                };
    }
}
