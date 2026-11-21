using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MyCustomNote.Core.Models;
using MyCustomNote.Core.Settings;
using MyCustomNote.Core.Theming;
using MyCustomNote.Services;

namespace MyCustomNote;

/// <summary>Theme, font and text colors. Every choice is applied and saved at once; the window itself shows the result.</summary>
public partial class SettingsWindow : Window
{
    private static readonly (string Label, ThemeOption Value)[] Themes =
    [
        ("Light", ThemeOption.Light),
        ("Dark", ThemeOption.Dark),
        ("Sepia", ThemeOption.Sepia),
        ("Forest", ThemeOption.Forest),
        ("Ocean", ThemeOption.Ocean),
        ("Follow Windows", ThemeOption.FollowWindows),
    ];

    private static readonly (string Label, NoteFont Value)[] Fonts =
    [
        ("System", NoteFont.System),
        ("Serif", NoteFont.Serif),
        ("Sans serif", NoteFont.SansSerif),
        ("Monospace", NoteFont.Monospace),
    ];

    // The null entry means "no custom color: use the theme's own".
    private static readonly (string Label, string? Hex)[] Colors =
    [
        ("Theme default", null),
        ("Black", "#000000"),
        ("Dark gray", "#424242"),
        ("Navy", "#1A237E"),
        ("Green", "#1B5E20"),
        ("Dark red", "#B71C1C"),
        ("Brown", "#4E342E"),
        ("White", "#FFFFFF"),
        ("Light gray", "#E0E0E0"),
    ];

    private readonly SettingsService _settings;
    private readonly SecurityService _security;
    private readonly Action _saveOpenNote;

    private static readonly (string Label, int Value)[] TrashChoices =
    [
        ("7 days", 7),
        ("30 days", 30),
        ("90 days", 90),
        ("Never", 0),
    ];

    private static readonly (string Label, int Value)[] LockAfterChoices =
    [
        ("1 minute", 1),
        ("5 minutes", 5),
        ("15 minutes", 15),
        ("30 minutes", 30),
        ("Only when minimized", 0),
    ];

    internal SettingsWindow(SettingsService settings, SecurityService security, Action saveOpenNote)
    {
        InitializeComponent();
        _settings = settings;
        _security = security;
        _saveOpenNote = saveOpenNote;

        AddChoices(TrashPanel, "trash", TrashChoices, s => s.TrashRetentionDays, (s, value) => s with { TrashRetentionDays = value });
        BuildSecurity();
        AddChoices(ThemePanel, "theme", Themes, s => s.Theme, (s, value) => s with { Theme = value });
        AddChoices(FontPanel, "font", Fonts, s => s.Font, (s, value) => s with { Font = value });
        AddSwatches(BodyColorPanel, s => s.BodyTextColor, (s, hex) => s with { BodyTextColor = hex });
        AddSwatches(HeadingColorPanel, s => s.HeadingTextColor, (s, hex) => s with { HeadingTextColor = hex });
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ---------- security ----------

    private void BuildSecurity()
    {
        SecurityPanel.Children.Clear();

        bool encrypted = _security.Data.DatabaseEncrypted;
        AddSecurityText(
            encrypted ? "App password: on (it is also the database password)" : _security.HasAppPin ? "App PIN: on" : "App PIN: off",
            bold: true);
        if (!_security.HasAppPin)
        {
            AddSecurityButton("Set app PIN...", () => PinDialogs.SetAppPin(this, _security));
        }
        else
        {
            if (!encrypted)
            {
                AddSecurityButton("Change app PIN...", () => PinDialogs.ChangeAppPin(this, _security));
                AddSecurityButton("Remove app PIN...", () => PinDialogs.RemoveAppPin(this, _security));
            }

            AddSecurityText("Ask for the PIN again after a time without use:");
            var group = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            foreach (var (label, minutes) in LockAfterChoices)
            {
                var button = new RadioButton
                {
                    Content = label,
                    GroupName = "lockafter",
                    Margin = new Thickness(0, 0, 18, 6),
                    IsChecked = _security.Data.LockAfterMinutes == minutes,
                };
                int value = minutes;
                button.Checked += (_, _) =>
                {
                    try
                    {
                        _security.SetLockAfterMinutes(value);
                    }
                    catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
                    {
                        MessageBox.Show(this, "The setting could not be saved:\n\n" + ex.Message, "MyCustomNote");
                    }
                };
                group.Children.Add(button);
            }

            SecurityPanel.Children.Add(group);
        }

        AddSecurityText(
            encrypted
                ? "Database encryption: ON. Every note, title, tag and folder is encrypted in the file notes.db."
                : "Database encryption: off. The file notes.db is a normal file: programs and people with access to this PC can read it, except locked notes.",
            bold: true);
        if (encrypted)
        {
            AddSecurityButton("Change password...", () => PinDialogs.ChangeDatabasePassword(this, _security, _saveOpenNote));
            AddSecurityButton("Turn off encryption...", () => PinDialogs.DecryptDatabase(this, _security, _saveOpenNote));
            AddSecurityText("A forgotten password cannot be recovered: all notes would be lost. Keep a copy of the password somewhere safe.");
        }
        else
        {
            AddSecurityButton("Encrypt the database...", () => PinDialogs.EncryptDatabase(this, _security, _saveOpenNote));
        }

        AddSecurityText(_security.HasNotesPin
            ? "Notes PIN: set (for locked and hidden notes)"
            : "Notes PIN: not set yet (you create it the first time you lock or hide a note)", bold: true);
        if (_security.HasNotesPin)
        {
            AddSecurityButton("Change notes PIN...", () => PinDialogs.ChangeNotesPin(this, _security));
        }

        AddSecurityText("The notes PIN cannot be removed or reset. Locked notes are encrypted with it, so if you forget it they cannot be read again.");
    }

    private void AddSecurityText(string text, bool bold = false)
    {
        var block = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = bold ? 13 : 12,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Margin = new Thickness(0, 4, 0, 6),
        };
        if (!bold)
        {
            block.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        }

        SecurityPanel.Children.Add(block);
    }

