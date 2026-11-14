using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Pixel8.Core.Cards;
using Pixel8.Core.Heroes;
using Pixel8.Core.Rewards;
using Pixel8.Core.Runs;
using Pixel8.Desktop.Fx;
using Pixel8.Desktop.Rendering;
using Pixel8.Desktop.Ui;

namespace Pixel8.Desktop.Screens;

/// <summary>
/// Lists every card the living heroes own, one column per hero, for "pick a card" moments (campfire upgrade,
/// merchant removal and upgrade). Cards that fail the filter are greyed out. Hover shows the card text
/// (and, in upgrade mode, what it becomes).
/// </summary>
internal sealed class DeckPicker
{
    private const int Top = 16, RowH = 7, Rows = 11, DetailY = 110;

    private readonly List<(Hero Hero, List<CardInstance> Cards)> _columns;
    private readonly Func<CardInstance, bool> _filter;
    private readonly bool _showUpgrade;
    private (Hero Hero, CardInstance Card)? _hover;

    /// <param name="only">Show just this hero's deck (level-ups).</param>
    public DeckPicker(Run run, Func<CardInstance, bool> filter, string title, bool showUpgrade, Hero? only = null)
    {
        Title = title;
        _filter = filter;
        _showUpgrade = showUpgrade;
        _columns = run.Party.Heroes.Where(h => !h.IsDead && (only is null || ReferenceEquals(h, only)))
            .Select(h => (h, h.Deck.AllCards.OrderBy(c => c.BaseDef.Name).ThenBy(c => c.Upgraded).ToList()))
            .ToList();
    }

    public string Title { get; }

    public bool HasChoices => _columns.Any(col => col.Cards.Any(_filter));

    private Rectangle ColumnRect(int i)
    {
        int w = 248 / Math.Max(1, _columns.Count);
        return new Rectangle(4 + i * w, Top, w - 4, 8 + Rows * RowH + 3);
    }

    private Rectangle RowRect(int column, int row)
    {
        var col = ColumnRect(column);
        return new Rectangle(col.X + 1, col.Y + 9 + row * RowH, col.Width - 2, RowH);
    }

    /// <summary>Returns the card the player clicked (or picked with 1–9 in the first column), else null.</summary>
    public (Hero Hero, CardInstance Card)? Update(Input input)
    {
        _hover = null;
        for (int c = 0; c < _columns.Count; c++)
        {
            var cards = _columns[c].Cards;
            for (int r = 0; r < Math.Min(Rows, cards.Count); r++)
            {
                if (!input.Over(RowRect(c, r))) continue;
                _hover = (_columns[c].Hero, cards[r]);
                if (input.LeftClick)
                {
                    if (_filter(cards[r])) return _hover;
                    Sfx.Play("deny");
                }
            }
        }
        return null;
    }

    public void Draw(SpriteBatch b, PixelCanvas c, SpriteSheet s)
    {
        Gui.TextCenter(b, c, Title, 128, 5, Palette.Yellow, shadow: true);
        int chars = Math.Max(4, (ColumnRect(0).Width - 4) / TinyFont.Advance);

        for (int i = 0; i < _columns.Count; i++)
        {
            var (hero, cards) = _columns[i];
            var col = ColumnRect(i);
            Gui.Panel(b, c, col, Palette.Black, Palette.DarkBlue);
            string sprite = s.Has("hero." + hero.Class.Id) ? "hero." + hero.Class.Id : "hero";
            s.Draw(b, sprite, col.X + 2, col.Y + 1);
            TinyFont.Draw(b, c, Gui.Clip(hero.Name.ToUpperInvariant(), chars - 3), col.X + 12, col.Y + 2, Palette.Peach);

            for (int r = 0; r < Math.Min(Rows, cards.Count); r++)
            {
                var card = cards[r];
                var rr = RowRect(i, r);
                bool ok = _filter(card);
                bool hover = _hover is { } h && ReferenceEquals(h.Card, card);
                if (hover) c.Fill(b, rr, ok ? Palette.DarkBlue : Palette.DarkPurple);
                var color = !ok ? Palette.DarkGrey : hover ? Palette.Yellow : card.Upgraded ? Palette.Green : Palette.White;
                TinyFont.Draw(b, c, Gui.Clip(card.DisplayName.ToUpperInvariant(), chars), rr.X + 2, rr.Y + 1, color);
            }
            if (cards.Count > Rows)
                TinyFont.Draw(b, c, $"+{cards.Count - Rows} MORE", col.X + 3, col.Bottom - 1, Palette.DarkGrey);
        }

        if (_hover is { } hv)
        {
            var def = hv.Card.Def;
            string now = $"{hv.Card.DisplayName}: {def.Text}".ToUpperInvariant();
            bool upgrade = _showUpgrade && hv.Card.CanUpgrade;
            var lines = Gui.Wrap(now, 62).Take(upgrade ? 1 : 3).Select(l => (l, Palette.White)).ToList();
            if (upgrade)
                lines.AddRange(Gui.Wrap("UPGRADED: " + hv.Card.BaseDef.Upgraded().Text.ToUpperInvariant(), 62).Take(2).Select(l => (l, Palette.Green)));
            int y = DetailY;
            foreach (var (line, color) in lines)
            {
                TinyFont.Draw(b, c, line, 4, y, color);
                y += 7;
            }
        }
    }
}

