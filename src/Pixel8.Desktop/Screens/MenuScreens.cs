using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Pixel8.Core.Content;
using Pixel8.Core.Runs;
using Pixel8.Desktop.Fx;
using Pixel8.Desktop.Rendering;
using Pixel8.Desktop.Ui;

namespace Pixel8.Desktop.Screens;

/// <summary>
/// Title screen: the big PIXEL8 logo and a menu. Start game, Difficulty (a ←/→ carousel), Shop (on map /
/// at campfires / anytime carousel), Sound on/off, React events (timed reactions) on/off, About, Quit.
/// Options are saved to <see cref="Settings"/>.
/// Keys: ↑/↓ select · ←/→ change · Enter/Space choose · Esc quit (or close About).
/// </summary>
public sealed class TitleScreen : IScreen
{
    private const int MenuX = 68, MenuY = 49, MenuW = 120, MenuH = 9, MenuGap = 10;
    private enum Item { Continue, Start, Difficulty, Shop, Sound, React, About, Quit }
    private static readonly string[] ShopModes = { "OnMap", "AtCampfires", "Anytime" };
    /// <summary>The menu; Continue only appears when there's a saved run.</summary>
    private readonly Item[] Items;

    /// <summary>"V0.1.0", from Directory.Build.props via the assembly version.</summary>
    private static readonly string VersionText =
        typeof(TitleScreen).Assembly.GetName().Version is { } v ? $"V{v.Major}.{v.Minor}.{v.Build}" : "";

    private static readonly string[] AboutLines =
    {
        "PIXEL8 - A PIXEL-ART DUNGEON DECKBUILDER.",
        "LEAD A PARTY OF 3 HEROES THROUGH 3 DEPTHS.",
        "EVERY ROLL IS A D20 WITH THE ODDS SHOWN.",
        "",
        "DESIGN + DEVELOPMENT: GAVIN NICHOLLS",
        "DEV ASSISTANCE: CLAUDE (ANTHROPIC)",
        "ENGINE: MONOGAME (MS-PL)",
        "MONSTERS + RULES: 5E SRD 5.1 (CC BY 4.0)",
        "PALETTE: PICO-8 COLOURS (LEXALOFFLE)",
        "INSPIRED BY MUNCHKIN, SLAY THE SPIRE,",
        "D+D 5E AND UNDERTALE. NOT AFFILIATED.",
        "",
        "CREDITS + LICENCES: THIRD-PARTY-NOTICES.MD",
        "ISSUES OR IP CONCERNS? OPEN A GITHUB ISSUE.",
    };

    private readonly ContentDb _content;
    private readonly Settings _settings;
    private readonly Action _onStart;
    private readonly Action _onQuit;
    private readonly Action? _onContinue;
    private readonly RunSave? _save;
    private readonly List<string> _difficulties;
    private int _cursor;
    private bool _about;
    private float _time;
    private Point _pointer;

    public TitleScreen(ContentDb content, Settings settings, Action onStart, Action onQuit,
        RunSave? save = null, Action? onContinue = null)
    {
        _content = content;
        _settings = settings;
        _onStart = onStart;
        _onQuit = onQuit;
        _save = save;
        _onContinue = onContinue;
        Items = Enum.GetValues<Item>().Where(i => i != Item.Continue || onContinue is not null).ToArray();
        _difficulties = content.Balance.Difficulties.Keys.ToList();
        if (_difficulties.Count == 0) _difficulties.Add(content.Balance.DefaultDifficulty);
        if (!_difficulties.Contains(settings.Difficulty)) settings.Difficulty = _difficulties.Contains(content.Balance.DefaultDifficulty)
            ? content.Balance.DefaultDifficulty : _difficulties[0];
    }

    public Point ShakeOffset => Point.Zero;

    private static Rectangle ItemRect(int i) => new(MenuX, MenuY + i * MenuGap, MenuW, MenuH);
    private static Rectangle LeftArrow(int i) => new(MenuX, MenuY + i * MenuGap, 12, MenuH);
    private static Rectangle RightArrow(int i) => new(MenuX + MenuW - 12, MenuY + i * MenuGap, 12, MenuH);
    private static readonly Rectangle AboutBox = new(14, 8, 228, 128);

