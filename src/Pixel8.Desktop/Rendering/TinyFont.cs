using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Pixel8.Desktop.Rendering;

/// <summary>
/// Built-in 3×5 pixel font so the prototype needs no content pipeline.
/// Each glyph is 5 rows of 3 bits (MSB = left pixel). Uppercase only.
/// Replace with a proper BMFont (m5x7 / m6x11 / Press Start 2P) in M3.
/// </summary>
public static class TinyFont
{
    public const int GlyphWidth = 3;
    public const int GlyphHeight = 5;
    public const int Advance = 4;

    private static readonly Dictionary<char, byte[]> Glyphs = new()
    {
        ['0'] = new byte[] { 7, 5, 5, 5, 7 }, ['1'] = new byte[] { 2, 6, 2, 2, 7 },
        ['2'] = new byte[] { 7, 1, 7, 4, 7 }, ['3'] = new byte[] { 7, 1, 3, 1, 7 },
        ['4'] = new byte[] { 5, 5, 7, 1, 1 }, ['5'] = new byte[] { 7, 4, 7, 1, 7 },
        ['6'] = new byte[] { 7, 4, 7, 5, 7 }, ['7'] = new byte[] { 7, 1, 1, 2, 2 },
        ['8'] = new byte[] { 7, 5, 7, 5, 7 }, ['9'] = new byte[] { 7, 5, 7, 1, 7 },
        ['A'] = new byte[] { 2, 5, 7, 5, 5 }, ['B'] = new byte[] { 6, 5, 6, 5, 6 },
        ['C'] = new byte[] { 3, 4, 4, 4, 3 }, ['D'] = new byte[] { 6, 5, 5, 5, 6 },
        ['E'] = new byte[] { 7, 4, 6, 4, 7 }, ['F'] = new byte[] { 7, 4, 6, 4, 4 },
        ['G'] = new byte[] { 3, 4, 5, 5, 3 }, ['H'] = new byte[] { 5, 5, 7, 5, 5 },
        ['I'] = new byte[] { 7, 2, 2, 2, 7 }, ['J'] = new byte[] { 1, 1, 1, 5, 2 },
        ['K'] = new byte[] { 5, 5, 6, 5, 5 }, ['L'] = new byte[] { 4, 4, 4, 4, 7 },
        ['M'] = new byte[] { 5, 7, 7, 5, 5 }, ['N'] = new byte[] { 6, 5, 5, 5, 5 },
        ['O'] = new byte[] { 2, 5, 5, 5, 2 }, ['P'] = new byte[] { 6, 5, 6, 4, 4 },
        ['Q'] = new byte[] { 2, 5, 5, 6, 3 }, ['R'] = new byte[] { 6, 5, 6, 5, 5 },
        ['S'] = new byte[] { 3, 4, 2, 1, 6 }, ['T'] = new byte[] { 7, 2, 2, 2, 2 },
        ['U'] = new byte[] { 5, 5, 5, 5, 7 }, ['V'] = new byte[] { 5, 5, 5, 5, 2 },
        ['W'] = new byte[] { 5, 5, 7, 7, 5 }, ['X'] = new byte[] { 5, 5, 2, 5, 5 },
        ['Y'] = new byte[] { 5, 5, 2, 2, 2 }, ['Z'] = new byte[] { 7, 1, 2, 4, 7 },
        [' '] = new byte[] { 0, 0, 0, 0, 0 }, ['-'] = new byte[] { 0, 0, 7, 0, 0 },
        ['.'] = new byte[] { 0, 0, 0, 0, 2 }, [':'] = new byte[] { 0, 2, 0, 2, 0 },
        ['/'] = new byte[] { 1, 1, 2, 4, 4 }, ['%'] = new byte[] { 5, 1, 2, 4, 5 },
        ['+'] = new byte[] { 0, 2, 7, 2, 0 }, ['?'] = new byte[] { 6, 1, 2, 0, 2 },
        ['!'] = new byte[] { 2, 2, 2, 0, 2 }, ['('] = new byte[] { 1, 2, 2, 2, 1 },
        [')'] = new byte[] { 4, 2, 2, 2, 4 },
        [','] = new byte[] { 0, 0, 0, 2, 4 }, ['\''] = new byte[] { 2, 2, 0, 0, 0 },
        ['>'] = new byte[] { 4, 2, 1, 2, 4 }, ['<'] = new byte[] { 1, 2, 4, 2, 1 },
        ['='] = new byte[] { 0, 7, 0, 7, 0 }, ['*'] = new byte[] { 0, 5, 2, 5, 0 },
        ['['] = new byte[] { 6, 4, 4, 4, 6 }, [']'] = new byte[] { 3, 1, 1, 1, 3 },
        ['#'] = new byte[] { 5, 7, 5, 7, 5 }, ['"'] = new byte[] { 5, 5, 0, 0, 0 },
        [';'] = new byte[] { 0, 2, 0, 2, 4 }, ['_'] = new byte[] { 0, 0, 0, 0, 7 },
        ['|'] = new byte[] { 2, 2, 2, 2, 2 },
    };

    public static int Measure(string text) => text.Length * Advance - 1;

    public static void Draw(SpriteBatch batch, PixelCanvas canvas, string text, int x, int y, Color color)
    {
        int cx = x;
        foreach (char raw in text.ToUpperInvariant())
        {
            if (Glyphs.TryGetValue(raw, out var rows))
            {
                for (int row = 0; row < GlyphHeight; row++)
                    for (int col = 0; col < GlyphWidth; col++)
                        if ((rows[row] & (4 >> col)) != 0)
                            canvas.Fill(batch, cx + col, y + row, 1, 1, color);
            }
            cx += Advance;
        }
    }

    /// <summary>Each font pixel drawn as a scale×scale block (title logo). Advance scales too.</summary>
    public static void DrawScaled(SpriteBatch batch, PixelCanvas canvas, string text, int x, int y, int scale, Color color)
    {
        int cx = x;
        foreach (char raw in text.ToUpperInvariant())
        {
            if (Glyphs.TryGetValue(raw, out var rows))
            {
                for (int row = 0; row < GlyphHeight; row++)
                    for (int col = 0; col < GlyphWidth; col++)
                        if ((rows[row] & (4 >> col)) != 0)
                            canvas.Fill(batch, cx + col * scale, y + row * scale, scale, scale, color);
            }
            cx += Advance * scale;
        }
    }

    public static int MeasureScaled(string text, int scale) => text.Length * Advance * scale - scale;

    /// <summary>Text with a 1px drop shadow — reads well on busy backgrounds.</summary>
    public static void DrawShadowed(SpriteBatch batch, PixelCanvas canvas, string text, int x, int y, Color color)
    {
        Draw(batch, canvas, text, x + 1, y + 1, Palette.Black);
        Draw(batch, canvas, text, x, y, color);
    }
}
