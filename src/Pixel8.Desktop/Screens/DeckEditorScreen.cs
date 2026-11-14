using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Pixel8.Core.Cards;
using Pixel8.Core.Content;
using Pixel8.Core.Heroes;
using Pixel8.Core.Randomness;
using Pixel8.Desktop.Fx;
using Pixel8.Desktop.Rendering;
using Pixel8.Desktop.Ui;

namespace Pixel8.Desktop.Screens;

/// <summary>
/// Debug deck editor (round 8): sculpt each hero's deck. Left column = the hero's cards (hand, draw pile and
/// discard), right column = cards that can be added (the hero's class + neutral; F shows every class).
/// ←/→ hero · Tab (or click a column) switch column · ↑/↓ pick · Enter or click the picked row again:
/// remove (left) or add (right) · U toggle upgrade (left) · R restock this hero's deck · Esc done.
/// No minimum deck size here: the basic actions are always available anyway.
/// </summary>
public sealed class DeckEditorScreen : IScreen
{
    private const int Top = 14, RowH = 7, Rows = 16, LeftX = 4, RightX = 130, ColW = 122;

    private readonly IReadOnlyList<Hero> _heroes;
    private readonly ContentDb _content;
    private readonly Rng _rng;
    private readonly Action _done;
    private int _hero;
    private bool _right;
    private int _leftRow, _rightRow, _leftScroll, _rightScroll;
    private bool _allClasses;
    private string _note = "";

    public DeckEditorScreen(IReadOnlyList<Hero> heroes, ContentDb content, Rng rng, Action done)
    {
        _heroes = heroes.Where(h => !h.IsDead).ToList();
        _content = content;
        _rng = rng;
        _done = done;
        Sfx.Play("select");
    }

    public Point ShakeOffset => Point.Zero;

    private Hero? Hero => _heroes.Count == 0 ? null : _heroes[_hero];

    private List<CardInstance> Deck() => Hero is null ? new()
        : Hero.Deck.AllCards.OrderBy(c => c.BaseDef.Name).ThenBy(c => c.Upgraded).ToList();

