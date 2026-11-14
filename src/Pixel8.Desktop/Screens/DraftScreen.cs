using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Pixel8.Core.Cards;
using Pixel8.Core.Rewards;
using Pixel8.Core.Runs;
using Pixel8.Desktop.Fx;
using Pixel8.Desktop.Rendering;
using Pixel8.Desktop.Ui;

namespace Pixel8.Desktop.Screens;

/// <summary>Post-fight reward: one card per living hero. Pick one (click or 1–3) or skip for gold (S).</summary>
public sealed class DraftScreen : IScreen
{
    private const int CardW = 76, CardH = 82, Gap = 8, CardY = 22, TextChars = 18;

    private readonly Run _run;
    private readonly CardDraft _draft;
    private readonly Action _onDone;
    private string _message = "";
    private float _doneTimer = -1;
    private float _time;
    private Point _pointer;

    public DraftScreen(Run run, CardDraft draft, Action onDone)
    {
        _run = run;
        _draft = draft;
        _onDone = onDone;
    }

    public Point ShakeOffset => Point.Zero;

    private Rectangle CardRect(int i)
    {
        int n = _draft.Offers.Count;
        int total = n * CardW + (n - 1) * Gap;
        int x0 = (PixelCanvas.Width - total) / 2;
        return new Rectangle(x0 + i * (CardW + Gap), CardY, CardW, CardH);
    }

    private static readonly Rectangle SkipButton = new(78, 128, 100, 11);

    public void Update(float dt, Input input)
    {
        _time += dt;
        _pointer = input.Pointer;

        if (_doneTimer >= 0)
        {
            _doneTimer -= dt;
            if (_doneTimer < 0 || input.LeftClick || input.PressedAny(Keys.Space, Keys.Enter)) _onDone();
            return;
        }

        int digit = input.PressedDigit();
        for (int i = 0; i < _draft.Offers.Count; i++)
        {
            if (digit == i || input.Clicked(CardRect(i)))
            {
                var offer = _draft.Offers[i];
                _draft.Choose(offer, _run.LootRng);
                _message = $"{offer.Card.Name} joins {offer.Hero.Name}'s deck".ToUpperInvariant();
                Sfx.Play("confirm");
                _doneTimer = 1.2f;
                return;
            }
        }

        if (input.Clicked(SkipButton) || input.Pressed(Keys.S))
        {
            int gold = _draft.Skip(_run.Party, _run.Content.Balance);
            _message = $"SKIPPED: +{gold} GOLD";
            Sfx.Play("coins");
            _doneTimer = 1.0f;
        }
    }

    public void Draw(SpriteBatch b, PixelCanvas c, SpriteSheet s)
    {
        Gui.TextCenter(b, c, "SPOILS: CHOOSE A CARD", 128, 5, Palette.Yellow, shadow: true);
        Gui.TextCenter(b, c, $"GOLD {_run.Party.Gold}", 128, 13, Palette.Orange);

        for (int i = 0; i < _draft.Offers.Count; i++)
        {
            var offer = _draft.Offers[i];
            var r = CardRect(i);
            bool hover = r.Contains(_pointer) && _doneTimer < 0;
            DrawCard(b, c, s, offer.Card, r, hover, i);

            s.Draw(b, s.Has("hero." + offer.Hero.Class.Id) ? "hero." + offer.Hero.Class.Id : "hero", r.X + 2, r.Bottom + 4);
            TinyFont.Draw(b, c, Gui.Clip("FOR " + offer.Hero.Name.ToUpperInvariant(), 15), r.X + 12, r.Bottom + 6, Palette.LightGrey);
        }

        if (_draft.Offers.Count == 0)
            Gui.TextCenter(b, c, "NO CARDS ON OFFER", 128, 60, Palette.DarkGrey);

        if (_message.Length > 0)
            Gui.TextCenter(b, c, _message, 128, SkipButton.Y + 2, Palette.Green, shadow: true);
        else
            Gui.Button(b, c, SkipButton, $"S  SKIP: +{_run.Content.Balance.SkipDraftGold} GOLD", SkipButton.Contains(_pointer));
    }

    private static void DrawCard(SpriteBatch b, PixelCanvas c, SpriteSheet s, CardDef card, Rectangle r, bool hover, int index)
    {
        var (fill, accent) = card.Speed switch
        {
            CardSpeed.Quick => (Palette.DarkGreen, Palette.Green),
            CardSpeed.Reaction => (Palette.DarkPurple, Palette.Pink),
            _ => (Palette.DarkBlue, Palette.Blue)
        };
        var rr = hover ? new Rectangle(r.X, r.Y - 2, r.Width, r.Height) : r;
        Gui.Panel(b, c, rr, fill, hover ? Palette.Yellow : accent);

        TinyFont.Draw(b, c, (index + 1).ToString(), rr.X + 3, rr.Y + 3, Palette.LightGrey);
        s.Draw(b, CardIcons.For(card), rr.X + 9, rr.Y + 2);
        var rarity = card.Rarity switch
        {
            Rarity.Uncommon => Palette.Blue,
            Rarity.Rare => Palette.Yellow,
            Rarity.Legendary => Palette.Orange,
            _ => Palette.LightGrey
        };
        Gui.TextRight(b, c, card.Rarity.ToString().ToUpperInvariant(), rr.Right - 3, rr.Y + 3, rarity);

        int y = rr.Y + 12;
        foreach (string line in Gui.Wrap(card.Name.ToUpperInvariant(), TextChars).Take(2))
        {
            Gui.TextCenter(b, c, line, rr.Center.X, y, Palette.Yellow, shadow: true);
            y += 7;
        }
        string kind = $"{card.Speed} {(card.Check == CheckKind.None ? "" : card.Check.ToString())}".Trim().ToUpperInvariant();
        Gui.TextCenter(b, c, kind, rr.Center.X, y, accent);
        y += 10;

        foreach (string line in Gui.Wrap(card.Text.ToUpperInvariant(), TextChars).Take(7))
        {
            TinyFont.Draw(b, c, line, rr.X + 3, y, Palette.White);
            y += 7;
        }
    }
}
