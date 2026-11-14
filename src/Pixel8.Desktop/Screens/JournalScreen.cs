using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Pixel8.Core.Cards;
using Pixel8.Core.Content;
using Pixel8.Core.Gear;
using Pixel8.Desktop.Fx;
using Pixel8.Desktop.Rendering;
using Pixel8.Desktop.Ui;

namespace Pixel8.Desktop.Screens;

/// <summary>
/// The Adventurer's Journal (round 11): every card, piece of gear and foe, with full details for the ones seen in any
/// run so far and "???" for the rest. Opened from the title menu or with J on the map. Tabs: ←/→ (or 1–3, Tab, a click);
/// ↑/↓, PgUp/PgDn, the wheel or a click browse; Esc goes back.
/// </summary>
public sealed class JournalScreen : IScreen
{
    private enum Tab { Cards, Gear, Foes }

    private const int ListX = 4, ListY = 23, ListW = 98, RowH = 7, Rows = 15;
    private static readonly Rectangle DetailBox = new(105, 21, 147, 110);

    private static readonly string[] ClassOrder = { "basic", "fighter", "rogue", "wizard", "cleric", "bard", "ranger", "druid", "paladin", "neutral" };

    private readonly ContentDb _content;
    private readonly Action _onDone;
    private readonly List<CardDef> _cards;
    private readonly List<GearDef> _gear;
    private readonly List<EnemyDef> _foes;
    private Tab _tab;
    private readonly int[] _cursor = new int[3];
    private readonly int[] _scroll = new int[3];
    private int _lastWheel = int.MinValue;
    private Point _pointer;

