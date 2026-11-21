using System.Windows.Media;
using MyCustomNote.Core.Models;
using MyCustomNote.Core.Theming;

namespace MyCustomNote.Services;

/// <summary>The color of each <see cref="NoteColorTag"/> as shown in the list (a stripe on the card) and in menus.</summary>
internal static class NoteColors
{
    private static readonly Dictionary<NoteColorTag, RgbColor> Palette = new()
    {
        [NoteColorTag.Red] = new RgbColor(0xE5, 0x39, 0x35),
        [NoteColorTag.Orange] = new RgbColor(0xFB, 0x8C, 0x00),
        [NoteColorTag.Yellow] = new RgbColor(0xFD, 0xD8, 0x35),
        [NoteColorTag.Green] = new RgbColor(0x43, 0xA0, 0x47),
        [NoteColorTag.Blue] = new RgbColor(0x1E, 0x88, 0xE5),
        [NoteColorTag.Purple] = new RgbColor(0x8E, 0x24, 0xAA),
    };

    /// <summary>The brush of a color tag; transparent for no tag or <see cref="NoteColorTag.Default"/>.</summary>
    public static Brush BrushFor(NoteColorTag? tag)
    {
        if (tag is { } value && Palette.TryGetValue(value, out var color))
        {
            var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
            brush.Freeze();
            return brush;
        }

        return Brushes.Transparent;
    }
}
