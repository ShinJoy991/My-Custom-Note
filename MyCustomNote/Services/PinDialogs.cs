using System.IO;
using System.Windows;
using Microsoft.Data.Sqlite;
using MyCustomNote.Core.Security;
using MyCustomNote.Data;

namespace MyCustomNote.Services;

/// <summary>Every PIN question the user can be asked, with its wording and its checks, in one place.</summary>
internal static class PinDialogs
{
    private static string? CheckNew(string pin, string repeat) =>
        !PinHasher.IsAcceptable(pin) ? $"Use at least {PinHasher.MinimumLength} characters."
        : pin != repeat ? "The two PINs are different."
        : null;

    /// <summary>The lock screen. Returns what the user typed (it is also the database password when the database is encrypted), or null if the user chose Quit.</summary>
    public static string? UnlockApp(Window? owner, SecurityService security) =>
        PinWindow.Ask(
            owner,
            "MyCustomNote is locked",
            "Enter your app PIN to continue.",
            ["PIN"],
            v => security.VerifyAppPin(v[0]) ? null : "Wrong PIN.",
            cancelLabel: "Quit")?[0];

    /// <summary>Asks for the notes PIN unless the notes are already unlocked. False if the user cancelled.</summary>
    public static bool AskNotesPin(Window? owner, SecurityService security, string message = "Enter your notes PIN.")
    {
        if (security.NotesKey is not null)
        {
            return true;
        }

        return PinWindow.Ask(
            owner,
            "Notes PIN",
            message,
            ["Notes PIN"],
            v => security.UnlockNotes(v[0]) ? null : "Wrong PIN.") is not null;
    }

    /// <summary>The key for locking and hiding: asks for the notes PIN, or creates one the first time. False if cancelled.</summary>
    public static bool EnsureNotesKey(Window? owner, SecurityService security) =>
        security.HasNotesPin ? AskNotesPin(owner, security) : CreateNotesPin(owner, security);

    public static bool CreateNotesPin(Window? owner, SecurityService security)
    {
        var values = PinWindow.Ask(
            owner,
            "Create a notes PIN",
            "This PIN protects locked and hidden notes. It is separate from the app PIN. "
            + "If you forget it, locked notes can NOT be recovered.",
            ["New notes PIN", "Repeat PIN"],
            v => CheckNew(v[0], v[1]));
        return values is not null && Guard(owner, () => security.CreateNotesPin(values[0]));
    }

    public static void ChangeNotesPin(Window? owner, SecurityService security)
    {
        var values = PinWindow.Ask(
            owner,
            "Change the notes PIN",
            "Locked notes stay locked and stay readable with the new PIN.",
            ["Current notes PIN", "New notes PIN", "Repeat new PIN"],
            v => Vault.TryUnlock(security.Data.NotesVault!, v[0]) is null ? "The current PIN is wrong." : CheckNew(v[1], v[2]));
        if (values is not null)
        {
            Guard(owner, () => security.ChangeNotesPin(values[0], values[1]));
        }
    }

    public static void SetAppPin(Window? owner, SecurityService security)
    {
        var values = PinWindow.Ask(
            owner,
            "Set an app PIN",
            "The app asks for this PIN when it starts, after it was minimized, and after a time without use.",
            ["New app PIN", "Repeat PIN"],
            v => CheckNew(v[0], v[1]));
        if (values is not null)
        {
            Guard(owner, () => security.SetAppPin(values[0]));
        }
    }

    public static void ChangeAppPin(Window? owner, SecurityService security)
    {
        var values = PinWindow.Ask(
            owner,
            "Change the app PIN",
            "",
            ["Current app PIN", "New app PIN", "Repeat new PIN"],
            v => !security.VerifyAppPin(v[0]) ? "The current PIN is wrong." : CheckNew(v[1], v[2]));
        if (values is not null)
        {
            Guard(owner, () => security.SetAppPin(values[1]));
        }
    }

    public static void RemoveAppPin(Window? owner, SecurityService security)
    {
        var values = PinWindow.Ask(
            owner,
            "Remove the app PIN",
            "The app will open without a PIN. Locked and hidden notes keep their own notes PIN.",
            ["Current app PIN"],
            v => security.VerifyAppPin(v[0]) ? null : "Wrong PIN.");
        if (values is not null)
        {
            Guard(owner, security.RemoveAppPin);
        }
    }

