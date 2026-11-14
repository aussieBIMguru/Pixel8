using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Pixel8.Core.Cards;
using Pixel8.Core.Gear;
using Pixel8.Core.Heroes;
using Pixel8.Core.Runs;
using Pixel8.Desktop.Fx;
using Pixel8.Desktop.Rendering;
using Pixel8.Desktop.Ui;

namespace Pixel8.Desktop.Screens;

/// <summary>Shared text and colours for gear (round 10).</summary>
internal static class GearText
{
    public static Color RarityColor(Rarity r) => r switch
    {
        Rarity.Uncommon => Palette.Green,
        Rarity.Rare => Palette.Blue,
        Rarity.Legendary => Palette.Orange,
        _ => Palette.LightGrey
    };

    public static string SlotLetter(GearSlot s) => s switch
    {
        GearSlot.Weapon => "W",
        GearSlot.Armor => "A",
        GearSlot.OffHand => "O",
        _ => "T"
    };

    public static string SlotName(GearSlot s) => s == GearSlot.OffHand ? "OFF-HAND" : s.ToString().ToUpperInvariant();

    /// <summary>A short stat summary: "1D8 +1 FINESSE", "AC 14+DEX", "+2 AC".</summary>
    public static string Stats(GearInstance g)
    {
        var d = g.Def;
        var parts = new List<string>();
        if (d.Slot == GearSlot.Weapon)
        {
            parts.Add((d.Damage ?? "1d4").ToUpperInvariant());
            if (d.Finesse) parts.Add("FINESSE");
            if (d.Ranged) parts.Add("RANGED");
            if (d.TwoHanded) parts.Add("2-HANDED");
        }
        else if (d.Slot == GearSlot.Armor)
        {
            string dex = d.Armor switch { ArmorKind.Medium => "+DEX(2)", ArmorKind.Heavy => "", _ => "+DEX" };
            parts.Add($"AC {d.BaseAc}{dex}");
        }
        else if (d.IsShield) parts.Add("+2 AC");
        if (g.Magic > 0) parts.Add($"+{g.Magic}");
        if (d.Ac != 0) parts.Add($"{Signed(d.Ac)} AC");
        if (d.Attack != 0) parts.Add($"{Signed(d.Attack)} HIT");
        if (d.DamageBonus != 0) parts.Add($"{Signed(d.DamageBonus)} DMG");
        if (d.SpellDc != 0) parts.Add($"{Signed(d.SpellDc)} SPELL");
        if (d.Saves != 0) parts.Add($"{Signed(d.Saves)} SAVES");
        if (d.MaxHp != 0) parts.Add($"{Signed(d.MaxHp)} HP");
        if (d.Initiative != 0) parts.Add($"{Signed(d.Initiative)} INIT");
        if (d.FightLuck != 0) parts.Add($"{Signed(d.FightLuck)} LUCK");
        if (d.Rearm != 0) parts.Add($"{Signed(d.Rearm)} REARM");
        if (d.StartBlock != 0) parts.Add($"{d.StartBlock} BLOCK");
        if (d.Cursed) parts.Add("CURSED");
        return string.Join(" ", parts);
    }

    /// <summary>"F R" initials of the living heroes who can use it, or "NOBODY".</summary>
    public static string Users(Run run, GearDef def)
    {
        var who = run.Party.Heroes.Where(h => !h.IsDead && h.CanUse(def)).Select(h => h.Name[..1].ToUpperInvariant()).ToList();
        return who.Count == 0 ? "NOBODY HERE" : string.Join(" ", who);
    }

    public static string StatLine(GearStats st) => $"AC{st.Ac} HIT{Signed(st.AttackMod)} {st.Weapon.ToString().ToUpperInvariant()}";

    public static string Signed(int v) => v >= 0 ? $"+{v}" : v.ToString();

