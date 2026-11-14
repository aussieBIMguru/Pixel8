using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Pixel8.Core.Cards;
using Pixel8.Core.Content;
using Pixel8.Core.Heroes;
using Pixel8.Core.Runs;
using Pixel8.Desktop.Fx;
using Pixel8.Desktop.Rendering;
using Pixel8.Desktop.Ui;

namespace Pixel8.Desktop.Screens;

/// <summary>
/// Party level-up (session 6): for every level gained, each living hero picks one reward in turn:
/// 1 +HP (max and current), 2 upgrade one of their cards, 3 remove one of their cards for good.
/// </summary>
public sealed class LevelUpScreen : IScreen
{
    private static readonly Rectangle[] Buttons =
    {
        new(28, 92, 64, 13), new(96, 92, 64, 13), new(164, 92, 64, 13)
    };
    private static readonly Rectangle BackButton = new(88, 133, 80, 10);

    private readonly Run _run;
    private readonly Action _onDone;
    private DeckPicker? _picker;
    private LevelUpChoice _pickingFor;
    private Point _pointer;
    private float _time;

    public LevelUpScreen(Run run, Action onDone)
    {
        _run = run;
        _onDone = onDone;
        Sfx.Play("levelup");
    }

    public Point ShakeOffset => Point.Zero;

    private Hero? Current => _run.PendingLevelUps.Count > 0 ? _run.PendingLevelUps[0] : null;

    public void Update(float dt, Input input)
    {
        _time += dt;
        _pointer = input.Pointer;
        var hero = Current;
        if (hero is null)
        {
            _onDone();
            return;
        }

        if (_picker is not null)
        {
            if (input.Pressed(Keys.Escape) || input.RightClick || input.Clicked(BackButton))
            {
                _picker = null;
                Sfx.Play("back");
                return;
            }
            if (_picker.Update(input) is { } pick)
            {
                _run.ApplyLevelUp(hero, _pickingFor, pick.Card);
                _picker = null;
                Sfx.Play(_pickingFor == LevelUpChoice.UpgradeCard ? "perfect" : "confirm");
                if (Current is null) _onDone();
            }
            return;
        }

        int digit = input.PressedDigit();
        for (int i = 0; i < 3; i++)
        {
            if (digit != i && !input.Clicked(Buttons[i])) continue;
            var choice = (LevelUpChoice)i;
            if (!_run.CanLevelUp(hero, choice))
            {
                Sfx.Play("deny");
                return;
            }
            if (choice == LevelUpChoice.MaxHp)
            {
                _run.ApplyLevelUp(hero, choice);
                Sfx.Play("heal");
                if (Current is null) _onDone();
            }
            else
            {
                _pickingFor = choice;
                _picker = choice == LevelUpChoice.UpgradeCard
                    ? new DeckPicker(_run, c => c.CanUpgrade, $"{hero.Name.ToUpperInvariant()}: UPGRADE A CARD", showUpgrade: true, only: hero)
                    : new DeckPicker(_run, _ => true, $"{hero.Name.ToUpperInvariant()}: REMOVE A CARD", showUpgrade: false, only: hero);
                Sfx.Play("select");
            }
            return;
        }
    }

