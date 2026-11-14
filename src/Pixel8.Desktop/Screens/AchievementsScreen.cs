using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Pixel8.Core.Achievements;
using Pixel8.Desktop.Fx;
using Pixel8.Desktop.Rendering;
using Pixel8.Desktop.Ui;

namespace Pixel8.Desktop.Screens;

/// <summary>
/// The achievements list from the title screen (round 10). Every achievement is shown from the start: locked ones
/// dimmed, unlocked ones gold with their date, cumulative ones with a progress bar. ↑/↓ or the wheel scroll.
/// Round 11: taller rows (a 3px gap between name and description) with an 8×8 icon, dimmed while locked.
/// </summary>
public sealed class AchievementsScreen : IScreen
{
    private const int Top = 18, RowH = 18, Visible = 6;

    private readonly Action _onDone;
    private int _scroll;
    private int _lastWheel = int.MinValue;

    public AchievementsScreen(Action onDone)
    {
        _onDone = onDone;
        Sfx.Play("select");
    }

    public Point ShakeOffset => Point.Zero;

    private static int MaxScroll => Math.Max(0, AchievementList.All.Count - Visible);

    public void Update(float dt, Input input)
    {
        if (input.PressedAny(Keys.Escape, Keys.Enter, Keys.Space) || input.RightClick)
        {
            Sfx.Play("back");
            _onDone();
            return;
        }
        if (input.PressedAny(Keys.Up, Keys.W)) _scroll--;
        if (input.PressedAny(Keys.Down, Keys.S)) _scroll++;
        if (input.Pressed(Keys.PageUp)) _scroll -= Visible;
        if (input.Pressed(Keys.PageDown)) _scroll += Visible;
        int wheel = Mouse.GetState().ScrollWheelValue;
        if (_lastWheel != int.MinValue && wheel != _lastWheel) _scroll += wheel < _lastWheel ? 1 : -1;
        _lastWheel = wheel;
        _scroll = Math.Clamp(_scroll, 0, MaxScroll);
    }

    public void Draw(SpriteBatch b, PixelCanvas c, SpriteSheet s)
    {
        var state = AchievementStore.Tracker.State;
        int done = AchievementList.All.Count(a => state.Has(a.Id));
        Gui.TextCenter(b, c, "ACHIEVEMENTS", 128, 3, Palette.Yellow, shadow: true);
        Gui.TextCenter(b, c, $"{done} / {AchievementList.All.Count} UNLOCKED", 128, 10, Palette.LightGrey);

        for (int i = 0; i < Visible && _scroll + i < AchievementList.All.Count; i++)
        {
            var a = AchievementList.All[_scroll + i];
            bool got = state.Has(a.Id);
            var r = new Rectangle(8, Top + i * RowH, 240, RowH - 2);
            Gui.Panel(b, c, r, got ? Palette.DarkBlue : Palette.Black, got ? Palette.Yellow : Palette.DarkGrey);
            DrawIcon(b, s, a, r.X + 4, r.Y + 4, got);
            TinyFont.Draw(b, c, a.Name.ToUpperInvariant(), r.X + 16, r.Y + 2, got ? Palette.Yellow : Palette.LightGrey);
            TinyFont.Draw(b, c, Gui.Clip(a.Description.ToUpperInvariant(), 54), r.X + 16, r.Y + 10, got ? Palette.White : Palette.DarkGrey);
            if (got)
                Gui.TextRight(b, c, state.Unlocked[a.Id].ToLocalTime().ToString("dd MMM yyyy").ToUpperInvariant(), r.Right - 3, r.Y + 2, Palette.Peach);
            else if (a.Target > 1)
            {
                int progress = AchievementList.Progress(state, a);
                Gui.TextRight(b, c, $"{progress}/{a.Target}", r.Right - 3, r.Y + 2, Palette.LightGrey);
                int w = 50;
                c.Fill(b, r.Right - 3 - w, r.Y + 8, w, 2, Palette.DarkGrey);
                c.Fill(b, r.Right - 3 - w, r.Y + 8, w * progress / a.Target, 2, Palette.Green);
            }
        }
        // Scroll hints: little pixel arrows in the right margin.
        if (_scroll > 0) for (int k = 0; k < 3; k++) c.Fill(b, 251 - k, Top + k, 1 + 2 * k, 1, Palette.Yellow);
        if (_scroll < MaxScroll) for (int k = 0; k < 3; k++) c.Fill(b, 251 - k, Top + Visible * RowH - 4 - k, 1 + 2 * k, 1, Palette.Yellow);
        Gui.TextCenter(b, c, "UP/DOWN SCROLL  ESC BACK", 128, 137, Palette.DarkGrey);
    }

    /// <summary>An achievement's 8×8 icon (ach.&lt;icon&gt;), dimmed while locked. Also used by the toast.</summary>
    public static void DrawIcon(SpriteBatch b, SpriteSheet s, AchievementDef a, int x, int y, bool unlocked)
    {
        string name = s.Has("ach." + a.Icon) ? "ach." + a.Icon : "ach.star";
        if (s.Has(name)) s.Draw(b, name, x, y, unlocked ? Color.White : Palette.DarkGrey);
    }
}
