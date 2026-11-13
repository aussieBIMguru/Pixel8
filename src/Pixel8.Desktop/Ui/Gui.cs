using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Pixel8.Desktop.Rendering;

namespace Pixel8.Desktop.Ui;

/// <summary>A full-screen game state: map, combat, draft, menus.</summary>
public interface IScreen
{
    void Update(float dt, Input input);
    void Draw(SpriteBatch batch, PixelCanvas canvas, SpriteSheet sprites);
    Point ShakeOffset { get; }
}

/// <summary>Small immediate-mode drawing helpers shared by the screens.</summary>
public static class Gui
{
    public static void Panel(SpriteBatch b, PixelCanvas c, Rectangle r, Color fill, Color border)
    {
        c.Fill(b, r, fill);
        c.Outline(b, r, border);
    }

    /// <summary>A 1-line button. Hover brightens the border; disabled buttons are grey.</summary>
    public static void Button(SpriteBatch b, PixelCanvas c, Rectangle r, string label, bool hover, bool enabled = true, Color? accent = null)
    {
        var border = !enabled ? Palette.DarkGrey : hover ? Palette.Yellow : accent ?? Palette.LightGrey;
        var fill = hover && enabled ? Palette.DarkBlue : Palette.Black;
        Panel(b, c, r, fill, border);
        TextCenter(b, c, label, r.Center.X, r.Y + (r.Height - TinyFont.GlyphHeight) / 2, enabled ? Palette.White : Palette.DarkGrey);
    }

    public static void TextCenter(SpriteBatch b, PixelCanvas c, string text, int centerX, int y, Color color, bool shadow = false)
    {
        int x = centerX - TinyFont.Measure(text) / 2;
        if (shadow) TinyFont.DrawShadowed(b, c, text, x, y, color);
        else TinyFont.Draw(b, c, text, x, y, color);
    }

    public static void TextRight(SpriteBatch b, PixelCanvas c, string text, int rightX, int y, Color color, bool shadow = false)
    {
        int x = rightX - TinyFont.Measure(text);
        if (shadow) TinyFont.DrawShadowed(b, c, text, x, y, color);
        else TinyFont.Draw(b, c, text, x, y, color);
    }

    public static void HpBar(SpriteBatch b, PixelCanvas c, int x, int y, int width, int hp, int maxHp)
    {
        c.Fill(b, x, y, width, 3, Palette.DarkPurple);
        int w = maxHp <= 0 ? 0 : (int)Math.Round((width - 2) * Math.Clamp(hp / (double)maxHp, 0, 1));
        var col = hp * 3 <= maxHp ? Palette.Red : hp * 3 <= maxHp * 2 ? Palette.Orange : Palette.Green;
        c.Fill(b, x + 1, y + 1, w, 1, col);
    }

    public static string Clip(string text, int maxChars) =>
        text.Length <= maxChars ? text : text[..Math.Max(0, maxChars - 1)] + ".";

    public static IEnumerable<string> Wrap(string text, int width)
    {
        var line = "";
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string w = word.Length > width ? word[..width] : word;
            if (line.Length > 0 && line.Length + 1 + w.Length > width)
            {
                yield return line;
                line = w;
            }
            else
            {
                line = line.Length == 0 ? w : line + " " + w;
            }
        }
        if (line.Length > 0) yield return line;
    }
}
