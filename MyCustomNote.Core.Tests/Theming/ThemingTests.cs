using Microsoft.VisualStudio.TestTools.UnitTesting;
using MyCustomNote.Core.Models;
using MyCustomNote.Core.Theming;

namespace MyCustomNote.Core.Tests.Theming;

[TestClass]
public class RgbColorTests
{
    private static readonly RgbColor Black = new(0, 0, 0);
    private static readonly RgbColor White = new(255, 255, 255);

    [TestMethod]
    [DataRow("#FF8000", 255, 128, 0)]
    [DataRow("ff8000", 255, 128, 0)]
    [DataRow("  #ff8000  ", 255, 128, 0)]
    [DataRow("#80FF8000", 255, 128, 0)]
    [DataRow("#000000", 0, 0, 0)]
    public void Parse_AcceptsSixAndEightDigitHex(string text, int r, int g, int b)
    {
        Assert.IsTrue(RgbColor.TryParse(text, out var color));
        Assert.AreEqual(new RgbColor((byte)r, (byte)g, (byte)b), color);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("#FFF")]
    [DataRow("#GG0000")]
    [DataRow("#FF80001")]
    [DataRow("red")]
    public void Parse_RejectsEverythingElse(string? text)
    {
        Assert.IsFalse(RgbColor.TryParse(text, out _));
        Assert.IsNull(RgbColor.ParseOrNull(text));
    }

    [TestMethod]
    public void Parse_RejectsNull()
    {
        Assert.IsFalse(RgbColor.TryParse(null, out _));
        Assert.IsNull(RgbColor.ParseOrNull(null));
    }

    [TestMethod]
    public void ToHex_WritesUppercaseSixDigits_AndParsesBack()
    {
        var color = new RgbColor(1, 171, 255);

        Assert.AreEqual("#01ABFF", color.ToHex());
        Assert.AreEqual(color, RgbColor.ParseOrNull(color.ToHex()));
    }

    [TestMethod]
    public void RelativeLuminance_IsZeroForBlackAndOneForWhite()
    {
        Assert.AreEqual(0.0, Black.RelativeLuminance, 1e-9);
        Assert.AreEqual(1.0, White.RelativeLuminance, 1e-9);
    }

    [TestMethod]
    public void Ratio_IsTwentyOneForBlackOnWhite_AndOneForTheSameColor()
    {
        Assert.AreEqual(21.0, Contrast.Ratio(Black, White), 1e-9);
        Assert.AreEqual(21.0, Contrast.Ratio(White, Black), 1e-9, "the order does not matter");
        Assert.AreEqual(1.0, Contrast.Ratio(White, White), 1e-9);
        Assert.AreEqual(1.0, Contrast.Ratio(new RgbColor(90, 40, 200), new RgbColor(90, 40, 200)), 1e-9);
    }

    [TestMethod]
    public void Mix_MovesBetweenTheTwoColours()
    {
        Assert.AreEqual(Black, RgbColor.Mix(Black, White, 0));
        Assert.AreEqual(White, RgbColor.Mix(Black, White, 1));
        Assert.AreEqual(new RgbColor(128, 128, 128), RgbColor.Mix(Black, White, 0.5));
        Assert.AreEqual(White, RgbColor.Mix(Black, White, 7), "the amount is limited to 0..1");
        Assert.AreEqual(Black, RgbColor.Mix(Black, White, -3));
    }

    [TestMethod]
    public void LegibleOrFallback_KeepsAReadableColour()
    {
        var chosen = new RgbColor(0x1A, 0x23, 0x7E); // dark blue

        Assert.AreEqual(chosen, Contrast.LegibleOrFallback(chosen, White, Black));
    }

    [TestMethod]
    public void LegibleOrFallback_ReplacesAColourThatIsHardToRead()
    {
        var lightGrey = new RgbColor(0xE0, 0xE0, 0xE0);
        var fallback = new RgbColor(0x10, 0x10, 0x10);

        Assert.AreEqual(fallback, Contrast.LegibleOrFallback(lightGrey, White, fallback));
        Assert.AreEqual(fallback, Contrast.LegibleOrFallback(White, White, fallback));
    }

    [TestMethod]
    public void LegibleOrFallback_WithoutACustomColour_GivesTheFallback()
    {
        var fallback = new RgbColor(5, 6, 7);

        Assert.AreEqual(fallback, Contrast.LegibleOrFallback(null, White, fallback));
    }

