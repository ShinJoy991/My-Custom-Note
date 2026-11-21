using System.IO;
using System.Windows;
using MyCustomNote.Core.Security;
using MyCustomNote.Core.Settings;
using MyCustomNote.Data;
using MyCustomNote.Services;
using MyCustomNote.ViewModels;

namespace MyCustomNote;

public partial class App : Application
{
    // Kept for the whole run: the theme manager listens to Windows light/dark changes.
    private ThemeManager? _themeManager;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The app must not close when the lock screen (the first window) closes.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Everything the app stores lives here: settings.json and notes.db. Deleting the folder resets the app.
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyCustomNote");

        var settings = new SettingsService(new SettingsStore(Path.Combine(folder, "settings.json")));
        _themeManager = new ThemeManager(settings);
        _themeManager.Apply();

        string databasePath = Path.Combine(folder, "notes.db");
        var security = new SecurityService(new SecurityStore(Path.Combine(folder, "security.json")), databasePath);
        if (security.FileIsDamaged)
        {
            MessageBox.Show(
                "The security file could not be read, so the app will not open without its PIN check:\n\n"
                + Path.Combine(folder, "security.json")
                + "\n\nRestore the file from a backup. If you delete it, the app PIN is removed and locked notes can no longer be read.",
                "MyCustomNote",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        // What the user typed on the lock screen is also the database password when the file is encrypted.
        bool fileIsEncrypted = NoteDatabase.FileIsEncrypted(databasePath);
        string? databasePassword = null;
        if (security.HasAppPin)
        {
            string? typed = PinDialogs.UnlockApp(null, security);
            if (typed is null)
            {
                Shutdown();
                return;
            }

            databasePassword = typed;
        }

        if (fileIsEncrypted && (databasePassword is null || !NoteDatabase.CheckPassword(databasePath, databasePassword)))
        {
            // The app PIN and the database password differ (security.json was lost or replaced): ask for the password itself.
            var values = PinWindow.Ask(
                null,
                "Database password",
                "The notes database is encrypted. Enter its password.",
                ["Password"],
                v => NoteDatabase.CheckPassword(databasePath, v[0]) ? null : "Wrong password.",
                cancelLabel: "Quit");
            if (values is null)
            {
                Shutdown();
                return;
            }

            databasePassword = values[0];
        }

        if (!fileIsEncrypted)
        {
            databasePassword = null; // a plain file is opened without a key
        }

        if (security.Data.DatabaseEncrypted != fileIsEncrypted)
        {
            try
            {
                security.RecordDatabaseEncrypted(fileIsEncrypted); // the file is the truth
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // only the settings screen shows a wrong label until the next start
            }
        }

        NoteDatabase database;
        try
        {
            database = new NoteDatabase(databasePath, databasePassword);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"The notes database could not be opened:\n\n{ex.Message}\n\nFile: {databasePath}",
                "MyCustomNote",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var notes = new NoteRepository(database);
        var folders = new FolderRepository(database);
        var tags = new TagRepository(database);
        var checklists = new ChecklistRepository(database);
        var viewModel = new MainViewModel(settings, notes, folders, tags, checklists, security);

        // Old notes in the trash are deleted for good (the number of days is a setting; 0 means never).
        int retentionDays = settings.Current.TrashRetentionDays;
        if (retentionDays > 0)
        {
            notes.PurgeTrashOlderThan(retentionDays * 86_400_000L);
        }

        var window = new MainWindow(viewModel, settings, security, notes, tags, checklists);
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
    }
}