    public void Draw(SpriteBatch b, PixelCanvas c, SpriteSheet s)
    {
        var hero = Current;
        if (hero is null) return;
        if (_picker is not null)
        {
            _picker.Draw(b, c, s);
            Gui.Button(b, c, BackButton, "ESC  BACK", BackButton.Contains(_pointer));
            return;
        }

        var col = (int)(_time * 6) % 2 == 0 ? Palette.Yellow : Palette.Orange;
        Gui.TextCenter(b, c, $"LEVEL UP!  PARTY LEVEL {_run.Party.Level}", 128, 8, col, shadow: true);
        Gui.TextCenter(b, c, $"{_run.PendingLevelUps.Count} PICK{(_run.PendingLevelUps.Count == 1 ? "" : "S")} LEFT", 128, 18, Palette.LightGrey);

        string sprite = s.Has("hero." + hero.Class.Id) ? "hero." + hero.Class.Id : "hero";
        int bob = (int)(_time * 4) % 2;
        s.Draw(b, sprite, 124, 32 - bob);
        Gui.TextCenter(b, c, hero.Name.ToUpperInvariant(), 128, 44, Palette.White, shadow: true);
        Gui.HpBar(b, c, 108, 52, 40, hero.Hp, hero.MaxHp);
        Gui.TextCenter(b, c, $"{hero.Hp}/{hero.MaxHp} HP   DECK {hero.Deck.AllCards.Count()}", 128, 58, Palette.LightGrey);
        Gui.TextCenter(b, c, "CHOOSE ONE REWARD:", 128, 78, Palette.Peach);

        string[] labels = { $"1  +{_run.Content.Balance.LevelUpHp} MAX HP", "2  UPGRADE", "3  REMOVE" };
        string[] notes = { "AND HEAL IT", "ONE OF THEIR CARDS", _run.CanRemoveFrom(hero) ? "THIN THEIR DECK" : "DECK TOO SMALL" };
        for (int i = 0; i < 3; i++)
        {
            bool ok = _run.CanLevelUp(hero, (LevelUpChoice)i);
            Gui.Button(b, c, Buttons[i], labels[i], Buttons[i].Contains(_pointer), ok, i == 0 ? Palette.Green : i == 1 ? Palette.Blue : Palette.Pink);
            Gui.TextCenter(b, c, notes[i], Buttons[i].Center.X, Buttons[i].Bottom + 3, ok ? Palette.LightGrey : Palette.DarkGrey);
        }
    }
}

/// <summary>
/// A map event ("?" tile): title, text and up to 3 options (click or 1–3). Options can cost gold/Torch and roll a
/// d20 check. A "remove a card" outcome opens the deck picker (Esc skips it). Then click to continue.
/// </summary>
public sealed class EventScreen : IScreen
{
    private const int OptionX = 24, OptionY = 66, OptionW = 208, OptionH = 11;

    private readonly Run _run;
    private readonly EventDef _event;
    private readonly Action<string, FightRequest?> _onDone;
    private EventResult? _result;
    private DeckPicker? _picker;
    /// <summary>Round 10: gear from a "gear" outcome, picked after the result is read.</summary>
    private LootScreen? _loot;
    private string _note = "";
    private Point _pointer;
    private float _time;

    /// <param name="onDone">Called with the closing message and, when an option started a fight, the fight to begin.</param>
    public EventScreen(Run run, EventDef ev, Action<string, FightRequest?> onDone)
    {
        _run = run;
        _event = ev;
        _onDone = onDone;
        Sfx.Play("select");
    }

    public Point ShakeOffset => Point.Zero;

    private static Rectangle OptionRect(int i) => new(OptionX, OptionY + i * (OptionH + 6), OptionW, OptionH);

