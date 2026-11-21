using System.Security.Cryptography;
using System.Text;

namespace MyCustomNote.Core.Security;

/// <summary>A PIN as it is stored: never the PIN itself, only a salted PBKDF2 hash.</summary>
public sealed record PinRecord(string Salt, string Hash, int Iterations);

public static class PinHasher
{
    /// <summary>Slow on purpose (about 0.1 s): a stolen file cannot be tried against millions of PINs per second.</summary>
    public const int DefaultIterations = 210_000;

    public const int MinimumLength = 4;

    /// <summary>The database password protects against copying the file and guessing offline, so it must be longer than a PIN.</summary>
    public const int MinimumDatabasePasswordLength = 8;

    public static bool IsStrongDatabasePassword(string? password) =>
        password is not null && password.Length >= MinimumDatabasePasswordLength;

    public static PinRecord Create(string pin, int iterations = DefaultIterations)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        return new PinRecord(Convert.ToBase64String(salt), Convert.ToBase64String(Derive(pin, salt, iterations)), iterations);
    }

    public static bool Verify(string pin, PinRecord record)
    {
        try
        {
            byte[] expected = Convert.FromBase64String(record.Hash);
            byte[] actual = Derive(pin, Convert.FromBase64String(record.Salt), record.Iterations);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false; // a damaged record never matches
        }
    }

    /// <summary>A PIN has at least <see cref="MinimumLength"/> characters (digits are the usual choice, but any text works).</summary>
    public static bool IsAcceptable(string? pin) => pin is not null && pin.Length >= MinimumLength;

    internal static byte[] Derive(string pin, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(pin), salt, iterations, HashAlgorithmName.SHA256, 32);
}

/// <summary>The notes PIN opens the vault: a random 256-bit data key, stored encrypted by a key made from the PIN.</summary>
public sealed record VaultRecord(string Salt, int Iterations, string WrappedKey);

/// <summary>
/// Encrypts and decrypts the text of locked notes. Everything is AES-256-GCM: a wrong key or a changed byte is
/// detected, never silently decrypted into garbage. Text is stored as <c>enc1:</c> plus Base64 of
/// nonce, tag and cipher text.
/// </summary>
public sealed class VaultKey
{
    public const string Prefix = "enc1:";

    private readonly byte[] _key;

    internal VaultKey(byte[] key) => _key = key;

    public static bool IsEncrypted(string text) => text.StartsWith(Prefix, StringComparison.Ordinal);

    public string Encrypt(string plain) => Prefix + Convert.ToBase64String(Seal(_key, Encoding.UTF8.GetBytes(plain)));

    /// <exception cref="FormatException">The text is not in the encrypted form.</exception>
    /// <exception cref="CryptographicException">The text was changed or was encrypted with another key.</exception>
    public string Decrypt(string stored)
    {
        if (!IsEncrypted(stored))
        {
            throw new FormatException("The text is not encrypted.");
        }

        return Encoding.UTF8.GetString(Open(_key, Convert.FromBase64String(stored[Prefix.Length..])));
    }

    internal static byte[] Seal(byte[] key, byte[] plain)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] cipher = new byte[plain.Length];
        byte[] tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plain, cipher, tag);
        return [.. nonce, .. tag, .. cipher];
    }

    internal static byte[] Open(byte[] key, byte[] sealedData)
    {
        if (sealedData.Length < 28)
        {
            throw new CryptographicException("The data is too short.");
        }

        byte[] nonce = sealedData[..12];
        byte[] tag = sealedData[12..28];
        byte[] cipher = sealedData[28..];
        byte[] plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }
}

public static class Vault
{
    public static VaultRecord Create(string notesPin, int iterations = PinHasher.DefaultIterations)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] dataKey = RandomNumberGenerator.GetBytes(32);
        byte[] wrapped = VaultKey.Seal(PinHasher.Derive(notesPin, salt, iterations), dataKey);
        return new VaultRecord(Convert.ToBase64String(salt), iterations, Convert.ToBase64String(wrapped));
    }

    /// <summary>Opens the vault. Null if the PIN is wrong (or the record is damaged).</summary>
    public static VaultKey? TryUnlock(VaultRecord record, string notesPin)
    {
        try
        {
            byte[] kek = PinHasher.Derive(notesPin, Convert.FromBase64String(record.Salt), record.Iterations);
            return new VaultKey(VaultKey.Open(kek, Convert.FromBase64String(record.WrappedKey)));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Changes the notes PIN. The data key stays the same, so locked notes need no re-encryption; only the wrapping
    /// changes. Null if the old PIN is wrong.
    /// </summary>
    public static VaultRecord? ChangePin(VaultRecord record, string oldPin, string newPin, int iterations = PinHasher.DefaultIterations)
    {
        try
        {
            byte[] oldKek = PinHasher.Derive(oldPin, Convert.FromBase64String(record.Salt), record.Iterations);
            byte[] dataKey = VaultKey.Open(oldKek, Convert.FromBase64String(record.WrappedKey));

            byte[] salt = RandomNumberGenerator.GetBytes(16);
            byte[] wrapped = VaultKey.Seal(PinHasher.Derive(newPin, salt, iterations), dataKey);
            return new VaultRecord(Convert.ToBase64String(salt), iterations, Convert.ToBase64String(wrapped));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