    /// <summary>A gear card (loot, shop): rarity-coloured border, name, slot, stats, text, who can use it.</summary>
    public static void DrawCard(SpriteBatch b, PixelCanvas c, SpriteSheet s, Run run, GearInstance g, Rectangle r, bool hover)
    {
        var col = RarityColor(g.EffectiveRarity);
        Gui.Panel(b, c, r, hover ? Palette.DarkBlue : Palette.Black, hover ? Palette.Yellow : col);
        int chars = Math.Max(4, (r.Width - 4) / TinyFont.Advance);
        // Round 12: the gear's icon heads the card.
        s.Draw(b, GearIcons.For(g.Def), r.Center.X - SpriteSheet.Size / 2, r.Y + 3);
        Gui.TextCenter(b, c, Gui.Clip(g.Name.ToUpperInvariant(), chars), r.Center.X, r.Y + 13, col);
        Gui.TextCenter(b, c, $"{g.EffectiveRarity.ToString().ToUpperInvariant()} {SlotName(g.Def.Slot)}", r.Center.X, r.Y + 20, Palette.DarkGrey);
        int y = r.Y + 28;
        foreach (string line in Gui.Wrap(Stats(g), chars).Take(2))
        {
            Gui.TextCenter(b, c, line, r.Center.X, y, Palette.White);
            y += 7;
        }
        foreach (string line in Gui.Wrap(g.Def.Text.ToUpperInvariant(), chars).Take(Math.Max(0, (r.Bottom - 10 - y) / 7)))
        {
            Gui.TextCenter(b, c, line, r.Center.X, y, Palette.LightGrey);
            y += 7;
        }
        bool usable = run.IsUsableByParty(g.Def);
        Gui.TextCenter(b, c, Gui.Clip("FOR: " + Users(run, g.Def), chars), r.Center.X, r.Bottom - 8, usable ? Palette.Peach : Palette.Red);
    }
}

/// <summary>
/// Loot (round 10): pick one of the offered pieces of gear (or the only one), or leave it. If the pack is full,
/// choose something in the pack to drop first. Once taken, a hero who can use it may equip it straight away.
/// </summary>
public sealed class LootScreen : IScreen
{
    private enum Stage { Choose, Drop, Equip }

    private readonly Run _run;
    private readonly LootOffer _offer;
    private readonly Action<string> _onDone;
    private Stage _stage = Stage.Choose;
    private GearInstance? _taken;
    private Point _pointer;
    private float _time;
    private string _note = "";

    /// <param name="onDone">Called with a message for the map log.</param>
    public LootScreen(Run run, LootOffer offer, Action<string> onDone)
    {
        _run = run;
        _offer = offer;
        _onDone = onDone;
        Sfx.Play(offer.Choices.Any(g => g.EffectiveRarity >= Rarity.Rare) ? "perfect" : "item");
    }

    public Point ShakeOffset => Point.Zero;

    private Rectangle CardRect(int i)
    {
        int n = _offer.Choices.Count, w = 78, gap = 6;
        int x0 = 128 - (n * w + (n - 1) * gap) / 2;
        return new Rectangle(x0 + i * (w + gap), 26, w, 72);
    }

    private static readonly Rectangle LeaveButton = new(96, 112, 64, 11);
    private static Rectangle PackRect(int i) => new(8 + i % 4 * 61, 30 + i / 4 * 14, 57, 11);
    private static Rectangle HeroButton(int i) => new(12 + i * 60, 108, 56, 11);
    private static readonly Rectangle KeepButton = new(192, 108, 56, 11);