    private void AddSecurityButton(string label, Action action)
    {
        var button = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 0, 6) };
        button.Click += (_, _) =>
        {
            action();
            BuildSecurity(); // the buttons depend on what is set now
        };
        SecurityPanel.Children.Add(button);
    }

    private void AddChoices<T>(
        Panel panel,
        string group,
        IEnumerable<(string Label, T Value)> choices,
        Func<AppSettings, T> current,
        Func<AppSettings, T, AppSettings> apply)
    {
        foreach (var (label, value) in choices)
        {
            var button = new RadioButton
            {
                Content = label,
                GroupName = group,
                Margin = new Thickness(0, 0, 18, 6),
                IsChecked = EqualityComparer<T>.Default.Equals(current(_settings.Current), value),
            };

            // Attached after IsChecked is set, so showing the current choice does not count as a change.
            button.Checked += (_, _) => _settings.Update(s => apply(s, value));
            panel.Children.Add(button);
        }
    }

    private void AddSwatches(Panel panel, Func<AppSettings, string?> current, Func<AppSettings, string?, AppSettings> apply)
    {
        foreach (var (label, hex) in Colors)
        {
            var swatch = new Border
            {
                Width = hex is null ? 74 : 32,
                Height = 32,
                Margin = new Thickness(0, 0, 8, 8),
                CornerRadius = new CornerRadius(4),
                Cursor = Cursors.Hand,
                Tag = hex,
                ToolTip = label,
            };

            if (RgbColor.ParseOrNull(hex) is { } color)
            {
                swatch.Background = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
            }
            else
            {
                swatch.SetResourceReference(Border.BackgroundProperty, "Brush.Window");
                var text = new TextBlock
                {
                    Text = "Auto",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 12,
                };
                text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
                swatch.Child = text;
            }

            swatch.MouseLeftButtonUp += (_, _) =>
            {
                _settings.Update(s => apply(s, hex));
                MarkSelectedSwatch(panel, hex);
            };

            panel.Children.Add(swatch);
        }

        MarkSelectedSwatch(panel, current(_settings.Current));
    }

    /// <summary>The chosen swatch gets a thick border in the theme's primary color, the others a thin quiet one.</summary>
    private static void MarkSelectedSwatch(Panel panel, string? selectedHex)
    {
        foreach (Border swatch in panel.Children)
        {
            bool selected = string.Equals((string?)swatch.Tag, selectedHex, StringComparison.OrdinalIgnoreCase);
            swatch.BorderThickness = new Thickness(selected ? 3 : 1);
            swatch.SetResourceReference(Border.BorderBrushProperty, selected ? "Brush.Primary" : "Brush.Border");
        }
    }
}