    public JournalScreen(ContentDb content, Action onDone)
    {
        _content = content;
        _onDone = onDone;
        _cards = content.Cards.Values
            .Where(c => !c.ClassId.Equals("enemy", StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => ClassRank(c.ClassId)).ThenBy(c => c.Rarity).ThenBy(c => c.Name, StringComparer.Ordinal)
            .ToList();
        _gear = content.Gear.Values
            .OrderBy(g => g.Slot).ThenBy(g => g.Rarity).ThenBy(g => g.Name, StringComparer.Ordinal)
            .ToList();
        _foes = content.Enemies.Values
            .OrderBy(e => e.Tier).ThenBy(e => e.MinDepth).ThenBy(e => e.Name, StringComparer.Ordinal)
            .ToList();
        Sfx.Play("select");
    }

    public Point ShakeOffset => Point.Zero;

    private static int ClassRank(string id)
    {
        int i = Array.FindIndex(ClassOrder, c => c.Equals(id, StringComparison.OrdinalIgnoreCase));
        return i < 0 ? ClassOrder.Length : i;
    }

    private static Pixel8.Core.Lore.JournalState Seen => JournalStore.Tracker.State;

    private int Count => _tab switch { Tab.Cards => _cards.Count, Tab.Gear => _gear.Count, _ => _foes.Count };

    private bool IsSeen(int i) => _tab switch
    {
        Tab.Cards => Seen.Cards.Contains(_cards[i].Id),
        Tab.Gear => Seen.Gear.Contains(_gear[i].Id),
        _ => Seen.Enemies.Contains(_foes[i].Id)
    };

    private Rectangle TabRect(Tab t) => new(4 + (int)t * 84, 11, 80, 9);
    private static Rectangle RowRect(int i) => new(ListX, ListY + i * RowH, ListW, RowH);

    // ================================================================== update

    public void Update(float dt, Input input)
    {
        _pointer = input.Pointer;
        if (input.PressedAny(Keys.Escape, Keys.Back, Keys.J) || input.RightClick)
        {
            Sfx.Play("back");
            _onDone();
            return;
        }

        foreach (var t in Enum.GetValues<Tab>())
            if (input.Clicked(TabRect(t))) SetTab(t);
        int digit = input.PressedDigit();
        if (digit is >= 0 and <= 2) SetTab((Tab)digit);
        if (input.PressedAny(Keys.Left, Keys.A)) SetTab((Tab)(((int)_tab + 2) % 3));
        if (input.PressedAny(Keys.Right, Keys.D, Keys.Tab)) SetTab((Tab)(((int)_tab + 1) % 3));

        int k = (int)_tab;
        if (input.PressedAny(Keys.Up, Keys.W)) _cursor[k]--;
        if (input.PressedAny(Keys.Down, Keys.S)) _cursor[k]++;
        if (input.Pressed(Keys.PageUp)) _cursor[k] -= Rows;
        if (input.Pressed(Keys.PageDown)) _cursor[k] += Rows;
        int wheel = Mouse.GetState().ScrollWheelValue;
        if (_lastWheel != int.MinValue && wheel != _lastWheel) _scroll[k] += wheel < _lastWheel ? 2 : -2;
        _lastWheel = wheel;
        if (input.LeftClick)
            for (int i = 0; i < Rows; i++)
                if (input.Over(RowRect(i)) && _scroll[k] + i < Count)
                {
                    _cursor[k] = _scroll[k] + i;
                    Sfx.Play("tick");
                }

        _cursor[k] = Math.Clamp(_cursor[k], 0, Math.Max(0, Count - 1));
        // Keep the cursor on screen when it moves; the wheel scrolls freely.
        if (_cursor[k] < _scroll[k]) _scroll[k] = _cursor[k];
        if (_cursor[k] >= _scroll[k] + Rows) _scroll[k] = _cursor[k] - Rows + 1;
        _scroll[k] = Math.Clamp(_scroll[k], 0, Math.Max(0, Count - Rows));
    }

    private void SetTab(Tab t)
    {
        if (t == _tab) return;
        _tab = t;
        Sfx.Play("select");
    }

    // ================================================================== draw

    public void Draw(SpriteBatch b, PixelCanvas c, SpriteSheet s)
    {
        Gui.TextCenter(b, c, "ADVENTURER'S JOURNAL", 128, 3, Palette.Yellow, shadow: true);

        foreach (var t in Enum.GetValues<Tab>())
        {
            int seen = t switch
            {
                Tab.Cards => _cards.Count(x => Seen.Cards.Contains(x.Id)),
                Tab.Gear => _gear.Count(x => Seen.Gear.Contains(x.Id)),
                _ => _foes.Count(x => Seen.Enemies.Contains(x.Id))
            };
            int total = t switch { Tab.Cards => _cards.Count, Tab.Gear => _gear.Count, _ => _foes.Count };
            var r = TabRect(t);
            bool on = t == _tab;
            Gui.Panel(b, c, r, on ? Palette.DarkBlue : Palette.Black, on ? Palette.Yellow : r.Contains(_pointer) ? Palette.White : Palette.DarkGrey);
            Gui.TextCenter(b, c, $"{(int)t + 1} {t.ToString().ToUpperInvariant()} {seen}/{total}", r.Center.X, r.Y + 2, on ? Palette.Yellow : Palette.LightGrey);
        }

        Gui.Panel(b, c, new Rectangle(ListX - 2, ListY - 2, ListW + 4, Rows * RowH + 3), Palette.Black, Palette.DarkBlue);
        int k = (int)_tab;
        for (int i = 0; i < Rows && _scroll[k] + i < Count; i++)
        {
            int index = _scroll[k] + i;
            var r = RowRect(i);
            bool selected = index == _cursor[k];
            if (selected) c.Fill(b, r, Palette.DarkBlue);
            c.Fill(b, r.X, r.Y + 1, 2, RowH - 2, GroupColor(index));
            bool seen = IsSeen(index);
            string name = seen ? NameOf(index) : "???";
            TinyFont.Draw(b, c, Gui.Clip(name.ToUpperInvariant(), 23), r.X + 4, r.Y + 1, seen ? NameColor(index) : Palette.DarkGrey);
        }
        if (_scroll[k] > 0) TinyFont.Draw(b, c, "^", ListX + ListW - 4, ListY - 1, Palette.Yellow);
        if (_scroll[k] < Math.Max(0, Count - Rows)) TinyFont.Draw(b, c, "v", ListX + ListW - 4, ListY + Rows * RowH - 6, Palette.Yellow);

        Gui.Panel(b, c, DetailBox, Palette.Black, Palette.DarkBlue);
        if (Count > 0) DrawDetail(b, c, s, _cursor[k]);

        Gui.TextCenter(b, c, "</> TAB   UP/DOWN BROWSE   ESC BACK", 128, 137, Palette.DarkGrey);
    }

    private string NameOf(int i) => _tab switch
    {
        Tab.Cards => _cards[i].Name,
        Tab.Gear => _gear[i].Name,
        _ => _foes[i].Name
    };

    private Color NameColor(int i) => _tab switch
    {
        Tab.Cards => RarityColor(_cards[i].Rarity),
        Tab.Gear => GearText.RarityColor(_gear[i].Rarity),
        _ => _foes[i].Tier switch { 3 => Palette.Red, 2 => Palette.Orange, _ => Palette.White }
    };

    private static Color RarityColor(Rarity r) => r switch
    {
        Rarity.Uncommon => Palette.Green,
        Rarity.Rare => Palette.Blue,
        Rarity.Legendary => Palette.Orange,
        _ => Palette.White
    };

    /// <summary>The 2px stripe at the left of a row: class (cards), slot (gear) or Depth band (foes).</summary>
    private Color GroupColor(int i) => _tab switch
    {
        Tab.Cards => ClassColor(_cards[i].ClassId),
        Tab.Gear => _gear[i].Slot switch
        {
            GearSlot.Weapon => Palette.Red,
            GearSlot.Armor => Palette.Blue,
            GearSlot.OffHand => Palette.Green,
            _ => Palette.Yellow
        },
        _ => _foes[i].MinDepth switch { 1 => Palette.Green, 2 => Palette.Yellow, 3 => Palette.Orange, 4 => Palette.Red, _ => Palette.DarkPurple }
    };

    private static Color ClassColor(string cls) => cls.ToLowerInvariant() switch
    {
        "fighter" => Palette.LightGrey,
        "rogue" => Palette.Lavender,
        "wizard" => Palette.Blue,
        "cleric" => Palette.White,
        "bard" => Palette.Pink,
        "ranger" => Palette.DarkGreen,
        "druid" => Palette.Brown,
        "paladin" => Palette.Yellow,
        "neutral" => Palette.Orange,
        _ => Palette.DarkGrey
    };

    private void DrawDetail(SpriteBatch b, PixelCanvas c, SpriteSheet s, int i)
    {
        var box = DetailBox;
        int x = box.X + 4, y = box.Y + 4;
        const int chars = 35;
        var lines = new List<(string Text, Color Color)>();

        if (!IsSeen(i))
        {
            Gui.TextCenter(b, c, "???", box.Center.X, box.Y + 40, Palette.DarkGrey);
            string hint = _tab switch
            {
                Tab.Cards => "OFFERED IN DRAFTS, SHOPS AND EVENTS",
                Tab.Gear => "FOUND IN CHESTS, SHOPS AND LOOT",
                _ => "MEET IT IN THE DUNGEON"
            };
            Gui.TextCenter(b, c, "NOT SEEN YET", box.Center.X, box.Y + 50, Palette.LightGrey);
            Gui.TextCenter(b, c, hint, box.Center.X, box.Y + 58, Palette.DarkGrey);
            return;
        }

        switch (_tab)
        {
            case Tab.Cards:
            {
                var card = _cards[i];
                lines.Add((card.Name.ToUpperInvariant(), RarityColor(card.Rarity)));
                string cls = card.ClassId.Equals("basic", StringComparison.OrdinalIgnoreCase) ? "CLASS ACTION" : card.ClassId.ToUpperInvariant();
                lines.Add(($"{cls}  {card.Rarity.ToString().ToUpperInvariant()}", ClassColor(card.ClassId) == Palette.DarkGrey ? Palette.LightGrey : ClassColor(card.ClassId)));
                string speed = card.Speed.ToString().ToUpperInvariant() + (card.Check == CheckKind.None ? "" : " " + card.Check.ToString().ToUpperInvariant())
                               + (card.Spell ? " (SPELL)" : "") + (card.Uses > 0 ? $"  {card.Uses}/FIGHT" : card.Exhaust ? "  ONCE" : "");
                lines.Add((speed, Palette.Lavender));
                lines.Add(("", Palette.White));
                lines.AddRange(Gui.Wrap(card.Text.ToUpperInvariant(), chars).Select(l => (l, Palette.White)));
                if (card.Upgrade?.Text is { } up)
                {
                    lines.Add(("", Palette.White));
                    lines.Add(("UPGRADED (+):", Palette.Green));
                    lines.AddRange(Gui.Wrap(up.ToUpperInvariant(), chars).Select(l => (l, Palette.LightGrey)));
                }
                break;
            }
            case Tab.Gear:
            {
                var g = _gear[i];
                lines.Add((g.Name.ToUpperInvariant(), GearText.RarityColor(g.Rarity)));
                lines.Add(($"{GearText.SlotName(g.Slot)}  {g.Rarity.ToString().ToUpperInvariant()}", Palette.Lavender));
                lines.AddRange(Gui.Wrap(GearText.Stats(new GearInstance(g)), chars).Select(l => (l, Palette.Yellow)));
                if (g.Passive is { } p) lines.Add((PassiveText(p), Palette.Blue));
                lines.Add(("", Palette.White));
                if (g.Text.Length > 0) lines.AddRange(Gui.Wrap(g.Text.ToUpperInvariant(), chars).Select(l => (l, Palette.White)));
                var users = _content.Heroes.Values.Where(h => ContentDb.CanUse(h, g)).Select(h => h.Name.ToUpperInvariant()).ToList();
                lines.Add(("", Palette.White));
                lines.Add(("USED BY:", Palette.LightGrey));
                lines.AddRange(Gui.Wrap(users.Count == _content.Heroes.Count ? "EVERYONE" : users.Count == 0 ? "NOBODY" : string.Join(", ", users), chars)
                    .Select(l => (l, Palette.LightGrey)));
                break;
            }
            default:
            {
                var e = _foes[i];
                string sprite = s.Has("enemy." + e.Id) ? "enemy." + e.Id : "fight";
                s.Draw(b, sprite, box.Right - 13, box.Y + 4, flipX: true);
                lines.Add((e.Name.ToUpperInvariant(), NameColor(i)));
                string tier = e.Tier switch { 3 => "BOSS", 2 => "ELITE", _ => "FOE" };
                string depths = e.Tier == 3 ? $"DEPTH {e.MinDepth}" : e.MaxDepth >= 9 ? $"DEPTH {e.MinDepth}+" : $"DEPTH {e.MinDepth}-{e.MaxDepth}";
                if (!e.Random && e.Tier < 3) depths = "IN DISGUISE";
                lines.Add(($"{tier}  {depths}" + (e.Type is { Length: > 0 } type ? "  " + type.ToUpperInvariant() : ""), Palette.Lavender));
                // HP as fought on Normal (published HP x game-side scaling); AC as published (deep Depths may cap it lower).
                int hp = (int)Math.Round(e.Hp * e.HpScale * _content.Balance.EnemyHpMultiplier);
                lines.Add(($"HP {hp}  AC {e.Ac}  DEFEATED {JournalStore.Tracker.DefeatedCount(e.Id)}", Palette.Yellow));
                lines.Add(("", Palette.White));
                foreach (var intent in e.Intents)
                    foreach (string l in Gui.Wrap(IntentText(intent), chars))
                        lines.Add((l, intent.Big ? Palette.Orange : Palette.White));
                if (e.Regen > 0)
                    lines.Add(($"REGENERATES {e.Regen}" + (e.RegenStoppedBy is { Count: > 0 } stop ? $" (NOT AFTER {string.Join("/", stop).ToUpperInvariant()})" : ""), Palette.Green));
                if (e.Heads > 0)
                    lines.Add(($"{e.Heads} HEADS. {e.SeverAt}+ DAMAGE SEVERS ONE;", Palette.Green));
                if (e.Heads > 0)
                    lines.Add(("THEY REGROW UNLESS BURNED.", Palette.Green));
                if (e.Summon is { } summon)
                    lines.Add(($"AT {(int)Math.Round(summon.AtHpFraction * 100)}% HP IT CALLS ALLIES.", Palette.Green));
                break;
            }
        }

        int maxLines = (box.Height - 8) / 7;
        foreach (var (text, color) in lines.Take(maxLines))
        {
            TinyFont.Draw(b, c, text, x, y, color);
            y += 7;
        }
    }

    private static string PassiveText(GearPassive p) => p.Type.ToLowerInvariant() switch
    {
        "onhit" => $"EACH HIT: +{p.Dice?.ToUpperInvariant()} {p.DamageType.ToUpperInvariant()}",
        "oncrit" => $"CRITS: +{p.Dice?.ToUpperInvariant()}",
        "poisononhit" => $"HITS POISON: {p.Amount} FOR {p.Rounds} TURNS",
        "regen" => $"HEAL {p.Amount} EACH TURN",
        "bracers" => $"+{p.Amount} AC WITH NO ARMOR OR SHIELD",
        _ => p.Type.ToUpperInvariant()
    };

    /// <summary>"BITE +5 2D6+3 +2D6 POISON" / "CONE OF COLD: ALL, CON 15 HALF, RECHARGE" / "COIL: GUARD 10".</summary>
    private static string IntentText(IntentDef i)
    {
        if (i.Kind == IntentKind.Guard) return $"{i.Name.ToUpperInvariant()}: GUARD {i.Block}";
        var parts = new List<string> { i.Name.ToUpperInvariant() + ":" };
        if (i.Area) parts.Add("ALL");
        if (i.PerHead) parts.Add("PER HEAD");
        if (i.Save is { Length: > 0 } save) parts.Add($"{save.ToUpperInvariant()} {i.SaveDc}");
        else parts.Add($"+{i.AttackBonus}");
        if (i.Damage.Trim() != "0") parts.Add(i.Damage.ToUpperInvariant());
        if (i.Extra is { Length: > 0 } extra) parts.Add($"+{extra.ToUpperInvariant()} {i.ExtraType.ToUpperInvariant()}");
        if (i.Poison > 0) parts.Add("POISON");
        if (i.Drain) parts.Add("DRAIN");
        if (i.Condition is { Length: > 0 } cond) parts.Add(cond.ToUpperInvariant());
        if (i.Recharge > 0) parts.Add("RECHARGE");
        if (i.Legendary) parts.Add("(LEGENDARY)");
        if (i.Big) parts.Add("BIG");
        return string.Join(" ", parts);
    }
}
