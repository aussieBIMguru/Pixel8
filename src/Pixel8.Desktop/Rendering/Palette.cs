using Microsoft.Xna.Framework;

namespace Pixel8.Desktop.Rendering;

/// <summary>
/// Locked 16-colour palette (PICO-8 values): the 8-bit look.
/// All art and UI should use only these, so curse/darkness palette-swaps stay trivial later.
/// Swap for Endesga 32 or similar when the art direction firms up.
/// </summary>
public static class Palette
{
    public static readonly Color Black = Hex(0x000000);
    public static readonly Color DarkBlue = Hex(0x1D2B53);
    public static readonly Color DarkPurple = Hex(0x7E2553);
    public static readonly Color DarkGreen = Hex(0x008751);
    public static readonly Color Brown = Hex(0xAB5236);
    public static readonly Color DarkGrey = Hex(0x5F574F);
    public static readonly Color LightGrey = Hex(0xC2C3C7);
    public static readonly Color White = Hex(0xFFF1E8);
    public static readonly Color Red = Hex(0xFF004D);
    public static readonly Color Orange = Hex(0xFFA300);
    public static readonly Color Yellow = Hex(0xFFEC27);
    public static readonly Color Green = Hex(0x00E436);
    public static readonly Color Blue = Hex(0x29ADFF);
    public static readonly Color Lavender = Hex(0x83769C);
    public static readonly Color Pink = Hex(0xFF77A8);
    public static readonly Color Peach = Hex(0xFFCCAA);

    /// <summary>The 16 colours in PICO-8 index order (0 = black … 15 = peach). Sprite strings use these indices.</summary>
    public static readonly Color[] ByIndex =
    {
        Black, DarkBlue, DarkPurple, DarkGreen, Brown, DarkGrey, LightGrey, White,
        Red, Orange, Yellow, Green, Blue, Lavender, Pink, Peach
    };

    private static Color Hex(uint rgb) => new((int)(rgb >> 16) & 0xFF, (int)(rgb >> 8) & 0xFF, (int)rgb & 0xFF);
}