    public void Update(float dt, Input input)
    {
        _time += dt;
        _pointer = input.Pointer;
        if (_time < 0.2f) return;
        int digit = input.PressedDigit();

        switch (_stage)
        {
            case Stage.Choose:
                for (int i = 0; i < _offer.Choices.Count; i++)
                    if (digit == i || input.Clicked(CardRect(i)))
                    {
                        Take(_offer.Choices[i]);
                        return;
                    }
                if (input.Clicked(LeaveButton) || input.Pressed(Keys.Escape))
                {
                    Sfx.Play("back");
                    _onDone("YOU LEAVE THE GEAR BEHIND.");
                }
                break;

            case Stage.Drop:
                for (int i = 0; i < _run.Party.Pack.Count; i++)
                    if (input.Clicked(PackRect(i)) || digit == i)
                    {
                        var dropped = _run.Party.Pack[i];
                        _run.Drop(dropped);
                        Sfx.Play("back");
                        var pick = _taken!;
                        _taken = null;
                        _note = $"DROPPED {dropped.Name.ToUpperInvariant()}.";
                        Take(pick);
                        return;
                    }
                if (input.Clicked(LeaveButton) || input.Pressed(Keys.Escape))
                {
                    _taken = null;
                    _stage = Stage.Choose;
                    Sfx.Play("back");
                }
                break;

            case Stage.Equip:
            {
                var heroes = _run.Party.Heroes;
                for (int i = 0; i < heroes.Count; i++)
                {
                    if (!(input.Clicked(HeroButton(i)) || digit == i)) continue;
                    if (_run.CanEquip(heroes[i], _taken!, out string reason))
                    {
                        _run.Equip(heroes[i], _taken!);
                        Sfx.Play("confirm");
                        _onDone($"{heroes[i].Name} equips {_taken!.Name}.".ToUpperInvariant());
                    }
                    else
                    {
                        _note = reason.ToUpperInvariant();
                        Sfx.Play("deny");
                    }
                    return;
                }
                if (input.Clicked(KeepButton) || input.PressedAny(Keys.Escape, Keys.Enter, Keys.Space))
                {
                    Sfx.Play("select");
                    _onDone($"{_taken!.Name} goes in the pack.".ToUpperInvariant());
                }
                break;
            }
        }
    }

    private void Take(GearInstance gear)
    {
        if (!_run.TakeLoot(gear))
        {
            _taken = gear;
            _stage = Stage.Drop;
            Sfx.Play("deny");
            return;
        }
        _taken = gear;
        Sfx.Play("item");
        bool anyone = _run.Party.Heroes.Any(h => !h.IsDead && h.CanUse(gear.Def));
        if (anyone) _stage = Stage.Equip;
        else _onDone($"{gear.Name} goes in the pack (nobody here can use it).".ToUpperInvariant());
    }

    public void Draw(SpriteBatch b, PixelCanvas c, SpriteSheet s)
    {
        string title = _offer.Source switch
        {
            LootSource.Mimic => "THE MIMIC'S HOARD",
            LootSource.Boss => "THE BOSS'S HOARD",
            LootSource.Treasure => "TREASURE!",
            _ => "LOOT"
        };
        Gui.TextCenter(b, c, title, 128, 5, Palette.Yellow, shadow: true);
        if (_offer.Gold > 0) Gui.TextCenter(b, c, $"+{_offer.Gold} GOLD", 128, 13, Palette.Yellow);

        switch (_stage)
        {
            case Stage.Choose:
                Gui.TextCenter(b, c, _offer.Choices.Count > 1 ? $"CHOOSE ONE (1-{_offer.Choices.Count})" : "TAKE IT? (1)", 128, 19, Palette.LightGrey);
                for (int i = 0; i < _offer.Choices.Count; i++)
                    GearText.DrawCard(b, c, s, _run, _offer.Choices[i], CardRect(i), CardRect(i).Contains(_pointer));
                Gui.TextCenter(b, c, $"PACK {_run.Party.Pack.Count}/{_run.Party.PackSize}", 128, 102, Palette.DarkGrey);
                Gui.Button(b, c, LeaveButton, "LEAVE IT", LeaveButton.Contains(_pointer));
                break;

            case Stage.Drop:
                Gui.TextCenter(b, c, "THE PACK IS FULL. DROP SOMETHING FOR", 128, 19, Palette.Red);
                Gui.TextCenter(b, c, Gui.Clip(_taken!.Name.ToUpperInvariant(), 40), 128, 64, Palette.Yellow);
                for (int i = 0; i < _run.Party.Pack.Count; i++)
                {
                    var g = _run.Party.Pack[i];
                    var r = PackRect(i);
                    Gui.IconButton(b, c, s, r, GearIcons.For(g.Def), Gui.Clip(g.Name.ToUpperInvariant(), 11), r.Contains(_pointer), true, GearText.RarityColor(g.EffectiveRarity));
                }
                Gui.Button(b, c, LeaveButton, "CANCEL", LeaveButton.Contains(_pointer));
                break;

            case Stage.Equip:
            {
                GearText.DrawCard(b, c, s, _run, _taken!, new Rectangle(89, 24, 78, 72), false);
                Gui.TextCenter(b, c, "EQUIP IT NOW?", 128, 100, Palette.LightGrey);
                var heroes = _run.Party.Heroes;
                for (int i = 0; i < heroes.Count; i++)
                {
                    var h = heroes[i];
                    bool ok = !h.IsDead && h.CanUse(_taken!.Def);
                    var r = HeroButton(i);
                    Gui.Button(b, c, r, $"{i + 1} {h.Name.ToUpperInvariant()}", r.Contains(_pointer) && ok, ok, Palette.Green);
                }
                Gui.Button(b, c, KeepButton, "KEEP IN PACK", KeepButton.Contains(_pointer));
                break;
            }
        }
        if (_note.Length > 0) Gui.TextCenter(b, c, Gui.Clip(_note, 62), 128, 126, Palette.Peach);
    }
}