    private List<CardDef> Catalogue() => Hero is null ? new()
        : _content.Cards.Values
            .Where(c => !c.ClassId.Equals("basic", StringComparison.OrdinalIgnoreCase))
            .Where(c => _allClasses || c.ClassId.Equals(Hero.Class.Id, StringComparison.OrdinalIgnoreCase)
                                    || c.ClassId.Equals("neutral", StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.ClassId == "neutral").ThenBy(c => c.ClassId).ThenBy(c => c.Rarity).ThenBy(c => c.Name)
            .ToList();

    private static Rectangle RowRect(bool right, int row) => new(right ? RightX : LeftX, Top + 9 + row * RowH, ColW, RowH - 1);

    public void Update(float dt, Input input)
    {
        if (input.Pressed(Keys.Escape) || input.RightClick)
        {
            Sfx.Play("select");
            _done();
            return;
        }
        if (Hero is null) return;

        if (input.PressedAny(Keys.Left, Keys.A)) SwitchHero(-1);
        if (input.PressedAny(Keys.Right, Keys.D)) SwitchHero(+1);
        if (input.Pressed(Keys.Tab)) _right = !_right;
        if (input.Pressed(Keys.F)) { _allClasses = !_allClasses; _rightRow = _rightScroll = 0; Sfx.Play("tick"); }
        if (input.Pressed(Keys.R))
        {
            Hero.Deck.Restock(_rng);
            _note = $"{Hero.Name} restocked";
            Sfx.Play("heal");
        }

        int count = _right ? Catalogue().Count : Deck().Count;
        ref int rowRef = ref _right ? ref _rightRow : ref _leftRow;
        if (input.PressedAny(Keys.Up, Keys.W)) rowRef = Math.Max(0, rowRef - 1);
        if (input.PressedAny(Keys.Down, Keys.S)) rowRef = Math.Min(Math.Max(0, count - 1), rowRef + 1);

        if (input.LeftClick)
        {
            for (int i = 0; i < Rows; i++)
                foreach (bool side in new[] { false, true })
                {
                    if (!input.Over(RowRect(side, i))) continue;
                    int index = (side ? _rightScroll : _leftScroll) + i;
                    int sideCount = side ? Catalogue().Count : Deck().Count;
                    if (index >= sideCount) continue;
                    bool again = _right == side && (side ? _rightRow : _leftRow) == index;
                    _right = side;
                    if (side) _rightRow = index; else _leftRow = index;
                    if (again) Act();
                }
        }
        else if (input.PressedAny(Keys.Enter, Keys.Space)) Act();

        if (!_right && input.Pressed(Keys.U))
        {
            var deck = Deck();
            if (_leftRow < deck.Count && deck[_leftRow].BaseDef.CanUpgrade)
            {
                deck[_leftRow].Upgraded = !deck[_leftRow].Upgraded;
                _note = $"{deck[_leftRow].DisplayName}";
                Sfx.Play("good");
            }
            else Sfx.Play("deny");
        }

        Clamp();
    }

    private void SwitchHero(int delta)
    {
        _hero = (_hero + delta + _heroes.Count) % _heroes.Count;
        _leftRow = _leftScroll = 0;
        _rightRow = _rightScroll = 0;
        Sfx.Play("select");
    }

    private void Act()
    {
        var hero = Hero!;
        if (_right)
        {
            var cat = Catalogue();
            if (_rightRow >= cat.Count) return;
            hero.Deck.AddCard(new CardInstance(cat[_rightRow]), _rng);
            _note = $"+ {cat[_rightRow].Name}";
            Sfx.Play("confirm");
        }
        else
        {
            var deck = Deck();
            if (_leftRow >= deck.Count) return;
            hero.Deck.RemoveCard(deck[_leftRow]);
            _note = $"- {deck[_leftRow].DisplayName}";
            Sfx.Play("deny");
        }
    }

    private void Clamp()
    {
        int deckCount = Deck().Count, catCount = Catalogue().Count;
        _leftRow = Math.Clamp(_leftRow, 0, Math.Max(0, deckCount - 1));
        _rightRow = Math.Clamp(_rightRow, 0, Math.Max(0, catCount - 1));
        if (_leftRow < _leftScroll) _leftScroll = _leftRow;
        if (_leftRow >= _leftScroll + Rows) _leftScroll = _leftRow - Rows + 1;
        if (_rightRow < _rightScroll) _rightScroll = _rightRow;
        if (_rightRow >= _rightScroll + Rows) _rightScroll = _rightRow - Rows + 1;
    }

    public void Draw(SpriteBatch b, PixelCanvas c, SpriteSheet s)
    {
        c.Fill(b, new Rectangle(0, 0, PixelCanvas.Width, PixelCanvas.Height), Palette.Black);
        var hero = Hero;
        if (hero is null)
        {
            Gui.TextCenter(b, c, "NO LIVING HEROES", 128, 60, Palette.Red);
            return;
        }

        var deck = Deck();
        var cat = Catalogue();
        Gui.TextCenter(b, c, $"< {hero.Name.ToUpperInvariant()} >  DECK EDITOR", 128, 3, Palette.Pink, shadow: true);
        Gui.TextRight(b, c, $"HAND {hero.Deck.Hand.Count} DRAW {hero.Deck.DrawPile.Count} DISC {hero.Deck.Discard.Count}", 252, 3, Palette.DarkGrey);

        DrawColumn(b, c, false, $"DECK ({deck.Count})", deck.Select(x => (x.DisplayName, x.Upgraded ? Palette.Green : RarityColor(x.BaseDef.Rarity))).ToList(), _leftRow, _leftScroll);
        DrawColumn(b, c, true, _allClasses ? "ADD: ALL CLASSES" : "ADD: CLASS + NEUTRAL",
            cat.Select(x => ($"{x.Name} {x.Rarity.ToString()[0]}", RarityColor(x.Rarity))).ToList(), _rightRow, _rightScroll);

        string footer = _note.Length > 0 ? _note.ToUpperInvariant()
            : "TAB SWITCH  ENTER ADD/REMOVE  U UPGRADE  F ALL  R RESTOCK  ESC DONE";
        Gui.TextCenter(b, c, Gui.Clip(footer, 62), 128, 137, _note.Length > 0 ? Palette.Green : Palette.DarkGrey);
    }

    private void DrawColumn(SpriteBatch b, PixelCanvas c, bool right, string title, List<(string Text, Color Color)> rows, int selected, int scroll)
    {
        int x = right ? RightX : LeftX;
        var frame = new Rectangle(x - 1, Top, ColW + 2, 9 + Rows * RowH + 2);
        Gui.Panel(b, c, frame, Palette.Black, _right == right ? Palette.Pink : Palette.DarkBlue);
        TinyFont.Draw(b, c, title, x + 2, Top + 2, _right == right ? Palette.Yellow : Palette.LightGrey);
        for (int i = 0; i < Rows && scroll + i < rows.Count; i++)
        {
            var r = RowRect(right, i);
            var (text, color) = rows[scroll + i];
            if (scroll + i == selected && _right == right) c.Fill(b, r, Palette.DarkBlue);
            TinyFont.Draw(b, c, Gui.Clip(text.ToUpperInvariant(), 30), r.X + 2, r.Y + 1, color);
        }
        if (scroll > 0) TinyFont.Draw(b, c, "*", frame.Right - 6, Top + 2, Palette.LightGrey);
        if (scroll + Rows < rows.Count) TinyFont.Draw(b, c, "*", frame.Right - 6, frame.Bottom - 7, Palette.LightGrey);
    }

    private static Color RarityColor(Rarity r) => r switch
    {
        Rarity.Uncommon => Palette.Blue,
        Rarity.Rare => Palette.Orange,
        Rarity.Legendary => Palette.Yellow,
        _ => Palette.White
    };
}
