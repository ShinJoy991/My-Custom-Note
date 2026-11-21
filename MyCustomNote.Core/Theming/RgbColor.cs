using System.Globalization;

namespace MyCustomNote.Core.Theming;

/// <summary>An opaque colour. Kept free of WPF types so that the theme rules can be unit tested.</summary>
public readonly record struct RgbColor(byte R, byte G, byte B)
{
    /// <summary>Reads <c>#RRGGBB</c> or <c>RRGGBB</c>; also <c>#AARRGGBB</c> (the alpha part is ignored).</summary>
    public static bool TryParse(string? text, out RgbColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string digits = text.Trim().TrimStart('#');
        if (digits.Length == 8)
        {
            digits = digits[2..];
        }

        if (digits.Length != 6
            || !byte.TryParse(digits.AsSpan(0, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out byte r)
            || !byte.TryParse(digits.AsSpan(2, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out byte g)
            || !byte.TryParse(digits.AsSpan(4, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out byte b))
        {
            return false;
        }

        color = new RgbColor(r, g, b);
        return true;
    }

    /// <summary>The colour, or null if the text is empty or not a colour.</summary>
    public static RgbColor? ParseOrNull(string? text) => TryParse(text, out var color) ? color : null;

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>WCAG relative luminance, 0 (black) to 1 (white).</summary>
    public double RelativeLuminance => (0.2126 * Channel(R)) + (0.7152 * Channel(G)) + (0.0722 * Channel(B));

    /// <summary>Moves <paramref name="from"/> towards <paramref name="to"/>: amount 0 gives <paramref name="from"/>, 1 gives <paramref name="to"/>.</summary>
    public static RgbColor Mix(RgbColor from, RgbColor to, double amount) => new(
        MixChannel(from.R, to.R, amount),
        MixChannel(from.G, to.G, amount),
        MixChannel(from.B, to.B, amount));

    private static byte MixChannel(byte from, byte to, double amount) =>
        (byte)Math.Round(from + ((to - from) * Math.Clamp(amount, 0, 1)));

    private static double Channel(byte value)
    {
        double c = value / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}

/// <summary>The WCAG contrast rules used to keep a user's text colour readable on every theme (as the Android app does).</summary>
public static class Contrast
{
    /// <summary>The smallest accepted contrast ratio for a custom text colour (the same threshold as Android).</summary>
    public const double MinimumRatio = 3.5;

    /// <summary>The contrast ratio of two colours, 1 (identical) to 21 (black on white).</summary>
    public static double Ratio(RgbColor a, RgbColor b)
    {
        double l1 = a.RelativeLuminance + 0.05;
        double l2 = b.RelativeLuminance + 0.05;
        return l1 > l2 ? l1 / l2 : l2 / l1;
    }

    /// <summary>The custom colour if it is readable on the background, otherwise the fallback (the theme's own text colour).</summary>
    public static RgbColor LegibleOrFallback(RgbColor? custom, RgbColor background, RgbColor fallback) =>
        custom is { } color && Ratio(color, background) >= MinimumRatio ? color : fallback;
}