/// <summary>
/// The gear screen (G on the map, round 10): three hero columns with their four slots and stats, the shared pack,
/// and details for whatever is under the cursor (with the stat change if the selected hero equipped it).
/// Click (or Enter on) a pack item to equip it to the selected hero; a filled slot to unequip it; D drops a pack item.
/// </summary>
public sealed class GearScreen : IScreen
{
    private const int ColW = 82, SlotY = 30, SlotH = 10, SlotGap = 11, PackY = 82;

    private readonly Run _run;
    private readonly Action _onDone;
    private int _hero;
    /// <summary>Cursor: rows 0–3 = slots (column = hero), rows 4–5 = pack (column 0–3).</summary>
    private int _row, _col;
    private Point _pointer;
    private string _note = "";
    private GearInstance? _confirmDrop;

    public GearScreen(Run run, Action onDone)
    {
        _run = run;
        _onDone = onDone;
        Sfx.Play("select");
    }

    public Point ShakeOffset => Point.Zero;

    private IReadOnlyList<Hero> Heroes => _run.Party.Heroes;
    private static Rectangle HeaderRect(int hero) => new(4 + hero * (ColW + 2), 8, ColW, 20);
    private static Rectangle SlotRect(int hero, int slot) => new(4 + hero * (ColW + 2), SlotY + slot * SlotGap, ColW, SlotH);
    private static Rectangle PackRect(int i) => new(4 + i % 4 * 62, PackY + i / 4 * 11, 60, SlotH);

    private (Hero? Hero, GearSlot? Slot, GearInstance? Gear) Selected()
    {
        if (_row < 4)
        {
            int h = Math.Clamp(_col, 0, Heroes.Count - 1);
            var slot = (GearSlot)_row;
            return (Heroes[h], slot, Heroes[h].Loadout[slot]);
        }
        int i = (_row - 4) * 4 + _col;
        return (null, null, i < _run.Party.Pack.Count ? _run.Party.Pack[i] : null);
    }

    public void Update(float dt, Input input)
    {
        _pointer = input.Pointer;

        if (input.PressedAny(Keys.Escape, Keys.G) || input.RightClick)
        {
            Sfx.Play("back");
            _onDone();
            return;
        }

        if (input.PressedAny(Keys.Up, Keys.W)) Move(-1, 0);
        if (input.PressedAny(Keys.Down, Keys.S)) Move(1, 0);
        if (input.PressedAny(Keys.Left, Keys.A)) Move(0, -1);
        if (input.PressedAny(Keys.Right, Keys.D)) Move(0, 1);
        int digit = input.PressedDigit();
        if (digit >= 0 && digit < Heroes.Count) { _hero = digit; Sfx.Play("select"); }

        // Mouse: hover moves the cursor; clicks act.
        for (int h = 0; h < Heroes.Count; h++)
        {
            if (input.Clicked(HeaderRect(h))) { _hero = h; Sfx.Play("select"); }
            for (int s = 0; s < 4; s++)
                if (input.Over(SlotRect(h, s)))
                {
                    _row = s;
                    _col = h;
                    if (input.LeftClick) { _hero = h; Act(); }
                }
        }
        for (int i = 0; i < _run.Party.PackSize; i++)
            if (input.Over(PackRect(i)))
            {
                _row = 4 + i / 4;
                _col = i % 4;
                if (input.LeftClick) Act();
            }

        if (input.PressedAny(Keys.Enter, Keys.Space)) Act();
        if (input.Pressed(Keys.X) || input.Pressed(Keys.Delete)) DropSelected();
    }

