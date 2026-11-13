using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Pixel8.Core.Combat;
using Pixel8.Core.Dice;
using Pixel8.Desktop.Fx;
using Pixel8.Desktop.Rendering;
using Pixel8.Desktop.Ui;

namespace Pixel8.Desktop.Screens;

/// <summary>
/// The dice roll strip under the action (locked 2026-11-13): every d20 check tumbles and lands, each labelled
/// modifier slides in ("+5 ATK", "+2 BLESS"), then "= total VS AC n" and the verdict. Damage dice roll the same
/// way ("2D8+4: [3][7] +4 = 14", then "EASY X0.85 = 12"). Enemy rolls use it too, so the player can always follow
/// the maths. The combat screen waits <see cref="Show(CheckRolled)"/>'s returned duration before the next event.
/// Visual randomness (tumbling faces) uses System.Random: the rules RNG is never touched.
/// </summary>
internal sealed class RollStrip
{
    public const int Height = 31;

    // Check timeline (seconds at normal speed)
    private const float Tumble = 0.35f, PartStep = 0.12f, VerdictGap = 0.1f, Hold = 0.35f;
    // Damage timeline
    private const float DmgTumble = 0.25f, DmgTotal = 0.4f, DmgNotes = 0.5f, DmgHold = 0.3f;

    private readonly Random _fx = new();
    private CheckRolled? _check;
    private DamageRolled? _damage;
    private float _t;
    private float _tickTimer;
    private int _face1 = 1, _face2 = 1;
    private int[] _dmgFaces = Array.Empty<int>();
    private int _stage;

    public RollStrip(Rectangle area) => Area = area;

    public Rectangle Area { get; }

    public bool HasContent => _check is not null || _damage is not null;

    /// <summary>The check on show (null while damage is shown).</summary>
    public CheckRolled? Check => _check;

    private IReadOnlyList<RollPart> NumericParts =>
        _check is null ? Array.Empty<RollPart>() : _check.Breakdown.Where(p => !p.Tag).ToArray();

    private float VerdictAt => Tumble + NumericParts.Count * PartStep + VerdictGap;

    /// <summary>Starts a check animation; returns how long the screen should wait before the next event.</summary>
    public float Show(CheckRolled e)
    {
        _check = e;
        _damage = null;
        _t = 0;
        _stage = 0;
        _tickTimer = 0;
        return VerdictAt + Hold;
    }

    public float Show(DamageRolled e)
    {
        _damage = e;
        _check = null;
        _t = 0;
        _stage = 0;
        _tickTimer = 0;
        _dmgFaces = e.Faces.Select(_ => 1).ToArray();
        return DmgNotes + DmgHold;
    }

    public void Update(float dt)
    {
        if (!HasContent) return;
        _t += dt;
        float tumble = _check is not null ? Tumble : DmgTumble;

        if (_t < tumble)
        {
            _tickTimer -= dt;
            if (_tickTimer <= 0)
            {
                _tickTimer = 0.05f;
                _face1 = _fx.Next(1, 21);
                _face2 = _fx.Next(1, 21);
                if (_damage is not null)
                    for (int i = 0; i < _dmgFaces.Length; i++) _dmgFaces[i] = _fx.Next(1, _damage.Dice.Sides + 1);
                Sfx.Play("tick", 0.6f, (float)(_fx.NextDouble() * 0.4 - 0.2));
            }
            return;
        }

        if (_check is not null)
        {
            int parts = NumericParts.Count;
            if (_stage == 0) { _stage = 1; Sfx.Play("land"); }
            int shown = Math.Min(parts, (int)((_t - Tumble) / PartStep) + 1);
            while (_stage - 1 < shown && _stage - 1 < parts) { _stage++; Sfx.Play("part", 0.5f, 0.1f * _stage); }
            if (_t >= VerdictAt && _stage < 100)
            {
                _stage = 100;
                var r = _check.Result;
                Sfx.Play(r.Critical ? "crit" : r.Success == (_check.Versus == "AC") ? "confirm" : "miss");
            }
        }
        else if (_damage is not null && _stage == 0)
        {
            _stage = 1;
            Sfx.Play("land");
        }
    }

