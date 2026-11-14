using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Pixel8.Desktop.Fx;
using Pixel8.Desktop.Rendering;
using Pixel8.Desktop.Ui;

namespace Pixel8.Desktop.Screens;

/// <summary>One line of the debug panel. <see cref="Change"/> (←/→) makes it a carousel, e.g. which enemy to fight.</summary>
public sealed class DebugCommand
{
    public DebugCommand(Func<string> label, Action run, Func<bool>? enabled = null, Action<int>? change = null)
    {
        Label = label;
        Run = run;
        Enabled = enabled ?? (() => true);
        Change = change;
    }

    public DebugCommand(string label, Action run, Func<bool>? enabled = null) : this(() => label, run, enabled) { }

    public Func<string> Label { get; }
    public Action Run { get; }
    public Func<bool> Enabled { get; }
    public Action<int>? Change { get; }
}

/// <summary>
/// Developer debug panel (round 8), drawn over whatever screen was showing. Opened with
/// <see cref="DebugMode.Key"/> while debug mode is on. ↑/↓ or mouse to pick, ←/→ to change a carousel,
/// Enter/click (or 1–9) to run, Esc or the debug key to close. Commands that start something new
/// (a fight, a level-up) close the panel themselves.
/// </summary>
public sealed class DebugScreen : IScreen
{
    private const int Width = 168;
    /// <summary>Rows tighten to 6px when the list gets long (round 10 added gear and achievement commands).</summary>
    private int RowH => _commands.Count > 16 ? 6 : 7;

    private readonly IScreen _under;
    private readonly IReadOnlyList<DebugCommand> _commands;
    private readonly Action _close;
    private readonly string _context;
    private int _cursor;
    private string _note = "";
    private float _time;

    public DebugScreen(IScreen under, string context, IReadOnlyList<DebugCommand> commands, Action close)
    {
        _under = under;
        _context = context;
        _commands = commands;
        _close = close;
        Sfx.Play("select");
    }

    public Point ShakeOffset => Point.Zero;

    private Rectangle Box => new((PixelCanvas.Width - Width) / 2, Math.Max(1, (PixelCanvas.Height - (_commands.Count * RowH + 24)) / 2),
        Width, _commands.Count * RowH + 24);

    private Rectangle Row(int i) => new(Box.X + 4, Box.Y + 12 + i * RowH, Box.Width - 8, RowH - 1);

    public void Update(float dt, Input input)
    {
        _time += dt;
        if (input.PressedAny(Keys.Escape, DebugMode.Key) || input.RightClick)
        {
            Sfx.Play("select");
            _close();
            return;
        }

        if (input.PressedAny(Keys.Up, Keys.W)) _cursor = Math.Max(0, _cursor - 1);
        if (input.PressedAny(Keys.Down, Keys.S)) _cursor = Math.Min(_commands.Count - 1, _cursor + 1);
        for (int i = 0; i < _commands.Count; i++)
            if (input.Over(Row(i))) _cursor = i;

        var cmd = _commands[_cursor];
        if (cmd.Change is not null)
        {
            if (input.PressedAny(Keys.Left, Keys.A)) { cmd.Change(-1); Sfx.Play("tick"); }
            if (input.PressedAny(Keys.Right, Keys.D)) { cmd.Change(+1); Sfx.Play("tick"); }
        }

        int digit = input.PressedDigit();
        if (digit >= 0 && digit < _commands.Count) Invoke(digit);
        else if (input.PressedAny(Keys.Enter, Keys.Space)) Invoke(_cursor);
        else if (input.LeftClick)
            for (int i = 0; i < _commands.Count; i++)
                if (input.Over(Row(i))) Invoke(i);
    }

    private void Invoke(int i)
    {
        _cursor = i;
        var cmd = _commands[i];
        if (!cmd.Enabled())
        {
            _note = "NOT AVAILABLE HERE";
            Sfx.Play("deny");
            return;
        }
        string label = cmd.Label();
        cmd.Run();
        _note = "DONE: " + label;
        Sfx.Play("confirm");
    }

    public void Draw(SpriteBatch b, PixelCanvas c, SpriteSheet s)
    {
        _under.Draw(b, c, s);
        c.Fill(b, new Rectangle(0, 0, PixelCanvas.Width, PixelCanvas.Height), Palette.Black * 0.55f);

        var box = Box;
        Gui.Panel(b, c, box, Palette.Black, (int)(_time * 3) % 2 == 0 ? Palette.Pink : Palette.DarkPurple);
        Gui.TextCenter(b, c, $"DEBUG - {_context}", box.Center.X, box.Y + 3, Palette.Pink, shadow: true);

        for (int i = 0; i < _commands.Count; i++)
        {
            var cmd = _commands[i];
            var r = Row(i);
            bool on = cmd.Enabled();
            if (i == _cursor) c.Fill(b, r, Palette.DarkBlue);
            string key = i < 9 ? $"{i + 1} " : "  ";
            string text = key + cmd.Label().ToUpperInvariant();
            if (cmd.Change is not null) text += "  </>";
            TinyFont.Draw(b, c, Gui.Clip(text, 40), r.X + 2, r.Y + 1, !on ? Palette.DarkGrey : i == _cursor ? Palette.Yellow : Palette.White);
        }

        string footer = _note.Length > 0 ? Gui.Clip(_note.ToUpperInvariant(), 40) : $"ENTER RUN  ESC/{DebugMode.KeyName} CLOSE";
        Gui.TextCenter(b, c, footer, box.Center.X, box.Bottom - 8, _note.Length > 0 ? Palette.Green : Palette.DarkGrey);
    }
}