/// <summary>
/// Campfire choice (M4, locked 2026-11-13): Rest (every hero heals a third and restocks) or Upgrade one card.
/// With the "shop at campfires" option a third button opens this fire's merchant (shopping doesn't use up the choice).
/// Keys: R / 1 rest · U / 2 upgrade · M / 3 shop · Esc back (from the card list).
/// </summary>
public sealed class CampfireScreen : IScreen
{
    private static readonly Rectangle RestButton = new(40, 92, 80, 13);
    private static readonly Rectangle UpgradeButton = new(136, 92, 80, 13);
    private static readonly Rectangle ShopButton = new(88, 122, 80, 11);
    private static readonly Rectangle BackButton = new(88, 133, 80, 10);

    private readonly Run _run;
    private readonly Action<string> _onDone;
    private readonly Merchant? _merchant;
    private DeckPicker? _picker;
    private MerchantScreen? _shop;
    private Point _pointer;
    private float _time;

    /// <param name="onDone">Called with a message for the map once the party has chosen.</param>
    /// <param name="merchant">This fire's merchant when the shop lives at campfires; null otherwise.</param>
    public CampfireScreen(Run run, Action<string> onDone, Merchant? merchant = null)
    {
        _run = run;
        _onDone = onDone;
        _merchant = merchant;
    }

    public Point ShakeOffset => Point.Zero;

    private bool CanUpgrade => _run.UpgradableCards().Any();

    public void Update(float dt, Input input)
    {
        _time += dt;
        _pointer = input.Pointer;

        if (_shop is not null)
        {
            _shop.Update(dt, input);
            return;
        }

        if (_merchant is not null && (input.Clicked(ShopButton) || input.PressedAny(Keys.M, Keys.D3, Keys.NumPad3)) && _picker is null)
        {
            Sfx.Play("select");
            _shop = new MerchantScreen(_run, _merchant, () => _shop = null);
            return;
        }

        if (_picker is not null)
        {
            if (input.Pressed(Keys.Escape) || input.RightClick || input.Clicked(BackButton))
            {
                _picker = null;
                Sfx.Play("select");
                return;
            }
            var pick = _picker.Update(input);
            if (pick is { } p && _run.UpgradeCard(p.Card))
            {
                Sfx.Play("perfect");
                _onDone($"CAMPFIRE: {p.Hero.Name}'s {p.Card.BaseDef.Name} is upgraded.".ToUpperInvariant());
            }
            return;
        }

        if (input.Clicked(RestButton) || input.PressedAny(Keys.R, Keys.D1, Keys.NumPad1))
        {
            _run.Rest();
            Sfx.Play("heal");
            _onDone("CAMPFIRE: HEROES HEAL AND RESTOCK.");
        }
        else if (input.Clicked(UpgradeButton) || input.PressedAny(Keys.U, Keys.D2, Keys.NumPad2))
        {
            if (!CanUpgrade)
            {
                Sfx.Play("deny");
                return;
            }
            Sfx.Play("select");
            _picker = new DeckPicker(_run, c => c.CanUpgrade, "UPGRADE ONE CARD", showUpgrade: true);
        }
    }

