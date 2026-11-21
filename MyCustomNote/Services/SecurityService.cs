using MyCustomNote.Core.Security;

namespace MyCustomNote.Services;

/// <summary>
/// The PINs and the notes key while the app runs. The notes key (<see cref="NotesKey"/>) exists only in memory, only
/// after the notes PIN was entered, and is forgotten when the user leaves the Locked or Hidden screen and whenever the
/// app locks. The app PIN only decides whether the app may be used; it opens nothing in the database.
/// </summary>
internal sealed class SecurityService
{
    private readonly SecurityStore _store;

    public SecurityService(SecurityStore store, string databasePath)
    {
        _store = store;
        DatabasePath = databasePath;
        Data = store.Load();
        FileIsDamaged = store.FileIsDamaged;
    }

    /// <summary>The notes.db file (needed to encrypt, decrypt or re-key it).</summary>
    public string DatabasePath { get; }

    public SecurityData Data { get; private set; }

    /// <summary>True if security.json exists but could not be read (the app then refuses to start unlocked).</summary>
    public bool FileIsDamaged { get; }

    public bool HasAppPin => Data.AppPin is not null;

    public bool HasNotesPin => Data.NotesVault is not null;

    public VaultKey? NotesKey { get; private set; }

    public bool VerifyAppPin(string pin) => Data.AppPin is { } record && PinHasher.Verify(pin, record);

    public void SetAppPin(string pin) => Update(d => d with { AppPin = PinHasher.Create(pin) });

    public void RemoveAppPin() => Update(d => d with { AppPin = null });

    /// <summary>Creates the vault with the first notes PIN and unlocks it.</summary>
    public void CreateNotesPin(string pin)
    {
        Update(d => d with { NotesVault = Vault.Create(pin) });
        UnlockNotes(pin);
    }

    /// <summary>Checks the notes PIN and keeps the key in memory. False for a wrong PIN or if there is no notes PIN yet.</summary>
    public bool UnlockNotes(string pin)
    {
        if (Data.NotesVault is not { } vault)
        {
            return false;
        }

        var key = Vault.TryUnlock(vault, pin);
        if (key is null)
        {
            return false;
        }

        NotesKey = key;
        return true;
    }

    public void LockNotes() => NotesKey = null;

    /// <summary>Changes the notes PIN; locked notes stay readable (the key inside the vault does not change). False if the old PIN is wrong.</summary>
    public bool ChangeNotesPin(string oldPin, string newPin)
    {
        if (Data.NotesVault is not { } vault || Vault.ChangePin(vault, oldPin, newPin) is not { } changed)
        {
            return false;
        }

        Update(d => d with { NotesVault = changed });
        return true;
    }

    public void SetLockAfterMinutes(int minutes) => Update(d => d with { LockAfterMinutes = Math.Max(0, minutes) });

    /// <summary>The database was encrypted with this password: the app PIN becomes the same password.</summary>
    public void RecordDatabasePassword(string password) =>
        Update(d => d with { AppPin = PinHasher.Create(password), DatabaseEncrypted = true });

    /// <summary>Tells the settings what the file really is (the file is the truth, this flag only mirrors it).</summary>
    public void RecordDatabaseEncrypted(bool encrypted) => Update(d => d with { DatabaseEncrypted = encrypted });

    private void Update(Func<SecurityData, SecurityData> change)
    {
        var next = change(Data);
        _store.Save(next); // if this throws, the old data stays in effect
        Data = next;
    }
}
