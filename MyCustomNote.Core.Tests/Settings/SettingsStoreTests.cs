using Microsoft.VisualStudio.TestTools.UnitTesting;
using MyCustomNote.Core.Models;
using MyCustomNote.Core.Settings;

namespace MyCustomNote.Core.Tests.Settings;

[TestClass]
public class SettingsStoreTests
{
    private string _folder = "";

    [TestInitialize]
    public void Setup() =>
        _folder = Path.Combine(Path.GetTempPath(), "mycustomnote-settings-tests", Guid.NewGuid().ToString());

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private SettingsStore Store(string name = "settings.json") => new(Path.Combine(_folder, name));

    [TestMethod]
    public void MissingFile_GivesTheDefaults()
    {
        var settings = Store().Load();

        Assert.AreEqual(new AppSettings(), settings);
        Assert.AreEqual(ThemeOption.FollowWindows, settings.Theme);
        Assert.AreEqual(SortOption.DateModified, settings.Sort);
        Assert.AreEqual(NoteFont.System, settings.Font);
        Assert.IsNull(settings.BodyTextColor);
    }

    [TestMethod]
    public void SavedSettings_AreReadBackExactly()
    {
        var store = Store();
        var settings = new AppSettings
        {
            Theme = ThemeOption.Sepia,
            Sort = SortOption.Title,
            Font = NoteFont.Monospace,
            BodyTextColor = "#1A237E",
            HeadingTextColor = "#B71C1C",
            Window = new WindowSettings
            {
                Left = 10.5, Top = 20, Width = 1000, Height = 700, Maximized = true, LeftPaneWidth = 230, MiddlePaneWidth = 340,
            },
        };

        store.Save(settings);

        Assert.AreEqual(settings, store.Load());
    }

    [TestMethod]
    public void Save_CreatesTheFolder_AndReplacesAnOldFile_WithoutLeavingATemporaryFile()
    {
        var store = Store();

        store.Save(new AppSettings { Theme = ThemeOption.Dark });
        store.Save(new AppSettings { Theme = ThemeOption.Ocean });

        Assert.AreEqual(ThemeOption.Ocean, store.Load().Theme);
        Assert.IsFalse(File.Exists(store.Path + ".tmp"));
    }

    [TestMethod]
    public void TheFile_UsesReadableNamesForTheEnums()
    {
        var store = Store();

        store.Save(new AppSettings { Theme = ThemeOption.FollowWindows, Sort = SortOption.LastOpened });
        string text = File.ReadAllText(store.Path);

        StringAssert.Contains(text, "\"theme\": \"FollowWindows\"");
        StringAssert.Contains(text, "\"sort\": \"LastOpened\"");
    }

    [TestMethod]
    public void DamagedFile_GivesTheDefaults()
    {
        var store = Store();
        Directory.CreateDirectory(_folder);
        File.WriteAllText(store.Path, "{ this is not json");

        Assert.AreEqual(new AppSettings(), store.Load());
    }

    [TestMethod]
    public void UnknownThemeName_GivesTheDefaults()
    {
        var store = Store();
        Directory.CreateDirectory(_folder);
        File.WriteAllText(store.Path, "{ \"theme\": \"Neon\" }");

        Assert.AreEqual(new AppSettings(), store.Load());
    }

    [TestMethod]
    public void PartialFile_KeepsWhatIsThereAndDefaultsTheRest()
    {
        var store = Store();
        Directory.CreateDirectory(_folder);
        File.WriteAllText(store.Path, "{ \"theme\": \"Forest\", \"window\": { \"width\": 900 } }");

        var settings = store.Load();

        Assert.AreEqual(ThemeOption.Forest, settings.Theme);
        Assert.AreEqual(SortOption.DateModified, settings.Sort);
        Assert.AreEqual(900, settings.Window.Width);
        Assert.IsNull(settings.Window.Height);
    }

    [TestMethod]
    public void EmptyFile_GivesTheDefaults()
    {
        var store = Store();
        Directory.CreateDirectory(_folder);
        File.WriteAllText(store.Path, "");

        Assert.AreEqual(new AppSettings(), store.Load());
    }

    [TestMethod]
    public void FileWithTheWordNull_GivesTheDefaults()
    {
        var store = Store();
        Directory.CreateDirectory(_folder);
        File.WriteAllText(store.Path, "null");

        Assert.AreEqual(new AppSettings(), store.Load());
    }
}
