using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using MyCustomNote.Core.Models;
using MyCustomNote.Core.Settings;
using MyCustomNote.Core.Theming;

namespace MyCustomNote.Services;

/// <summary>
/// Puts the chosen theme on the running app: the Fluent control theme (light or dark) for buttons, scroll bars and
/// so on, plus our own brushes (<c>Brush.*</c>) and the note font (<c>Font.Note</c>) as application resources.
/// The XAML uses them with DynamicResource, so a change shows at once. The palettes and the contrast rule live in
/// Core and are unit tested; this class only turns them into WPF objects.
/// </summary>
internal sealed class ThemeManager
{
    private readonly SettingsService _settings;
    private bool? _fluentIsDark;

    public ThemeManager(SettingsService settings)
    {
        _settings = settings;
        _settings.Changed += (_, _) => Apply();

        // Windows tells us when the user switches between light and dark mode; only "Follow Windows" cares.
        SystemEvents.UserPreferenceChanged += (_, _) =>
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                if (_settings.Current.Theme == ThemeOption.FollowWindows)
                {
                    Apply();
                }
            });
    }

    /// <summary>The colours that text in notes really gets after the contrast rule (for the settings preview and for tests by eye).</summary>
    public (RgbColor Body, RgbColor Heading) EffectiveTextColors()
    {
        var settings = _settings.Current;
        var palette = ThemePalettes.For(settings.Theme, WindowsIsDark());
        return (
            Contrast.LegibleOrFallback(RgbColor.ParseOrNull(settings.BodyTextColor), palette.Background, palette.Text),
            Contrast.LegibleOrFallback(RgbColor.ParseOrNull(settings.HeadingTextColor), palette.Background, palette.Text));
    }

    public void Apply()
    {
        var settings = _settings.Current;
        var palette = ThemePalettes.For(settings.Theme, WindowsIsDark());
        var (body, heading) = EffectiveTextColors();

        var application = Application.Current;
        if (_fluentIsDark != palette.IsDark)
        {
            application.ThemeMode = palette.IsDark ? ThemeMode.Dark : ThemeMode.Light;
            _fluentIsDark = palette.IsDark;
        }

        var resources = application.Resources;
        resources["Brush.Window"] = Brush(palette.Background);
        resources["Brush.PaneLeft"] = Brush(palette.PaneLeft);
        resources["Brush.PaneMiddle"] = Brush(palette.PaneMiddle);
        resources["Brush.Text"] = Brush(palette.Text);
        resources["Brush.TextMuted"] = Brush(palette.TextMuted);
        resources["Brush.Border"] = Brush(palette.Border);
        resources["Brush.Primary"] = Brush(palette.Primary);
        resources["Brush.OnPrimary"] = Brush(palette.OnPrimary);
        resources["Brush.BodyText"] = Brush(body);
        resources["Brush.HeadingText"] = Brush(heading);
        resources["Font.Note"] = FontFor(settings.Font);
    }

    private static SolidColorBrush Brush(RgbColor color)
    {
        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private static FontFamily FontFor(NoteFont font) => font switch
    {
        NoteFont.Serif => new FontFamily("Georgia"),
        NoteFont.SansSerif => new FontFamily("Arial"),
        NoteFont.Monospace => new FontFamily("Consolas"),
        _ => SystemFonts.MessageFontFamily,
    };

    private static bool WindowsIsDark()
    {
        // AppsUseLightTheme is 0 when Windows apps are set to dark mode; if the value is missing we assume light.
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }
}