    /// <param name="headline">Replaces the first row (the Luck prompt uses it).</param>
    /// <param name="hideLastRow">Leaves the last row free for buttons.</param>
    public void Draw(SpriteBatch b, PixelCanvas c, string? headline = null, bool hideLastRow = false)
    {
        if (!HasContent) return;
        var a = Area;
        c.Fill(b, a, Palette.Black);
        c.Fill(b, a.X, a.Y, a.Width, 1, Palette.DarkBlue);
        if (_check is not null) DrawCheck(b, c, _check, headline, hideLastRow);
        else if (_damage is not null) DrawDamage(b, c, _damage, headline, hideLastRow);
    }

    // ------------------------------------------------------------------ checks

    private void DrawCheck(SpriteBatch b, PixelCanvas c, CheckRolled e, string? headline, bool hideLastRow)
    {
        var a = Area;
        int chars = (a.Width - 6) / TinyFont.Advance;
        bool save = e.Versus == "DC";
        bool landed = _t >= Tumble;

        // Row 1: who rolls what, with tags (ADV, CRIT 19-20) on the right
        string tags = string.Join(" ", e.Breakdown.Where(p => p.Tag).Select(p => p.Label.ToUpperInvariant()));
        string who = e.Roller is Enemy && !save ? $"{e.Label} > {e.Against.Name}"
            : save ? e.Label
            : $"{e.Roller.Name} {e.Label} > {e.Against.Name}";
        if (e.IsReroll) who = "REROLL! " + who;
        if (headline is not null)
        {
            TinyFont.Draw(b, c, Gui.Clip(headline, chars), a.X + 3, a.Y + 3, Palette.Yellow);
        }
        else
        {
            int room = tags.Length > 0 ? chars - tags.Length - 1 : chars;
            var whoColor = save ? Palette.Lavender : e.Roller is Enemy ? Palette.Pink : Palette.Peach;
            TinyFont.Draw(b, c, Gui.Clip(who.ToUpperInvariant(), Math.Max(4, room)), a.X + 3, a.Y + 3, whoColor);
            if (tags.Length > 0) Gui.TextRight(b, c, tags, a.Right - 3, a.Y + 3, Palette.Pink);
        }

        // Row 2: the d20 (two for advantage/disadvantage), then the modifiers sliding in
        int x = a.X + 3, y = a.Y + 11;
        var r = e.Result;
        bool twoDice = r.Die1 != r.Die2 || IsAdvantage(e);
        int first = landed ? r.Die1 : _face1;
        int second = landed ? r.Die2 : _face2;
        if (twoDice)
        {
            bool firstKept = r.Kept == r.Die1;
            x = DrawDie(b, c, x, y, first, !landed || firstKept, landed && firstKept && r.Critical, landed && firstKept && r.Fumble);
            x = DrawDie(b, c, x, y, second, !landed || !firstKept, landed && !firstKept && r.Critical, landed && !firstKept && r.Fumble);
        }
        else
        {
            x = DrawDie(b, c, x, y, first, true, landed && r.Critical, landed && r.Fumble);
        }

        if (landed)
        {
            var parts = NumericParts;
            int shown = Math.Min(parts.Count, (int)((_t - Tumble) / PartStep) + 1);
            for (int i = 0; i < shown; i++)
            {
                string text = parts[i].Text.ToUpperInvariant();
                // slide in from the right over a part step
                float k = Math.Clamp((_t - Tumble - i * PartStep) / PartStep, 0, 1);
                int slide = (int)((1 - k) * 8);
                if (x + TinyFont.Measure(text) > a.Right - 3) text = parts[i].Value >= 0 ? $"+{parts[i].Value}" : parts[i].Value.ToString();
                if (x + TinyFont.Measure(text) > a.Right - 3) break;
                var color = parts[i].Label == "ATK" || parts[i].Label == "DEX" ? Palette.White : Palette.Green;
                TinyFont.Draw(b, c, text, x + slide, y + 2, color);
                x += TinyFont.Measure(text) + 4;
            }
        }

        // Row 3: total vs target, then the verdict
        if (hideLastRow || _t < VerdictAt) return;
        int ry = a.Y + 22;
        TinyFont.Draw(b, c, $"= {r.Total} VS {e.Versus} {r.Target}", a.X + 3, ry, Palette.White);
        var (verdict, vColor) = Verdict(e);
        bool blink = (int)(_t * 8) % 2 == 0 || _t > VerdictAt + 0.3f;
        if (blink) Gui.TextRight(b, c, verdict, a.Right - 3, ry, vColor);
    }