    public void Update(float dt, Input input)
    {
        _time += dt;
        _pointer = input.Pointer;

        if (_about)
        {
            if (_time > 0.2f && (input.LeftClick || input.PressedAny(Keys.Escape, Keys.Enter, Keys.Space)))
            {
                _about = false;
                Sfx.Play("select");
            }
            return;
        }

        if (input.PressedAny(Keys.Up, Keys.W)) Move(-1);
        if (input.PressedAny(Keys.Down, Keys.S)) Move(1);
        for (int i = 0; i < Items.Length; i++)
            if (input.Over(ItemRect(i)) && _cursor != i && input.Pointer != _lastPointer) _cursor = i;
        _lastPointer = input.Pointer;

        var item = Items[_cursor];
        if (input.PressedAny(Keys.Left, Keys.A)) Change(item, -1);
        if (input.PressedAny(Keys.Right, Keys.D)) Change(item, +1);

        if (input.LeftClick)
        {
            for (int i = 0; i < Items.Length; i++)
            {
                if (!input.Over(ItemRect(i))) continue;
                _cursor = i;
                if (Items[i] is Item.Difficulty or Item.Shop && input.Over(LeftArrow(i))) Change(Items[i], -1);
                else if (Items[i] is Item.Difficulty or Item.Shop or Item.Sound or Item.React) Change(Items[i], +1);
                else Activate(Items[i]);
                return;
            }
        }

        if (input.PressedAny(Keys.Enter, Keys.Space))
        {
            if (item is Item.Difficulty or Item.Shop or Item.Sound or Item.React) Change(item, +1);
            else Activate(item);
        }
        else if (input.Pressed(Keys.Escape))
        {
            _onQuit();
        }
    }

    private Point _lastPointer;

    private void Move(int delta)
    {
        _cursor = (_cursor + delta + Items.Length) % Items.Length;
        Sfx.Play("select");
    }

    private void Change(Item item, int delta)
    {
        switch (item)
        {
            case Item.Difficulty:
            {
                int i = Math.Max(0, _difficulties.IndexOf(_settings.Difficulty));
                _settings.Difficulty = _difficulties[(i + delta + _difficulties.Count) % _difficulties.Count];
                break;
            }
            case Item.Shop:
            {
                int i = Math.Max(0, Array.IndexOf(ShopModes, _settings.Shop));
                _settings.Shop = ShopModes[(i + delta + ShopModes.Length) % ShopModes.Length];
                break;
            }
            case Item.Sound:
                _settings.Sound = !_settings.Sound;
                Sfx.Enabled = _settings.Sound;
                break;
            case Item.React:
                _settings.ReactEvents = !_settings.ReactEvents;
                break;
            default:
                return;
        }
        _settings.Save();
        Sfx.Play("select");
    }

    private void Activate(Item item)
    {
        switch (item)
        {
            case Item.Continue:
                Sfx.Play("confirm");
                _onContinue?.Invoke();
                break;
            case Item.Start:
                Sfx.Play("confirm");
                _onStart();
                break;
            case Item.About:
                Sfx.Play("select");
                _about = true;
                _time = 0;
                break;
            case Item.Quit:
                _onQuit();
                break;
        }
    }

    private string Label(Item item) => item switch
    {
        Item.Continue => _save is null ? "CONTINUE" : $"CONTINUE  D{_save.Depth} LV{_save.Level}",
        Item.Start => _save is null ? "START GAME" : "NEW GAME",
        Item.Difficulty => _content.Balance.DifficultyOf(_settings.Difficulty).Name.ToUpperInvariant(),
        Item.Shop => "SHOP: " + (_settings.Shop switch
        {
            "AtCampfires" => "CAMPFIRES",
            "Anytime" => "ANYTIME",
            _ => "ON MAP"
        }),
        Item.Sound => "SOUND: " + (_settings.Sound ? "ON" : "OFF"),
        Item.React => "REACT EVENTS: " + (_settings.ReactEvents ? "ON" : "OFF"),
        Item.About => "ABOUT",
        _ => "QUIT"
    };