    // ---------- encryption of the whole database ----------

    private static string? CheckNewPassword(string password, string repeat) =>
        !PinHasher.IsStrongDatabasePassword(password) ? $"Use at least {PinHasher.MinimumDatabasePasswordLength} characters."
        : password != repeat ? "The two passwords are different."
        : null;

    /// <summary>
    /// Encrypts notes.db with a new password that also becomes the app PIN. The app must be restarted afterwards:
    /// the open connections use the old state. <paramref name="beforeChange"/> saves what is being typed.
    /// </summary>
    public static void EncryptDatabase(Window? owner, SecurityService security, Action beforeChange)
    {
        var values = PinWindow.Ask(
            owner,
            "Encrypt the database",
            "All notes, titles, tags and folders are encrypted on disk with this password. It becomes your app PIN too and is "
            + "asked every time the app starts. If you forget it, ALL notes are lost and nobody can recover them. "
            + $"Use at least {PinHasher.MinimumDatabasePasswordLength} characters.",
            ["New password", "Repeat password"],
            v => CheckNewPassword(v[0], v[1]));
        if (values is null)
        {
            return;
        }

        beforeChange();
        if (!ChangeDatabase(owner, () =>
        {
            NoteDatabase.Encrypt(security.DatabasePath, values[0]);
            security.RecordDatabasePassword(values[0]);
        }))
        {
            return;
        }

        RestartNotice(owner, "The database is encrypted.");
    }

    public static void ChangeDatabasePassword(Window? owner, SecurityService security, Action beforeChange)
    {
        var values = PinWindow.Ask(
            owner,
            "Change the password",
            "The database password and the app PIN are the same, so both change. If you forget the new password, all notes are lost.",
            ["Current password", "New password", "Repeat new password"],
            v => !security.VerifyAppPin(v[0]) ? "The current password is wrong." : CheckNewPassword(v[1], v[2]));
        if (values is null)
        {
            return;
        }

        beforeChange();
        if (!ChangeDatabase(owner, () =>
        {
            NoteDatabase.ChangePassword(security.DatabasePath, values[0], values[1]);
            security.RecordDatabasePassword(values[1]);
        }))
        {
            return;
        }

        RestartNotice(owner, "The password was changed.");
    }

    /// <summary>Turns the file encryption off. The app PIN stays (as a PIN again).</summary>
    public static void DecryptDatabase(Window? owner, SecurityService security, Action beforeChange)
    {
        var values = PinWindow.Ask(
            owner,
            "Turn off database encryption",
            "The notes will be stored as a plain file again that any program on this PC can read. Enter your password to continue.",
            ["Current password"],
            v => security.VerifyAppPin(v[0]) ? null : "Wrong password.");
        if (values is null)
        {
            return;
        }

        beforeChange();
        if (!ChangeDatabase(owner, () =>
        {
            NoteDatabase.Decrypt(security.DatabasePath, values[0]);
            security.RecordDatabaseEncrypted(false);
        }))
        {
            return;
        }

        RestartNotice(owner, "The database is no longer encrypted.");
    }

    // Runs a change of the database file; if it fails the user is told, and the file is untouched (the change swaps
    // files only after checking the new one).
    private static bool ChangeDatabase(Window? owner, Action change)
    {
        try
        {
            change();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException
                                       or InvalidOperationException or WrongDatabasePasswordException)
        {
            MessageBox.Show(
                owner,
                "The change could not be made and your notes were not changed:\n\n" + ex.Message,
                "MyCustomNote",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private static void RestartNotice(Window? owner, string what)
    {
        MessageBox.Show(
            owner,
            what + "\n\nThe app closes now. Start it again and enter your password.",
            "MyCustomNote",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        Application.Current.Shutdown();
    }

    // The settings are saved to a file; if that fails nothing changed, and the user is told.
    private static bool Guard(Window? owner, Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(owner, "The PIN could not be saved:\n\n" + ex.Message, "MyCustomNote", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }
}
