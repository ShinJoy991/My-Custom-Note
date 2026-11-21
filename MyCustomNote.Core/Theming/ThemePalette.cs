using MyCustomNote.Core.Models;

namespace MyCustomNote.Core.Theming;

/// <summary>
/// The colours of one theme. Only five are chosen by hand (taken from the Android palettes); the others are
/// mixed from them so that every theme gets the same relationships (panes slightly darker towards the left,
/// muted text that stays readable, borders that are visible but quiet).
/// </summary>
public sealed record ThemePalette(bool IsDark, RgbColor Background, RgbColor Text, RgbColor Primary, RgbColor OnPrimary)
{
    public RgbColor PaneLeft => RgbColor.Mix(Background, Text, 0.06);

    public RgbColor PaneMiddle => RgbColor.Mix(Background, Text, 0.025);

    public RgbColor TextMuted => RgbColor.Mix(Text, Background, 0.25);

    public RgbColor Border => RgbColor.Mix(Background, Text, 0.18);
}

public static class ThemePalettes
{
    private static readonly ThemePalette Light = new(false, Rgb(0xFFFBFE), Rgb(0x1C1B1F), Rgb(0x6650A4), Rgb(0xFFFFFF));
    private static readonly ThemePalette Dark = new(true, Rgb(0x1C1B1F), Rgb(0xE6E1E5), Rgb(0xD0BCFF), Rgb(0x381E72));
    private static readonly ThemePalette Sepia = new(false, Rgb(0xF4ECD8), Rgb(0x3B2F22), Rgb(0x8A5A33), Rgb(0xFFFFFF));
    private static readonly ThemePalette Forest = new(false, Rgb(0xEFF3E8), Rgb(0x243025), Rgb(0x3F6B4A), Rgb(0xFFFFFF));
    private static readonly ThemePalette Ocean = new(false, Rgb(0xE8F4F5), Rgb(0x132A2C), Rgb(0x1F6F78), Rgb(0xFFFFFF));

    /// <summary>The palette of a theme. <see cref="ThemeOption.FollowWindows"/> uses Light or Dark depending on the Windows setting.</summary>
    public static ThemePalette For(ThemeOption option, bool windowsIsDark) => option switch
    {
        ThemeOption.Light => Light,
        ThemeOption.Dark => Dark,
        ThemeOption.Sepia => Sepia,
        ThemeOption.Forest => Forest,
        ThemeOption.Ocean => Ocean,
        ThemeOption.FollowWindows => windowsIsDark ? Dark : Light,
        _ => throw new ArgumentOutOfRangeException(nameof(option), option, null),
    };

    private static RgbColor Rgb(int rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