    private void Move(int dRow, int dCol)
    {
        _confirmDrop = null;
        _row = Math.Clamp(_row + dRow, 0, 5);
        int maxCol = _row < 4 ? Heroes.Count - 1 : 3;
        _col = Math.Clamp(_col + dCol, 0, maxCol);
        if (_row < 4 && dCol != 0) _hero = _col;
        Sfx.Play("tick");
    }

    private void Act()
    {
        var (hero, slot, gear) = Selected();
        _confirmDrop = null;
        if (hero is not null && slot is not null)
        {
            _hero = _col;
            if (gear is null) { _note = $"{hero.Name.ToUpperInvariant()}: EMPTY {GearText.SlotName(slot.Value)} SLOT. PICK SOMETHING FROM THE PACK."; return; }
            if (_run.CanUnequip(hero, slot.Value, out string why))
            {
                _run.Unequip(hero, slot.Value);
                _note = $"{gear.Name} goes back in the pack.".ToUpperInvariant();
                Sfx.Play("select");
            }
            else { _note = why.ToUpperInvariant(); Sfx.Play("deny"); }
            return;
        }
        if (gear is null) return;
        var target = Heroes[Math.Clamp(_hero, 0, Heroes.Count - 1)];
        if (_run.CanEquip(target, gear, out string reason))
        {
            _run.Equip(target, gear);
            _note = $"{target.Name} equips {gear.Name}.".ToUpperInvariant();
            Sfx.Play("confirm");
        }
        else { _note = reason.ToUpperInvariant(); Sfx.Play("deny"); }
    }

    private void DropSelected()
    {
        var (hero, _, gear) = Selected();
        if (hero is not null || gear is null) return;
        if (!ReferenceEquals(_confirmDrop, gear))
        {
            _confirmDrop = gear;
            _note = $"PRESS X AGAIN TO DROP {gear.Name.ToUpperInvariant()} FOR GOOD.";
            Sfx.Play("warn");
            return;
        }
        _run.Drop(gear);
        _confirmDrop = null;
        _note = $"DROPPED {gear.Name.ToUpperInvariant()}.";
        Sfx.Play("back");
    }