    public void Draw(SpriteBatch b, PixelCanvas c, SpriteSheet s)
    {
        DrawLogo(b, c);
        Gui.TextCenter(b, c, "A PIXEL DUNGEON DECKBUILDER", 128, 41, Palette.LightGrey);
        Gui.TextRight(b, c, VersionText, 253, 2, Palette.DarkGrey);

        for (int i = 0; i < Items.Length; i++)
        {
            var item = Items[i];
            var r = ItemRect(i);
            bool selected = i == _cursor && !_about;
            Gui.Button(b, c, r, Label(item), selected, true, item is Item.Start or Item.Continue ? Palette.Green : null);
            if (item is Item.Difficulty or Item.Shop)
            {
                var arrow = selected ? Palette.Yellow : Palette.DarkGrey;
                TinyFont.Draw(b, c, "<", r.X + 4, r.Y + 2, arrow);
                TinyFont.Draw(b, c, ">", r.Right - 7, r.Y + 2, arrow);
            }
            if (selected && (int)(_time * 3) % 2 == 0)
                TinyFont.Draw(b, c, ">", r.X - 7, r.Y + 2, Palette.Yellow);
        }

        string hint = Items[_cursor] switch
        {
            Item.Difficulty => _content.Balance.DifficultyOf(_settings.Difficulty).Description,
            Item.React => "TIME A KEYPRESS TO DODGE BIG ATTACKS",
            Item.Shop => _settings.Shop switch
            {
                "AtCampfires" => "EVERY CAMPFIRE HAS A MERCHANT",
                "Anytime" => "PRESS M ON THE MAP TO SHOP",
                _ => "ONE MERCHANT HIDDEN ON EACH DEPTH"
            },
            Item.Sound => "CHIPTUNE BLIPS AND BLOOPS",
            Item.Continue => "PICK UP WHERE YOU LEFT OFF",
            Item.Start when _save is not null => "STARTING A NEW RUN REPLACES YOUR SAVE",
            _ => ""
        };
        Gui.TextCenter(b, c, Gui.Clip(hint.ToUpperInvariant(), 62), 128, MenuY + Items.Length * MenuGap + 1, Palette.Lavender);
        Gui.TextCenter(b, c, "UP/DOWN SELECT  </> CHANGE  ENTER CHOOSE", 128, 137, Palette.DarkGrey);

        if (_about) DrawAbout(b, c);
    }

    private void DrawLogo(SpriteBatch b, PixelCanvas c)
    {
        const string title = "PIXEL8";
        const int scale = 5;
        int x = 128 - TinyFont.MeasureScaled(title, scale) / 2;
        int y = 7 + ((int)(_time * 2) % 2);   // a one-pixel bob
        for (int i = 0; i < title.Length; i++)
        {
            string ch = title[i].ToString();
            int cx = x + i * TinyFont.Advance * scale;
            TinyFont.DrawScaled(b, c, ch, cx + 2, y + 2, scale, Palette.DarkPurple);
            TinyFont.DrawScaled(b, c, ch, cx, y, scale, i == title.Length - 1 ? Palette.Yellow : Palette.Peach);
        }
    }

    private static void DrawAbout(SpriteBatch b, PixelCanvas c)
    {
        Gui.Panel(b, c, AboutBox, Palette.Black, Palette.Yellow);
        Gui.TextCenter(b, c, "ABOUT", AboutBox.Center.X, AboutBox.Y + 4, Palette.Yellow, shadow: true);
        int y = AboutBox.Y + 14;
        foreach (string line in AboutLines)
        {
            TinyFont.Draw(b, c, line, AboutBox.X + 6, y, line.StartsWith("ISSUES") ? Palette.Peach : Palette.White);
            y += 7;
        }
        Gui.TextCenter(b, c, "PRESS ENTER TO CLOSE", AboutBox.Center.X, AboutBox.Bottom - 9, Palette.DarkGrey);
    }
}

/// <summary>
/// Pick 3 of the classes in heroes.json (click or 1–4), then Start (Enter). Esc goes back to the title.
/// Difficulty and react events were chosen on the title screen and are shown here.
/// </summary>
public sealed class PartySelectScreen : IScreen
{
    private const int PartySize = 3;
    private const int BoxW = 54, BoxH = 70, BoxY = 30;

    private readonly ContentDb _content;
    private readonly Settings _settings;
    private readonly Action<IReadOnlyList<string>> _onStart;
    private readonly Action _onBack;
    private readonly List<HeroClassDef> _classes;
    private readonly List<string> _chosen = new();
    private Point _pointer;
    private float _time;

    public PartySelectScreen(ContentDb content, Settings settings, Action<IReadOnlyList<string>> onStart, Action onBack)
    {
        _content = content;
        _settings = settings;
        _onStart = onStart;
        _onBack = onBack;
        _classes = content.Heroes.Values.Take(4).ToList();
        var previous = settings.LastParty?.Where(id => _classes.Any(c => c.Id == id)).Distinct().ToList();
        _chosen.AddRange(previous is { Count: PartySize } ? previous : _classes.Take(PartySize).Select(h => h.Id));
    }

    public Point ShakeOffset => Point.Zero;

    private Rectangle BoxRect(int i)
    {
        int total = _classes.Count * BoxW + (_classes.Count - 1) * 6;
        return new Rectangle((PixelCanvas.Width - total) / 2 + i * (BoxW + 6), BoxY, BoxW, BoxH);
    }

    private static readonly Rectangle StartButton = new(88, 120, 80, 11);