    [TestMethod]
    public void LegibleOrFallback_TheSameColourCanBeReadableOnOneThemeAndNotOnAnother()
    {
        var white = White;
        var lightTheme = ThemePalettes.For(ThemeOption.Light, false);
        var darkTheme = ThemePalettes.For(ThemeOption.Dark, true);

        Assert.AreEqual(lightTheme.Text, Contrast.LegibleOrFallback(white, lightTheme.Background, lightTheme.Text));
        Assert.AreEqual(white, Contrast.LegibleOrFallback(white, darkTheme.Background, darkTheme.Text));
    }
}

[TestClass]
public class ThemePalettesTests
{
    public static IEnumerable<object[]> AllThemes() =>
        Enum.GetValues<ThemeOption>().Select(option => new object[] { option });

    [TestMethod]
    [DynamicData(nameof(AllThemes))]
    public void EveryTheme_HasReadableTextOnEveryPane(ThemeOption option)
    {
        var palette = ThemePalettes.For(option, windowsIsDark: false);

        foreach (var pane in new[] { palette.Background, palette.PaneLeft, palette.PaneMiddle })
        {
            Assert.IsTrue(Contrast.Ratio(palette.Text, pane) >= 7, $"{option}: text {Contrast.Ratio(palette.Text, pane):F2}");
            Assert.IsTrue(Contrast.Ratio(palette.TextMuted, pane) >= 4.5, $"{option}: muted text {Contrast.Ratio(palette.TextMuted, pane):F2}");
        }
    }

    [TestMethod]
    [DynamicData(nameof(AllThemes))]
    public void EveryTheme_HasReadableTextOnThePrimaryColour(ThemeOption option)
    {
        var palette = ThemePalettes.For(option, windowsIsDark: false);

        Assert.IsTrue(Contrast.Ratio(palette.OnPrimary, palette.Primary) >= 4.5, $"{option}: {Contrast.Ratio(palette.OnPrimary, palette.Primary):F2}");
    }

    [TestMethod]
    [DynamicData(nameof(AllThemes))]
    public void EveryTheme_HasABorderThatDiffersFromTheBackground(ThemeOption option)
    {
        var palette = ThemePalettes.For(option, windowsIsDark: false);

        Assert.AreNotEqual(palette.Background, palette.Border);
        Assert.IsTrue(Contrast.Ratio(palette.Border, palette.Background) >= 1.2);
    }

    [TestMethod]
    public void FollowWindows_UsesLightOrDarkAccordingToWindows()
    {
        Assert.AreEqual(ThemePalettes.For(ThemeOption.Light, false), ThemePalettes.For(ThemeOption.FollowWindows, false));
        Assert.AreEqual(ThemePalettes.For(ThemeOption.Dark, true), ThemePalettes.For(ThemeOption.FollowWindows, true));
    }

    [TestMethod]
    public void OnlyTheDarkThemeIsDark_AndItIsTheOnlyOneWithLightText()
    {
        foreach (var option in Enum.GetValues<ThemeOption>().Where(o => o != ThemeOption.FollowWindows))
        {
            var palette = ThemePalettes.For(option, false);

            Assert.AreEqual(option == ThemeOption.Dark, palette.IsDark, option.ToString());
            Assert.AreEqual(palette.IsDark, palette.Text.RelativeLuminance > palette.Background.RelativeLuminance, option.ToString());
        }
    }

    [TestMethod]
    public void TheFivePalettes_AreTheAndroidColours()
    {
        Assert.AreEqual("#FFFBFE", ThemePalettes.For(ThemeOption.Light, false).Background.ToHex());
        Assert.AreEqual("#1C1B1F", ThemePalettes.For(ThemeOption.Dark, false).Background.ToHex());
        Assert.AreEqual("#F4ECD8", ThemePalettes.For(ThemeOption.Sepia, false).Background.ToHex());
        Assert.AreEqual("#EFF3E8", ThemePalettes.For(ThemeOption.Forest, false).Background.ToHex());
        Assert.AreEqual("#E8F4F5", ThemePalettes.For(ThemeOption.Ocean, false).Background.ToHex());
        Assert.AreEqual("#6650A4", ThemePalettes.For(ThemeOption.Light, false).Primary.ToHex());
        Assert.AreEqual("#D0BCFF", ThemePalettes.For(ThemeOption.Dark, false).Primary.ToHex());
    }
}
