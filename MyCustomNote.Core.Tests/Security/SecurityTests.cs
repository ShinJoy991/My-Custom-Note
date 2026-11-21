using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MyCustomNote.Core.Security;

namespace MyCustomNote.Core.Tests.Security;

[TestClass]
public class PinHasherTests
{
    private const int Fast = 1000; // few iterations: the tests would be slow with the real number

    [TestMethod]
    public void CorrectPin_Verifies_WrongPin_Does_Not()
    {
        var record = PinHasher.Create("1234", Fast);

        Assert.IsTrue(PinHasher.Verify("1234", record));
        Assert.IsFalse(PinHasher.Verify("1235", record));
        Assert.IsFalse(PinHasher.Verify("", record));
        Assert.IsFalse(PinHasher.Verify("12345", record));
    }

    [TestMethod]
    public void TheRecordNeverContainsThePin_AndTheSameAndSaltsDiffer()
    {
        var a = PinHasher.Create("987654", Fast);
        var b = PinHasher.Create("987654", Fast);

        Assert.IsFalse(a.Hash.Contains("987654"));
        Assert.AreNotEqual(a.Salt, b.Salt);
        Assert.AreNotEqual(a.Hash, b.Hash);
        Assert.AreEqual(Fast, a.Iterations);
    }

    [TestMethod]
    public void ADamagedRecord_NeverMatches()
    {
        Assert.IsFalse(PinHasher.Verify("1234", new PinRecord("!!!", "###", Fast)));
    }

    [TestMethod]
    public void UnicodePins_Work()
    {
        var record = PinHasher.Create("m\u1EADt-kh\u1EA9u", Fast);

        Assert.IsTrue(PinHasher.Verify("m\u1EADt-kh\u1EA9u", record));
        Assert.IsFalse(PinHasher.Verify("mat-khau", record));
    }

    [TestMethod]
    [DataRow("1234", true)]
    [DataRow("123", false)]
    [DataRow("", false)]
    [DataRow("a longer pass phrase", true)]
    public void IsAcceptable_NeedsFourCharacters(string pin, bool expected)
    {
        Assert.AreEqual(expected, PinHasher.IsAcceptable(pin));
    }

    [TestMethod]
    public void IsAcceptable_RejectsNull()
    {
        Assert.IsFalse(PinHasher.IsAcceptable(null));
    }
}

[TestClass]
public class VaultTests
{
    private const int Fast = 1000;

    [TestMethod]
    public void AnEncryptedTextCanBeReadAgainWithTheRightPin()
    {
        var record = Vault.Create("4321", Fast);
        var key = Vault.TryUnlock(record, "4321");

        Assert.IsNotNull(key);
        string stored = key.Encrypt("secret \U0001F600 \u0111\u1EADm");

        Assert.IsTrue(VaultKey.IsEncrypted(stored));
        Assert.IsFalse(stored.Contains("secret"));
        Assert.AreEqual("secret \U0001F600 \u0111\u1EADm", key.Decrypt(stored));
    }

    [TestMethod]
    public void AWrongPin_DoesNotOpenTheVault()
    {
        var record = Vault.Create("4321", Fast);

        Assert.IsNull(Vault.TryUnlock(record, "4322"));
        Assert.IsNull(Vault.TryUnlock(record, ""));
    }

    [TestMethod]
    public void TheSameTextEncryptedTwice_LooksDifferent()
    {
        var key = Vault.TryUnlock(Vault.Create("4321", Fast), "4321")!;

        Assert.AreNotEqual(key.Encrypt("same"), key.Encrypt("same"));
    }

    [TestMethod]
    public void EmptyText_RoundTrips()
    {
        var key = Vault.TryUnlock(Vault.Create("4321", Fast), "4321")!;

        Assert.AreEqual("", key.Decrypt(key.Encrypt("")));
    }

    [TestMethod]
    public void ChangedCipherText_IsDetected()
    {
        var key = Vault.TryUnlock(Vault.Create("4321", Fast), "4321")!;
        string stored = key.Encrypt("do not change me");
        byte[] bytes = Convert.FromBase64String(stored[VaultKey.Prefix.Length..]);
        bytes[^1] ^= 0x01;
        string tampered = VaultKey.Prefix + Convert.ToBase64String(bytes);

        Assert.Throws<CryptographicException>(() => key.Decrypt(tampered));
    }