    public void Update(float dt, Input input)
    {
        _time += dt;
        _pointer = input.Pointer;

        if (input.Pressed(Keys.Escape))
        {
            _onBack();
            return;
        }

        int digit = input.PressedDigit();
        for (int i = 0; i < _classes.Count; i++)
        {
            if (digit != i && !input.Clicked(BoxRect(i))) continue;
            string id = _classes[i].Id;
            if (_chosen.Contains(id)) _chosen.Remove(id);
            else if (_chosen.Count < PartySize) _chosen.Add(id);
            Sfx.Play("select");
        }

        bool ready = _chosen.Count == PartySize;
        if (ready && (input.Clicked(StartButton) || input.Pressed(Keys.Enter)))
        {
            var party = _classes.Where(c => _chosen.Contains(c.Id)).Select(c => c.Id).ToList();
            _settings.LastParty = party;
            _settings.Save();
            Sfx.Play("confirm");
            _onStart(party);
        }
    }

    public void Draw(SpriteBatch b, PixelCanvas c, SpriteSheet s)
    {
        Gui.TextCenter(b, c, "P I X E L 8", 128, 6, Palette.Yellow, shadow: true);
        Gui.TextCenter(b, c, $"CHOOSE {PartySize} HEROES", 128, 16, Palette.Peach);

        for (int i = 0; i < _classes.Count; i++)
        {
            var cls = _classes[i];
            var r = BoxRect(i);
            bool chosen = _chosen.Contains(cls.Id);
            bool hover = r.Contains(_pointer);
            Gui.Panel(b, c, r, chosen ? Palette.DarkBlue : Palette.Black,
                chosen ? Palette.Yellow : hover ? Palette.White : Palette.DarkGrey);

            int bob = chosen && (int)(_time * 4) % 2 == 0 ? 1 : 0;
            string sprite = s.Has("hero." + cls.Id) ? "hero." + cls.Id : "hero";
            s.Draw(b, sprite, r.Center.X - 4, r.Y + 5 - bob);
            TinyFont.Draw(b, c, (i + 1).ToString(), r.X + 3, r.Y + 3, Palette.LightGrey);

            int y = r.Y + 17;
            Gui.TextCenter(b, c, cls.Name.ToUpperInvariant(), r.Center.X, y, chosen ? Palette.Yellow : Palette.White);
            y += 9;
            TinyFont.Draw(b, c, $"HP {cls.Hp}", r.X + 4, y, Palette.Green);
            TinyFont.Draw(b, c, $"AC {cls.Ac}", r.X + 30, y, Palette.Blue);
            y += 7;
            TinyFont.Draw(b, c, $"ATK +{cls.AttackMod}", r.X + 4, y, Palette.Orange);
            y += 9;

            var cards = (cls.StartingDeck ?? Array.Empty<string>())
                .Distinct()
                .Select(id => _content.Cards.TryGetValue(id, out var cd) ? cd.Name.ToUpperInvariant() : id)
                .Take(3);
            foreach (string name in cards)
            {
                TinyFont.Draw(b, c, Gui.Clip(name, 12), r.X + 4, y, Palette.LightGrey);
                y += 7;
            }
        }

        var difficulty = _content.Balance.DifficultyOf(_settings.Difficulty);
        Gui.TextCenter(b, c, $"{difficulty.Name.ToUpperInvariant()}   REACT EVENTS {(_settings.ReactEvents ? "ON" : "OFF")}", 128, 107, Palette.Lavender);

        bool ready = _chosen.Count == PartySize;
        Gui.Button(b, c, StartButton, "ENTER  START", ready && StartButton.Contains(_pointer), ready, Palette.Green);
        Gui.TextCenter(b, c, "1-4 HEROES   ENTER START   ESC BACK", 128, 137, Palette.DarkGrey);
    }
}

/// <summary>A title, a few lines and "click to continue" (defeat, victory).</summary>
public sealed class MessageScreen : IScreen
{
    private readonly string _title;
    private readonly IReadOnlyList<string> _lines;
    private readonly Color _color;
    private readonly Action _onContinue;
    private float _time;

    public MessageScreen(string title, IReadOnlyList<string> lines, Color color, Action onContinue)
    {
        _title = title;
        _lines = lines;
        _color = color;
        _onContinue = onContinue;
    }

    public Point ShakeOffset => Point.Zero;

    public void Update(float dt, Input input)
    {
        _time += dt;
        if (_time > 0.5f && (input.LeftClick || input.PressedAny(Keys.Space, Keys.Enter)))
            _onContinue();
    }

    public void Draw(SpriteBatch b, PixelCanvas c, SpriteSheet s)
    {
        Gui.TextCenter(b, c, _title, 128, 44, _color, shadow: true);
        int y = 60;
        foreach (string line in _lines)
        {
            Gui.TextCenter(b, c, line.ToUpperInvariant(), 128, y, Palette.White);
            y += 8;
        }
        if ((int)(_time * 2) % 2 == 0)
            Gui.TextCenter(b, c, "CLICK TO CONTINUE", 128, 120, Palette.DarkGrey);
    }
}