    public void Draw(SpriteBatch b, PixelCanvas c, SpriteSheet s)
    {
        if (_shop is not null)
        {
            _shop.Draw(b, c, s);
            return;
        }
        if (_picker is not null)
        {
            _picker.Draw(b, c, s);
            Gui.Button(b, c, BackButton, "ESC  BACK", BackButton.Contains(_pointer));
            return;
        }

        if (_merchant is not null)
            Gui.Button(b, c, ShopButton, "3  SHOP", ShopButton.Contains(_pointer), true, Palette.Yellow);

        Gui.TextCenter(b, c, "CAMPFIRE", 128, 8, Palette.Orange, shadow: true);
        s.Draw(b, (int)(_time * 6) % 2 == 0 ? "camp0" : "camp1", 124, 20);
        Gui.TextCenter(b, c, "THE PARTY SETTLES BY THE FIRE. CHOOSE ONE:", 128, 34, Palette.Peach);

        int y = 46;
        foreach (var h in _run.Party.Heroes)
        {
            string sprite = h.IsDead ? "i.skull" : s.Has("hero." + h.Class.Id) ? "hero." + h.Class.Id : "hero";
            s.Draw(b, sprite, 70, y);
            TinyFont.Draw(b, c, Gui.Clip(h.Name.ToUpperInvariant(), 9), 82, y + 2, h.IsDead ? Palette.DarkGrey : Palette.White);
            Gui.HpBar(b, c, 122, y + 3, 40, h.Hp, h.MaxHp);
            TinyFont.Draw(b, c, h.IsDead ? "DEAD" : $"{h.Hp}/{h.MaxHp}", 166, y + 2, Palette.LightGrey);
            y += 11;
        }

        Gui.Button(b, c, RestButton, "1  REST", RestButton.Contains(_pointer), true, Palette.Green);
        Gui.Button(b, c, UpgradeButton, "2  UPGRADE", UpgradeButton.Contains(_pointer), CanUpgrade, Palette.Blue);
        Gui.TextCenter(b, c, "HEAL 1/3 HP", RestButton.Center.X, RestButton.Bottom + 3, Palette.LightGrey);
        Gui.TextCenter(b, c, "+ RESTOCK DECKS", RestButton.Center.X, RestButton.Bottom + 10, Palette.LightGrey);
        Gui.TextCenter(b, c, CanUpgrade ? "IMPROVE ONE CARD" : "NOTHING TO UPGRADE", UpgradeButton.Center.X, UpgradeButton.Bottom + 3, Palette.LightGrey);
        Gui.TextCenter(b, c, "FOR GOOD", UpgradeButton.Center.X, UpgradeButton.Bottom + 10, Palette.LightGrey);
    }
}

/// <summary>
/// The merchant (M4): one card per hero, potions, paid card removal and paid upgrade (once per visit each).
/// Keys: 1–3 buy a card · Esc leave (or back out of a card list).
/// </summary>
public sealed class MerchantScreen : IScreen
{
    private const int CardW = 62, CardH = 46, CardY = 16, ItemY = 66, GearY = 79, ServiceY = 92, DetailY = 106;
    private static readonly Rectangle LeaveButton = new(88, 133, 80, 10);
    /// <summary>Round 10: sell gear from the pack for half price.</summary>
    private static readonly Rectangle SellButton = new(172, ServiceY, 80, 11);
    private bool _selling;
    private static Rectangle SellRect(int i) => new(8 + i % 2 * 122, 22 + i / 2 * 13, 118, 11);

    private readonly Run _run;
    private readonly Merchant _merchant;
    private readonly Action _onDone;
    private DeckPicker? _picker;
    private ShopOffer? _service;
    private ShopOffer? _hover;
    private string _message = "";
    private Point _pointer;

    public MerchantScreen(Run run, Merchant merchant, Action onDone)
    {
        _run = run;
        _merchant = merchant;
        _onDone = onDone;
    }

    public Point ShakeOffset => Point.Zero;