    [TestMethod]
    public void TextFromAnotherVault_CannotBeRead()
    {
        var one = Vault.TryUnlock(Vault.Create("1111", Fast), "1111")!;
        var two = Vault.TryUnlock(Vault.Create("1111", Fast), "1111")!;

        Assert.Throws<CryptographicException>(() => two.Decrypt(one.Encrypt("x")));
    }

    [TestMethod]
    public void PlainText_IsNotEncrypted_AndDecryptRefusesIt()
    {
        var key = Vault.TryUnlock(Vault.Create("1111", Fast), "1111")!;

        Assert.IsFalse(VaultKey.IsEncrypted("hello"));
        Assert.ThrowsExactly<FormatException>(() => key.Decrypt("hello"));
    }

    [TestMethod]
    public void ChangingThePin_KeepsEarlierTextReadable()
    {
        var record = Vault.Create("1111", Fast);
        string stored = Vault.TryUnlock(record, "1111")!.Encrypt("kept");

        var changed = Vault.ChangePin(record, "1111", "2222", Fast);

        Assert.IsNotNull(changed);
        Assert.IsNull(Vault.TryUnlock(changed, "1111"), "the old PIN no longer works");
        Assert.AreEqual("kept", Vault.TryUnlock(changed, "2222")!.Decrypt(stored));
    }

    [TestMethod]
    public void ChangingThePin_NeedsTheOldPin()
    {
        var record = Vault.Create("1111", Fast);

        Assert.IsNull(Vault.ChangePin(record, "9999", "2222", Fast));
        Assert.IsNotNull(Vault.TryUnlock(record, "1111"), "the vault is unchanged");
    }

    [TestMethod]
    public void ADamagedRecord_DoesNotOpen()
    {
        Assert.IsNull(Vault.TryUnlock(new VaultRecord("???", Fast, "***"), "1234"));
        Assert.IsNull(Vault.TryUnlock(new VaultRecord(Convert.ToBase64String(new byte[16]), Fast, Convert.ToBase64String(new byte[5])), "1234"));
    }
}

[TestClass]
public class SecurityStoreTests
{
    private string _folder = "";

    [TestInitialize]
    public void Setup() => _folder = Path.Combine(Path.GetTempPath(), "mycustomnote-security-tests", Guid.NewGuid().ToString());

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [TestMethod]
    public void MissingFile_MeansNoPins()
    {
        var store = new SecurityStore(Path.Combine(_folder, "security.json"));

        var data = store.Load();

        Assert.IsNull(data.AppPin);
        Assert.IsNull(data.NotesVault);
        Assert.AreEqual(5, data.LockAfterMinutes);
        Assert.IsFalse(store.FileIsDamaged);
    }

    [TestMethod]
    public void SavedData_IsReadBack_AndStillWorks()
    {
        var store = new SecurityStore(Path.Combine(_folder, "security.json"));
        var data = new SecurityData
        {
            AppPin = PinHasher.Create("1234", 1000),
            NotesVault = Vault.Create("5678", 1000),
            LockAfterMinutes = 15,
        };

        store.Save(data);
        var back = store.Load();

        Assert.AreEqual(data, back);
        Assert.IsTrue(PinHasher.Verify("1234", back.AppPin!));
        Assert.IsNotNull(Vault.TryUnlock(back.NotesVault!, "5678"));
        Assert.IsFalse(File.Exists(store.Path + ".tmp"));
    }

    [TestMethod]
    public void TheFile_NeverContainsAPin()
    {
        var store = new SecurityStore(Path.Combine(_folder, "security.json"));

        store.Save(new SecurityData { AppPin = PinHasher.Create("246810", 1000), NotesVault = Vault.Create("135790", 1000) });

        string text = File.ReadAllText(store.Path);
        Assert.IsFalse(text.Contains("246810"));
        Assert.IsFalse(text.Contains("135790"));
    }

    [TestMethod]
    public void ADamagedFile_IsReportedAndGivesNoPins()
    {
        var store = new SecurityStore(Path.Combine(_folder, "security.json"));
        Directory.CreateDirectory(_folder);
        File.WriteAllText(store.Path, "{ broken");

        var data = store.Load();

        Assert.IsTrue(store.FileIsDamaged);
        Assert.IsNull(data.AppPin);
    }
}