    public void Update(float dt, Input input)
    {
        _time += dt;
        _pointer = input.Pointer;

        if (_loot is not null)
        {
            _loot.Update(dt, input);
            return;
        }

        if (_picker is not null)
        {
            if (input.Pressed(Keys.Escape) || input.RightClick)
            {
                _picker = null;
                _result!.Lines.Add("You keep your cards.");
                return;
            }
            if (_picker.Update(input) is { } pick)
            {
                if (_run.RemoveCardFree(pick.Hero, pick.Card))
                {
                    _result!.Lines.Add($"{pick.Hero.Name} gives up {pick.Card.DisplayName}.");
                    Sfx.Play("confirm");
                    _picker = null;
                }
                else Sfx.Play("deny");
            }
            return;
        }

        if (_result is not null)
        {
            if (_time > 0.3f && (input.LeftClick || input.PressedAny(Keys.Space, Keys.Enter, Keys.Escape)))
            {
                string message = $"{_event.Title}: {(_result.Lines.Count > 0 ? _result.Lines[^1] : "done.")}".ToUpperInvariant();
                if (_result.Loot is { Choices.Count: > 0 } offer)
                {
                    var fight = _result.Fight;
                    _loot = new LootScreen(_run, offer, lootMessage => _onDone($"{message} {lootMessage}", fight));
                    return;
                }
                _onDone(message, _result.Fight);
            }
            return;
        }

        int digit = input.PressedDigit();
        for (int i = 0; i < Math.Min(3, _event.Options.Count); i++)
        {
            if (digit != i && !input.Clicked(OptionRect(i))) continue;
            var option = _event.Options[i];
            if (!_run.CanChoose(option, out string reason))
            {
                _note = reason.ToUpperInvariant();
                Sfx.Play("deny");
                return;
            }
            _result = _run.ResolveEvent(option);
            _time = 0;
            Sfx.Play(_result.Roll is null ? "confirm" : _result.Success ? "good" : "hurt");
            if (_result.Lines.Any(l => l.StartsWith("Found:", StringComparison.Ordinal))) Sfx.Play("item");
            else if (_result.Lines.Any(l => l.Contains(" gold.", StringComparison.Ordinal) && l.StartsWith('+'))) Sfx.Play("coins");
            if (_result.RemoveCard)
                _picker = new DeckPicker(_run, card => _run.Party.Heroes.Any(h => h.Deck.AllCards.Contains(card) && _run.CanRemoveFrom(h)),
                    "GIVE UP ONE CARD (ESC TO REFUSE)", showUpgrade: false);
            return;
        }
    }

    public void Draw(SpriteBatch b, PixelCanvas c, SpriteSheet s)
    {
        if (_loot is not null)
        {
            _loot.Draw(b, c, s);
            return;
        }
        if (_picker is not null)
        {
            _picker.Draw(b, c, s);
            return;
        }

        s.Draw(b, "event", 124, 4);
        Gui.TextCenter(b, c, _event.Title.ToUpperInvariant(), 128, 15, Palette.Lavender, shadow: true);
        int y = 26;
        foreach (string line in Gui.Wrap(_event.Text.ToUpperInvariant(), 58).Take(4))
        {
            Gui.TextCenter(b, c, line, 128, y, Palette.White);
            y += 7;
        }
        Gui.TextRight(b, c, $"GOLD {_run.Party.Gold}  TORCH {_run.Party.Torch}  LUCK {_run.Party.Luck}", 252, 56, Palette.Orange);

        if (_result is null)
        {
            for (int i = 0; i < Math.Min(3, _event.Options.Count); i++)
            {
                var option = _event.Options[i];
                var r = OptionRect(i);
                bool ok = _run.CanChoose(option, out _);
                Gui.Button(b, c, r, $"{i + 1}  {option.Label.ToUpperInvariant()}", r.Contains(_pointer), ok, Palette.Lavender);
                if (option.Text.Length > 0)
                    Gui.TextCenter(b, c, Gui.Clip(option.Text.ToUpperInvariant(), 52), r.Center.X, r.Bottom + 1, ok ? Palette.LightGrey : Palette.DarkGrey);
            }
            if (_note.Length > 0) Gui.TextCenter(b, c, _note, 128, 120, Palette.Red);
            return;
        }

        y = 70;
        if (_result.Lines.Count == 0) Gui.TextCenter(b, c, "YOU MOVE ON.", 128, y, Palette.LightGrey);
        foreach (string raw in _result.Lines)
            foreach (string line in Gui.Wrap(raw.ToUpperInvariant(), 58))
            {
                if (y > 122) break;
                Gui.TextCenter(b, c, line, 128, y, _result.Success ? Palette.Green : Palette.Peach);
                y += 8;
            }
        if ((int)(_time * 2) % 2 == 0)
            Gui.TextCenter(b, c, _result.Fight is null ? "CLICK TO CONTINUE" : "CLICK TO FIGHT", 128, 132,
                _result.Fight is null ? Palette.DarkGrey : Palette.Red);
    }
}
