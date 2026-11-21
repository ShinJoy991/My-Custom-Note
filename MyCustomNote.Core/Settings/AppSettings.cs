using System.Text.Json;
using System.Text.Json.Serialization;
using MyCustomNote.Core.Models;

namespace MyCustomNote.Core.Settings;

/// <summary>Where the main window was and how wide its side panes were, so the app opens as it was closed.</summary>
public sealed record WindowSettings
{
    public double? Left { get; init; }

    public double? Top { get; init; }

    public double? Width { get; init; }

    public double? Height { get; init; }

    public bool Maximized { get; init; }

    public double? LeftPaneWidth { get; init; }

    public double? MiddlePaneWidth { get; init; }
}

/// <summary>The app preferences. Every field has a default, so a missing or partial file still gives a usable result.</summary>
public sealed record AppSettings
{
    public ThemeOption Theme { get; init; } = ThemeOption.FollowWindows;

    public SortOption Sort { get; init; } = SortOption.DateModified;

    public NoteFont Font { get; init; } = NoteFont.System;

    /// <summary>Notes in the trash for longer than this many days are deleted for good when the app starts; 0 means never.</summary>
    public int TrashRetentionDays { get; init; } = 30;

    /// <summary>A custom colour for note text as <c>#RRGGBB</c>, or null for the theme's own colour.</summary>
    public string? BodyTextColor { get; init; }

    /// <summary>A custom colour for titles and headings as <c>#RRGGBB</c>, or null for the theme's own colour.</summary>
    public string? HeadingTextColor { get; init; }

    public WindowSettings Window { get; init; } = new();
}

/// <summary>
/// Reads and writes <see cref="AppSettings"/> as a JSON file. Reading never throws: a missing, unreadable or
/// damaged file gives the default settings (losing a preference is better than the app not starting).
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public SettingsStore(string path) => Path = path;

    public string Path { get; }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(Path))
            {
                return new AppSettings();
            }

            using var stream = File.OpenRead(Path);
            return JsonSerializer.Deserialize<AppSettings>(stream, Options) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return new AppSettings();
        }
    }

    /// <summary>Writes the file (through a temporary file, so a crash cannot leave half a file). May throw an <see cref="IOException"/>.</summary>
    public void Save(AppSettings settings)
    {
        string? directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporary = Path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
        File.Move(temporary, Path, overwrite: true);
    }
}
