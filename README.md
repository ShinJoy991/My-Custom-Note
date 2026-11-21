# MyCustomNote for Windows

A local, offline note app for Windows. Your notes stay on your PC, in one folder you can back up. It is the desktop sibling of the Android app MyCustomNote, and notes can be exchanged between the two.

Built with C# on .NET 10 and WPF.

## Features

- **Rich text:** bold, italic, underline, strikethrough, a heading level, bullet and numbered lists. Choose a style before you type, or select text first. Paste is plain text.
- **Organise:** folders, tags, color tags, checklists, pinned notes, archive and trash (trash is emptied after 30 days by default, adjustable).
- **Find and sort:** search, and sort by date created, date modified, title, last opened, or your own drag-and-drop order.
- **Privacy:**
  - an app PIN that locks the app on start, when minimised, or after being idle;
  - locked notes, encrypted with AES-256-GCM under a separate notes PIN (there is no recovery if you forget that PIN);
  - a hidden-notes area;
  - optional encryption of the whole database file (SQLCipher).
- **Import and export:** Markdown, plain text, HTML and JSON. JSON keeps everything (text, styles, lists, folder, tags, checklist, color). You can import by button or by dropping files on the window, and export one note or many at once.
- **Themes:** several themes, including one that follows Windows.

Not included: cloud sync, Windows Hello, an installer, an app icon.

## Download and run

Download the zip from the [Releases page](https://github.com/ShinJoy991/My-Custom-Note/releases), unzip it anywhere and start `MyCustomNote.exe`. No .NET installation is needed. The program is not code-signed, so Windows SmartScreen may show a warning the first time.

Your data lives in `%LOCALAPPDATA%\MyCustomNote` (`notes.db`, `settings.json`, `security.json`). Back that folder up. Deleting it resets the app.

## Build from source

You need Windows and the .NET 10 SDK (Visual Studio is optional).

```powershell
git clone https://github.com/ShinJoy991/My-Custom-Note.git
cd My-Custom-Note
dotnet build MyCustomNote.slnx
dotnet test MyCustomNote.slnx
dotnet run --project MyCustomNote
```

To make the release (a self-contained single file plus a zip):

```powershell
.\publish.ps1
```

## Project layout

| Project | What it holds |
|---|---|
| `MyCustomNote` | The WPF app: windows, view models, services |
| `MyCustomNote.Core` | Models, text formatting logic (styles, lists), import and export, sorting, security (PIN hashing, locked-note encryption) |
| `MyCustomNote.Data` | SQLite storage (including optional SQLCipher encryption) and repositories |
| `MyCustomNote.Editor` | The rich-text editor control |
| `MyCustomNote.Core.Tests`, `MyCustomNote.Data.Tests` | Unit tests |
| `MyCustomNote.EditorSpike` | Developer tool: a small window that runs the automatic editor self-check. Run it after any change to the editor |

How text is stored: a note's text is plain, and its formatting is a list of style ranges (start, end, kind) plus a paragraph kind per line. No formatting characters are mixed into the text, and no regular expressions are used for formatting.

## Using it with the Android app

- Export a note as **JSON** to move it between the apps without losing anything the receiving app can show.
- The phone app has no real lists or headings: it shows them as text prefixes (`# `, `- `, `1. `). When you import a phone file, the Windows app turns those prefixes back into real lists.
- Windows files carry a `paragraphs` entry; a JSON file without it is treated as coming from the phone.

## Security notes

- PINs are stored as salted PBKDF2-SHA256 hashes (210,000 iterations).
- Locked notes are written to disk only in encrypted form. Their key is protected by the notes PIN, so a forgotten notes PIN means those notes cannot be recovered.
- Whole-database encryption is the newest feature. Back up `%LOCALAPPDATA%\MyCustomNote` before turning it on.
- The app has not had an independent security review.

## Third-party packages

CommunityToolkit.Mvvm, Microsoft.Data.Sqlite and SQLitePCLRaw (SQLCipher bundle). Check each package's own license before redistributing.

## License

MIT, see [LICENSE](LICENSE).