    private List<ShopOffer> Of(ShopKind kind) => _merchant.Offers.Where(o => o.Kind == kind).ToList();

    private IEnumerable<(ShopOffer Offer, Rectangle Rect)> Layout()
    {
        var cards = Of(ShopKind.Card);
        int total = cards.Count * CardW + Math.Max(0, cards.Count - 1) * 6;
        for (int i = 0; i < cards.Count; i++)
            yield return (cards[i], new Rectangle((256 - total) / 2 + i * (CardW + 6), CardY, CardW, CardH));

        var items = Of(ShopKind.Item);
        int itemsW = items.Count * 60 + Math.Max(0, items.Count - 1) * 4;
        for (int i = 0; i < items.Count; i++)
            yield return (items[i], new Rectangle((256 - itemsW) / 2 + i * 64, ItemY, 60, 11));

        var gear = Of(ShopKind.Gear);
        for (int i = 0; i < gear.Count; i++)
            yield return (gear[i], new Rectangle(4 + i * 84, GearY, 80, 11));

        var services = Of(ShopKind.RemoveCard).Concat(Of(ShopKind.UpgradeCard)).ToList();
        for (int i = 0; i < services.Count; i++)
            yield return (services[i], new Rectangle(4 + i * 84, ServiceY, 80, 11));
    }

    public void Update(float dt, Input input)
    {
        _pointer = input.Pointer;

        if (_selling)
        {
            if (input.Pressed(Keys.Escape) || input.RightClick || input.Clicked(LeaveButton))
            {
                _selling = false;
                Sfx.Play("back");
                return;
            }
            var pack = _run.Party.Pack;
            for (int i = 0; i < pack.Count; i++)
                if (input.Clicked(SellRect(i)))
                {
                    var g = pack[i];
                    int price = _run.SellPrice(g);
                    Merchant.Sell(_run.Party, g, price);
                    _message = $"Sold {g.Name} for {price} gold.";
                    Sfx.Play("coins");
                    if (pack.Count == 0) _selling = false;
                    return;
                }
            return;
        }

        if (input.Clicked(SellButton))
        {
            if (_run.Party.Pack.Count == 0) { _message = "THE PACK IS EMPTY: NOTHING TO SELL."; Sfx.Play("deny"); }
            else { _selling = true; Sfx.Play("select"); }
            return;
        }

        if (_picker is not null)
        {
            if (input.Pressed(Keys.Escape) || input.RightClick || input.Clicked(LeaveButton))
            {
                _picker = null;
                _service = null;
                Sfx.Play("select");
                return;
            }
            if (_picker.Update(input) is { } pick && _service is not null)
            {
                if (_service.Kind == ShopKind.RemoveCard)
                {
                    _merchant.RemoveCard(_run.Party, _service, pick.Hero, pick.Card);
                    _message = $"{pick.Card.DisplayName} is gone from {pick.Hero.Name}'s deck.";
                }
                else
                {
                    _merchant.UpgradeCard(_run.Party, _service, pick.Card);
                    _message = $"{pick.Hero.Name}'s {pick.Card.BaseDef.Name} is upgraded.";
                }
                Sfx.Play("buy");
                _picker = null;
                _service = null;
            }
            return;
        }

        if (input.Pressed(Keys.Escape) || input.Clicked(LeaveButton))
        {
            Sfx.Play("back");
            _onDone();
            return;
        }

        _hover = null;
        var layout = Layout().ToList();
        foreach (var (offer, rect) in layout)
            if (input.Over(rect)) _hover = offer;

        ShopOffer? chosen = input.LeftClick ? _hover : null;
        int digit = input.PressedDigit();
        var cards = Of(ShopKind.Card);
        if (digit >= 0 && digit < cards.Count) chosen = cards[digit];
        if (chosen is not null) Choose(chosen);
    }

