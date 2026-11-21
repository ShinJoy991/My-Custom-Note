using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyCustomNote.Core.Security;

/// <summary>What is stored about security: the two PINs (hashes only) and the lock timeout. Never a PIN, never a key.</summary>
public sealed record SecurityData
{
    /// <summary>The app PIN: asked when the app starts and after the lock timeout. Null means no app lock.</summary>
    public PinRecord? AppPin { get; init; }

    /// <summary>The notes PIN and the encrypted data key. Null means no locked or hidden notes can be made yet.</summary>
    public VaultRecord? NotesVault { get; init; }

    /// <summary>Minutes without any input after which the app locks again; 0 means never (it still locks when minimized).</summary>
    public int LockAfterMinutes { get; init; } = 5;

    /// <summary>
    /// True if the whole database file is encrypted (SQLCipher). The password is then the app password, which is why
    /// the app PIN and the database password are changed together. The file itself is the truth (see
    /// <c>NoteDatabase.FileIsEncrypted</c>); this flag only tells the settings screen what to show.
    /// </summary>
    public bool DatabaseEncrypted { get; init; }
}

/// <summary>Reads and writes <see cref="SecurityData"/> as JSON. Like <c>SettingsStore</c>, reading never throws.</summary>
public sealed class SecurityStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public SecurityStore(string path) => Path = path;

    public string Path { get; }

    /// <summary>
    /// The stored data. A missing file means "no PINs yet". A file that exists but cannot be read also gives no
    /// PINs, so <see cref="FileIsDamaged"/> tells the caller to refuse to start silently without a lock.
    /// </summary>
    public SecurityData Load()
    {
        FileIsDamaged = false;
        try
        {
            if (!File.Exists(Path))
            {
                return new SecurityData();
            }

            using var stream = File.OpenRead(Path);
            return JsonSerializer.Deserialize<SecurityData>(stream, Options) ?? new SecurityData();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            FileIsDamaged = true;
            return new SecurityData();
        }
    }

    public bool FileIsDamaged { get; private set; }

    public void Save(SecurityData data)
    {
        string? directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporary = Path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(data, Options));
        File.Move(temporary, Path, overwrite: true);
    }
}