    private static bool IsAdvantage(CheckRolled e) => e.Breakdown.Any(p => p.Tag && p.Label.StartsWith("ADV", StringComparison.OrdinalIgnoreCase));

    public static (string Text, Color Color) Verdict(CheckRolled e)
    {
        var r = e.Result;
        if (e.Versus == "DC")
            return r.Success ? ("SAVED", Palette.Blue) : ("FAILED!", Palette.Green);
        bool heroRolled = e.Roller is not Enemy;
        if (r.Critical) return ("CRIT!", heroRolled ? Palette.Yellow : Palette.Red);
        if (r.Fumble) return ("FUMBLE", Palette.DarkGrey);
        if (r.Success) return ("HIT", heroRolled ? Palette.Green : Palette.Red);
        return ("MISS", heroRolled ? Palette.Red : Palette.Green);
    }

    /// <summary>An 11×9 die face. Returns the x after it.</summary>
    private static int DrawDie(SpriteBatch b, PixelCanvas c, int x, int y, int value, bool kept, bool crit, bool fumble)
    {
        var rect = new Rectangle(x, y, 11, 9);
        var border = !kept ? Palette.DarkGrey : crit ? Palette.Yellow : fumble ? Palette.Red : Palette.White;
        c.Fill(b, rect, kept ? Palette.DarkBlue : Palette.Black);
        c.Outline(b, rect, border);
        string text = value.ToString();
        Gui.TextCenter(b, c, text, rect.Center.X, y + 2, kept ? (crit ? Palette.Yellow : Palette.White) : Palette.DarkGrey);
        if (!kept) c.Fill(b, x + 1, y + 4, 9, 1, Palette.DarkGrey);   // struck through
        return x + 13;
    }

    // ------------------------------------------------------------------ damage

    private void DrawDamage(SpriteBatch b, PixelCanvas c, DamageRolled e, string? headline, bool hideLastRow)
    {
        var a = Area;
        int chars = (a.Width - 6) / TinyFont.Advance;
        bool landed = _t >= DmgTumble;

        string head = headline ?? $"DAMAGE {e.Dice.ToString().ToUpperInvariant()}{(e.Critical ? " CRIT" : "")} > {e.Target.Name.ToUpperInvariant()}";
        TinyFont.Draw(b, c, Gui.Clip(head, chars), a.X + 3, a.Y + 3, e.Critical ? Palette.Yellow : Palette.Orange);

        int x = a.X + 3, y = a.Y + 11;
        int maxBoxes = (a.Width - 6 - 16) / 10;
        if (e.Faces.Count <= maxBoxes)
        {
            for (int i = 0; i < e.Faces.Count; i++)
            {
                int v = landed ? e.Faces[i] : _dmgFaces[i];
                var rect = new Rectangle(x, y, 9, 9);
                c.Fill(b, rect, Palette.DarkPurple);
                c.Outline(b, rect, landed ? Palette.Orange : Palette.LightGrey);
                Gui.TextCenter(b, c, v.ToString(), rect.Center.X, y + 2, Palette.White);
                x += 10;
            }
        }
        else
        {
            string many = landed ? $"[{e.Faces.Count}D: {e.Faces.Sum()}]" : $"[{e.Faces.Count}D: ..]";
            TinyFont.Draw(b, c, many, x, y + 2, Palette.White);
            x += TinyFont.Measure(many) + 4;
        }
        if (landed && e.Dice.Bonus != 0)
            TinyFont.Draw(b, c, e.Dice.Bonus > 0 ? $"+{e.Dice.Bonus}" : e.Dice.Bonus.ToString(), x + 1, y + 2, Palette.White);
        if (e.Faces.Count == 0 && landed)
            TinyFont.Draw(b, c, "FLAT", x + 16, y + 2, Palette.DarkGrey);

        if (hideLastRow || _t < DmgTotal) return;
        int ry = a.Y + 22;
        string total = $"= {e.Raw}";
        TinyFont.Draw(b, c, total, a.X + 3, ry, Palette.Yellow);
        if (e.Notes.Count > 0 && _t >= DmgNotes)
        {
            string notes = string.Join(" ", e.Notes).ToUpperInvariant() + $" = {e.Total}";
            int room = chars - total.Length - 1;
            Gui.TextRight(b, c, Gui.Clip(notes, room), a.Right - 3, ry, e.Total == 0 ? Palette.Green : Palette.Peach);
        }
    }
}