    private void Choose(ShopOffer offer)
    {
        if (offer.Sold) { _message = "SOLD OUT."; Sfx.Play("deny"); return; }
        if (!Merchant.CanAfford(_run.Party, offer)) { _message = $"YOU NEED {offer.Price} GOLD."; Sfx.Play("deny"); return; }

        switch (offer.Kind)
        {
            case ShopKind.Card:
                if (offer.Hero!.IsDead) { _message = $"{offer.Hero.Name} can't use it now."; Sfx.Play("deny"); return; }
                _merchant.Buy(_run.Party, offer, _run.LootRng);
                _message = $"{offer.Card!.Name} joins {offer.Hero.Name}'s deck.";
                Sfx.Play("buy");
                break;
            case ShopKind.Item:
                _merchant.Buy(_run.Party, offer, _run.LootRng);
                _message = $"{offer.Item!.Name} goes in the bag: B in combat, P on the map.";
                Sfx.Play("buy");
                break;
            case ShopKind.Gear:
                if (_run.Party.PackFull) { _message = "THE PACK IS FULL. SELL OR DROP SOMETHING FIRST."; Sfx.Play("deny"); return; }
                _merchant.Buy(_run.Party, offer, _run.LootRng);
                _message = $"{offer.Gear!.Name} goes in the pack. Press G on the map to equip it.";
                Sfx.Play("buy");
                break;
            case ShopKind.RemoveCard:
                _service = offer;
                _picker = new DeckPicker(_run, _ => true, $"REMOVE ONE CARD ({offer.Price} GOLD)", showUpgrade: false);
                Sfx.Play("select");
                break;
            case ShopKind.UpgradeCard:
                if (!_run.UpgradableCards().Any()) { _message = "NOTHING TO UPGRADE."; Sfx.Play("deny"); return; }
                _service = offer;
                _picker = new DeckPicker(_run, c => c.CanUpgrade, $"UPGRADE ONE CARD ({offer.Price} GOLD)", showUpgrade: true);
                Sfx.Play("select");
                break;
        }
    }

    public void Draw(SpriteBatch b, PixelCanvas c, SpriteSheet s)
    {
        if (_selling)
        {
            Gui.TextCenter(b, c, "SELL GEAR (HALF PRICE)", 128, 5, Palette.Peach, shadow: true);
            Gui.TextRight(b, c, $"GOLD {_run.Party.Gold}", 252, 5, Palette.Yellow, shadow: true);
            var pack = _run.Party.Pack;
            for (int i = 0; i < pack.Count; i++)
            {
                var g = pack[i];
                var r = SellRect(i);
                Gui.IconButton(b, c, s, r, GearIcons.For(g.Def), $"{Gui.Clip(g.Name.ToUpperInvariant(), 20)} {_run.SellPrice(g)}G", r.Contains(_pointer), true, GearText.RarityColor(g.EffectiveRarity));
            }
            Gui.Button(b, c, LeaveButton, "ESC  BACK", LeaveButton.Contains(_pointer));
            return;
        }
        if (_picker is not null)
        {
            _picker.Draw(b, c, s);
            Gui.TextRight(b, c, $"GOLD {_run.Party.Gold}", 252, 5, Palette.Yellow, shadow: true);
            Gui.Button(b, c, LeaveButton, "ESC  BACK", LeaveButton.Contains(_pointer));
            return;
        }

        s.Draw(b, "shop", 4, 3);
        TinyFont.DrawShadowed(b, c, "MERCHANT", 14, 5, Palette.Peach);
        Gui.TextRight(b, c, $"GOLD {_run.Party.Gold}", 252, 5, Palette.Yellow, shadow: true);

        int index = 0;
        foreach (var (offer, r) in Layout())
        {
            bool hover = ReferenceEquals(offer, _hover);
            bool afford = Merchant.CanAfford(_run.Party, offer);
            if (offer.Kind == ShopKind.Card) DrawCardOffer(b, c, s, offer, r, hover, afford, index++);
            else
            {
                string label = offer.Sold ? "SOLD" : $"{ShortName(offer)} {offer.Price}G";
                var accent = offer.Kind switch
                {
                    ShopKind.Item => Palette.Green,
                    ShopKind.Gear => GearText.RarityColor(offer.Gear!.EffectiveRarity),
                    _ => Palette.Pink
                };
                if (offer.Kind == ShopKind.Gear && !offer.Sold)
                    Gui.IconButton(b, c, s, r, GearIcons.For(offer.Gear!.Def), label, hover, afford, accent);
                else Gui.Button(b, c, r, label, hover && !offer.Sold, !offer.Sold && afford, accent);
            }
        }
        Gui.Button(b, c, SellButton, "SELL GEAR", SellButton.Contains(_pointer), _run.Party.Pack.Count > 0, Palette.Yellow);

        string detail = _message.Length > 0 && _hover is null ? _message : Detail(_hover);
        int y = DetailY;
        foreach (string line in Gui.Wrap(detail.ToUpperInvariant(), 62).Take(3))
        {
            TinyFont.Draw(b, c, line, 4, y, _hover is null ? Palette.Green : Palette.White);
            y += 7;
        }
        Gui.Button(b, c, LeaveButton, "ESC  LEAVE", LeaveButton.Contains(_pointer));
    }