    public void Draw(SpriteBatch b, PixelCanvas c, SpriteSheet s)
    {
        Gui.TextCenter(b, c, "GEAR", 128, 1, Palette.Yellow, shadow: true);
        if (_run.AtCampfire) Gui.TextRight(b, c, "AT A CAMPFIRE", 252, 1, Palette.Orange);

        for (int h = 0; h < Heroes.Count; h++)
        {
            var hero = Heroes[h];
            var head = HeaderRect(h);
            bool sel = h == _hero;
            Gui.Panel(b, c, head, sel ? Palette.DarkBlue : Palette.Black, sel ? Palette.Yellow : Palette.DarkGrey);
            Gui.TextCenter(b, c, $"{h + 1} {hero.Name.ToUpperInvariant()}{(hero.IsDead ? " (DEAD)" : "")}", head.Center.X, head.Y + 2, sel ? Palette.Yellow : Palette.White);
            Gui.TextCenter(b, c, $"AC{hero.Ac} HIT{GearText.Signed(hero.AttackMod)} HP{hero.MaxHp}", head.Center.X, head.Y + 8, Palette.LightGrey);
            Gui.TextCenter(b, c, $"{hero.Weapon.ToString().ToUpperInvariant()} DC{hero.SpellDc}", head.Center.X, head.Y + 14, Palette.LightGrey);
            for (int sl = 0; sl < 4; sl++)
            {
                var slot = (GearSlot)sl;
                var g = hero.Loadout[slot];
                var r = SlotRect(h, sl);
                bool cursor = _row == sl && _col == h;
                Gui.Panel(b, c, r, cursor ? Palette.DarkBlue : Palette.Black, cursor ? Palette.Yellow : Palette.DarkGrey);
                // Round 12: the gear's icon stands in for the slot letter (the letter still marks an empty slot).
                if (g is null) TinyFont.Draw(b, c, $"{GearText.SlotLetter(slot)} -", r.X + 2, r.Y + 2, Palette.DarkGrey);
                else
                {
                    s.Draw(b, GearIcons.For(g.Def), r.X + 1, r.Y + 1);
                    TinyFont.Draw(b, c, Gui.Clip(g.Name.ToUpperInvariant(), 17), r.X + 11, r.Y + 2,
                        g.Def.Cursed ? Palette.Pink : GearText.RarityColor(g.EffectiveRarity));
                }
            }
        }

        TinyFont.Draw(b, c, $"PACK {_run.Party.Pack.Count}/{_run.Party.PackSize}  (EQUIP TO {Heroes[Math.Clamp(_hero, 0, Heroes.Count - 1)].Name.ToUpperInvariant()})", 4, PackY - 8, Palette.LightGrey);
        for (int i = 0; i < _run.Party.PackSize; i++)
        {
            var r = PackRect(i);
            bool cursor = _row == 4 + i / 4 && _col == i % 4;
            var g = i < _run.Party.Pack.Count ? _run.Party.Pack[i] : null;
            Gui.Panel(b, c, r, cursor ? Palette.DarkBlue : Palette.Black, cursor ? Palette.Yellow : Palette.DarkGrey);
            if (g is not null)
            {
                s.Draw(b, GearIcons.For(g.Def), r.X + 1, r.Y + 1);
                TinyFont.Draw(b, c, Gui.Clip(g.Name.ToUpperInvariant(), 12), r.X + 11, r.Y + 2, GearText.RarityColor(g.EffectiveRarity));
            }
        }

        DrawDetails(b, c);
        TinyFont.Draw(b, c, "ARROWS/CLICK  ENTER EQUIP/REMOVE  1-3 HERO  X DROP  ESC BACK", 4, 137, Palette.DarkGrey);
    }

    private void DrawDetails(SpriteBatch b, PixelCanvas c)
    {
        var (hero, _, gear) = Selected();
        int y = 106;
        if (gear is null)
        {
            if (_note.Length > 0) Gui.TextCenter(b, c, Gui.Clip(_note, 62), 128, y + 8, Palette.Peach);
            return;
        }
        var col = GearText.RarityColor(gear.EffectiveRarity);
        TinyFont.Draw(b, c, Gui.Clip($"{gear.Name.ToUpperInvariant()} - {gear.EffectiveRarity.ToString().ToUpperInvariant()} {GearText.SlotName(gear.Def.Slot)}", 62), 4, y, col);
        TinyFont.Draw(b, c, Gui.Clip(GearText.Stats(gear) + "  " + gear.Def.Text.ToUpperInvariant(), 62), 4, y + 7, Palette.White);
        if (hero is null)
        {
            var target = Heroes[Math.Clamp(_hero, 0, Heroes.Count - 1)];
            string line;
            if (!target.CanUse(gear.Def)) line = $"{target.Name.ToUpperInvariant()} CAN'T USE IT. FOR: {GearText.Users(_run, gear.Def)}";
            else if (_run.CurrentStats(target) is { } now && _run.PreviewEquip(target, gear) is { } then)
                line = $"{target.Name.ToUpperInvariant()}: {GearText.StatLine(now)} -> {GearText.StatLine(then)}";
            else line = $"FOR: {GearText.Users(_run, gear.Def)}";
            TinyFont.Draw(b, c, Gui.Clip(line, 62), 4, y + 14, target.CanUse(gear.Def) ? Palette.Green : Palette.Red);
        }
        if (_note.Length > 0) TinyFont.Draw(b, c, Gui.Clip(_note, 62), 4, y + 22, Palette.Peach);
    }
}