    private static string ShortName(ShopOffer offer) => offer.Kind switch
    {
        ShopKind.Item => Gui.Clip(offer.Item!.Name.Replace("Potion of ", "").ToUpperInvariant(), 8),
        ShopKind.RemoveCard => "REMOVE A CARD",
        ShopKind.Gear => Gui.Clip(offer.Gear!.Name.ToUpperInvariant(), 11),
        _ => "UPGRADE A CARD"
    };

    private string Detail(ShopOffer? offer) => offer switch
    {
        null => "",
        { Kind: ShopKind.Gear } => $"{offer.Gear!.Name} ({offer.Gear.EffectiveRarity} {GearText.SlotName(offer.Gear.Def.Slot)}): {GearText.Stats(offer.Gear)}. {offer.Gear.Def.Text} For: {GearText.Users(_run, offer.Gear.Def)}.",
        { Kind: ShopKind.Card } => $"{offer.Card!.Name} for {offer.Hero!.Name}: {offer.Card.Text}",
        { Kind: ShopKind.Item } => $"{offer.Item!.Name}: {offer.Item.Text} Kept in the bag.",
        { Kind: ShopKind.RemoveCard } => "Take one card out of a hero's deck for good. Decks never reshuffle, so a thin deck draws its best cards more often.",
        _ => "Upgrade one card for good, like resting at a campfire would."
    };

    private static void DrawCardOffer(SpriteBatch b, PixelCanvas c, SpriteSheet s, ShopOffer offer, Rectangle r, bool hover, bool afford, int index)
    {
        var card = offer.Card!;
        var (fill, accent) = card.Speed switch
        {
            CardSpeed.Quick => (Palette.DarkGreen, Palette.Green),
            CardSpeed.Reaction => (Palette.DarkPurple, Palette.Pink),
            _ => (Palette.DarkBlue, Palette.Blue)
        };
        var rr = hover && !offer.Sold ? new Rectangle(r.X, r.Y - 2, r.Width, r.Height) : r;
        Gui.Panel(b, c, rr, offer.Sold ? Palette.Black : fill, offer.Sold ? Palette.DarkGrey : hover ? Palette.Yellow : accent);
        TinyFont.Draw(b, c, (index + 1).ToString(), rr.X + 2, rr.Y + 2, Palette.LightGrey);
        string sprite = s.Has("hero." + offer.Hero!.Class.Id) ? "hero." + offer.Hero.Class.Id : "hero";
        s.Draw(b, sprite, rr.Right - 10, rr.Y + 2);
        s.Draw(b, CardIcons.For(card), rr.Center.X - SpriteSheet.Size / 2, rr.Y + 2, offer.Sold ? Palette.DarkGrey : null);

        int y = rr.Y + 12;
        foreach (string line in Gui.Wrap(card.Name.ToUpperInvariant(), 14).Take(2))
        {
            Gui.TextCenter(b, c, line, rr.Center.X, y, offer.Sold ? Palette.DarkGrey : Palette.Yellow);
            y += 7;
        }
        Gui.TextCenter(b, c, card.Rarity.ToString().ToUpperInvariant(), rr.Center.X, rr.Bottom - 16, Palette.LightGrey);
        string price = offer.Sold ? "SOLD" : $"{offer.Price} GOLD";
        Gui.TextCenter(b, c, price, rr.Center.X, rr.Bottom - 8, offer.Sold ? Palette.DarkGrey : afford ? Palette.Yellow : Palette.Red);
    }
}
