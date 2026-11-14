using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Pixel8.Core.Achievements;
using Pixel8.Core.Content;
using Pixel8.Core.Runs;
using Pixel8.Desktop.Fx;
using Pixel8.Desktop.Rendering;
using Pixel8.Desktop.Ui;

namespace Pixel8.Desktop.Screens;

/// <summary>
/// Title screen: the big PIXEL8 logo and a menu: Continue, Start game, Options, About, Quit.
/// Options (a sub-menu since round 9): Difficulty (a ←/→ carousel), Shop (on map / at campfires / anytime), Music and
/// SFX volume sliders (0–10), React events (timed reactions) on/off, Back. The secret DEBUG MODE toggle lives below
/// Back: press Down on Back 20 times to reveal it. Options are saved to <see cref="Settings"/>.
/// Keys: ↑/↓ select · ←/→ change · Enter/Space choose · Esc back (options), quit (main menu) or close About.
/// </summary>
public sealed class TitleScreen : IScreen
{
    private const int MenuX = 68, MenuW = 120, MenuH = 9;
    private enum Item { Continue, Start, Options, Achievements, Journal, About, Quit, Difficulty, Shop, Music, Sfx, React, Back, Debug }

    private static readonly Item[] MainItems = { Item.Continue, Item.Start, Item.Options, Item.Achievements, Item.Journal, Item.About, Item.Quit };
    private static readonly Item[] OptionItems = { Item.Difficulty, Item.Shop, Item.Music, Item.Sfx, Item.React, Item.Back, Item.Debug };

    /// <summary>Showing the Options sub-menu rather than the main menu.</summary>
    private bool _options;

    /// <summary>The achievements list, shown over the title while open (round 10).</summary>
    private AchievementsScreen? _achievements;

    /// <summary>The Adventurer's Journal, shown over the title while open (round 11).</summary>
    private JournalScreen? _journal;

    private int MenuY => _options ? 49 : 50;
    private static readonly string[] ShopModes = { "OnMap", "AtCampfires", "Anytime" };

    /// <summary>The current menu; Continue only appears with a saved run, Debug only once it's been unlocked this session.</summary>
    private Item[] Items => (_options ? OptionItems : MainItems)
        .Where(i => (i != Item.Continue || _onContinue is not null) && (i != Item.Debug || DebugMode.Unlocked))
        .ToArray();

    private const int MenuGap = 10;

    /// <summary>Down presses on the last Options item (Back), towards the debug unlock.</summary>
    private int _bottomPresses;
    /// <summary>The unlock's flash of light (1 → 0).</summary>
    private float _unlockFlash;

    /// <summary>"V0.1.0", from Directory.Build.props via the assembly version.</summary>
    private static readonly string VersionText =
        typeof(TitleScreen).Assembly.GetName().Version is { } v ? $"V{v.Major}.{v.Minor}.{v.Build}" : "";

    private static readonly string[] AboutLines =
    {
        "PIXEL8 - A PIXEL-ART DUNGEON DECKBUILDER.",
        "LEAD A PARTY OF 3 HEROES THROUGH 5 DEPTHS.",
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
        _difficulties = content.Balance.Difficulties.Keys.ToList();
        if (_difficulties.Count == 0) _difficulties.Add(content.Balance.DefaultDifficulty);
        if (!_difficulties.Contains(settings.Difficulty)) settings.Difficulty = _difficulties.Contains(content.Balance.DefaultDifficulty)
            ? content.Balance.DefaultDifficulty : _difficulties[0];
    }

    public Point ShakeOffset => Point.Zero;

    private Rectangle ItemRect(int i) => new(MenuX, MenuY + i * MenuGap, MenuW, MenuH);
    private Rectangle LeftArrow(int i) => new(MenuX, MenuY + i * MenuGap, 12, MenuH);
    private Rectangle RightArrow(int i) => new(MenuX + MenuW - 12, MenuY + i * MenuGap, 12, MenuH);
    private static bool IsToggle(Item item) => item is Item.Difficulty or Item.Shop or Item.Music or Item.Sfx or Item.React or Item.Debug;
    private static bool HasArrows(Item item) => item is Item.Difficulty or Item.Shop or Item.Music or Item.Sfx;
    private static readonly Rectangle AboutBox = new(14, 8, 228, 128);

    public void Update(float dt, Input input)
    {
        _time += dt;
        _unlockFlash = Math.Max(0, _unlockFlash - dt * 1.2f);
        _pointer = input.Pointer;
        if (_achievements is not null)
        {
            _achievements.Update(dt, input);
            return;
        }
        if (_journal is not null)
        {
            _journal.Update(dt, input);
            return;
        }

        if (_about)
        {
            if (_time > 0.2f && (input.LeftClick || input.PressedAny(Keys.Escape, Keys.Enter, Keys.Space)))
            {
                _about = false;
                Sfx.Play("back");
            }
            return;
        }
        _cursor = Math.Clamp(_cursor, 0, Items.Length - 1);

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
                if (Items[i] is Item.Music or Item.Sfx && SliderLevel(i, input.Pointer) is int level) SetVolume(Items[i], level);
                else if (HasArrows(Items[i]) && input.Over(LeftArrow(i))) Change(Items[i], -1);
                else if (IsToggle(Items[i])) Change(Items[i], +1);
                else Activate(Items[i]);
                return;
            }
        }

        if (input.PressedAny(Keys.Enter, Keys.Space))
        {
            if (IsToggle(item)) Change(item, +1);
            else Activate(item);
        }
        else if (input.Pressed(Keys.Escape))
        {
            if (_options) ShowMain();
            else _onQuit();
        }
    }

    private Point _lastPointer;

    /// <summary>No wraparound. In Options, pressing Down on the last item (Back) 20 times reveals the debug toggle (this session only).</summary>
    private void Move(int delta)
    {
        int last = Items.Length - 1;
        if (delta > 0 && _cursor >= last)
        {
            if (!_options) return;
            _bottomPresses++;
            if (!DebugMode.Unlocked && _bottomPresses >= DebugMode.UnlockPresses)
            {
                DebugMode.Unlocked = true;
                _unlockFlash = 1f;
                _cursor = Items.Length - 1;   // land on the new DEBUG MODE item
                Sfx.Play("perfect");
            }
            return;
        }
        if (delta < 0) _bottomPresses = 0;
        int next = Math.Clamp(_cursor + delta, 0, last);
        if (next == _cursor) return;
        _cursor = next;
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
            case Item.Music:
                // Arrows step 0..10; Right / Enter / click at 10 wraps round to OFF.
                _settings.MusicVolume = Step(_settings.MusicVolume, delta);
                _settings.ApplyAudio();
                break;
            case Item.Sfx:
                _settings.SfxVolume = Step(_settings.SfxVolume, delta);
                _settings.ApplyAudio();
                break;
            case Item.React:
                _settings.ReactEvents = !_settings.ReactEvents;
                break;
            case Item.Debug:
                // Session only: never written to settings.json.
                DebugMode.Enabled = !DebugMode.Enabled;
                Sfx.Play(DebugMode.Enabled ? "good" : "select");
                return;
            default:
                return;
        }
        _settings.Save();
        Sfx.Play("select");
    }

    /// <summary>
    /// The volume bars work as a slider: "MUSIC " / "SFX " is followed by up to ten 4px-wide bars, so a click on the
    /// bar area sets that level (left of the first bar = off). Null when the click wasn't on the bar area.
    /// </summary>
    private int? SliderLevel(int index, Point p)
    {
        var item = Items[index];
        var r = ItemRect(index);
        string prefix = item == Item.Music ? "MUSIC " : "SFX ";
        string full = prefix + new string('|', 10);
        int textX = r.Center.X - TinyFont.Measure(full) / 2;   // the label is centred in the button
        int barX = textX + TinyFont.Measure(prefix);
        if (p.X < barX - 4 || p.X > barX + 10 * TinyFont.Advance + 2) return null;
        return Math.Clamp((int)Math.Floor((p.X - barX) / (double)TinyFont.Advance) + 1, 0, 10);
    }

    private void SetVolume(Item item, int level)
    {
        if (item == Item.Music) _settings.MusicVolume = level;
        else _settings.SfxVolume = level;
        _settings.ApplyAudio();
        _settings.Save();
        Sfx.Play("select");
    }

    private static int Step(int volume, int delta) =>
        delta > 0 && volume >= 10 ? 0 : Math.Clamp(volume + delta, 0, 10);

    /// <summary>Ten slots: filled bars for the level, dots for the rest (fixed width, so clicks map to levels).</summary>
    private static string Volume(int v) => new string('|', v) + new string('.', 10 - v);

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
            case Item.Options:
                Sfx.Play("confirm");
                _options = true;
                _cursor = 0;
                _bottomPresses = 0;
                break;
            case Item.Back:
                ShowMain();
                break;
            case Item.Achievements:
                _achievements = new AchievementsScreen(() => _achievements = null);
                break;
            case Item.Journal:
                _journal = new JournalScreen(_content, () => _journal = null);
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

    /// <summary>Back from Options to the main menu, with the cursor on Options.</summary>
    private void ShowMain()
    {
        _options = false;
        _bottomPresses = 0;
        _cursor = Math.Max(0, Array.IndexOf(Items, Item.Options));
        Sfx.Play("back");
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
        Item.Music => "MUSIC " + Volume(_settings.MusicVolume),
        Item.Sfx => "SFX " + Volume(_settings.SfxVolume),
        Item.React => "REACT EVENTS: " + (_settings.ReactEvents ? "ON" : "OFF"),
        Item.Options => "OPTIONS",
        Item.Achievements => $"ACHIEVEMENTS {AchievementList.All.Count(a => AchievementStore.Tracker.State.Has(a.Id))}/{AchievementList.All.Count}",
        Item.Journal => "ADVENTURER'S JOURNAL",
        Item.Back => "BACK",
        Item.About => "ABOUT",
        Item.Debug => "DEBUG MODE: " + (DebugMode.Enabled ? "ON" : "OFF"),
        _ => "QUIT"
    };

    public void Draw(SpriteBatch b, PixelCanvas c, SpriteSheet s)
    {
        if (_achievements is not null)
        {
            _achievements.Draw(b, c, s);
            return;
        }
        if (_journal is not null)
        {
            _journal.Draw(b, c, s);
            return;
        }
        DrawLogo(b, c);
        if (_options) Gui.TextCenter(b, c, "OPTIONS", 128, 41, Palette.Yellow, shadow: true);
        else Gui.TextCenter(b, c, "A PIXEL DUNGEON DECKBUILDER", 128, 41, Palette.LightGrey);
        Gui.TextRight(b, c, VersionText, 253, 2, Palette.DarkGrey);

        for (int i = 0; i < Items.Length; i++)
        {
            var item = Items[i];
            var r = ItemRect(i);
            bool selected = i == _cursor && !_about;
            Color? accent = item is Item.Start or Item.Continue ? Palette.Green
                : item == Item.Debug ? (DebugMode.Enabled ? Palette.Pink : Palette.DarkPurple) : null;
            Gui.Button(b, c, r, Label(item), selected, true, accent);
            if (HasArrows(item))
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
            Item.Music => "GENERATED CHIPTUNE MUSIC",
            Item.Sfx => "CHIPTUNE BLIPS AND BLOOPS",
            Item.Continue => "PICK UP WHERE YOU LEFT OFF",
            Item.Options => "DIFFICULTY, SHOP, SOUND AND MORE",
            Item.Achievements => "BADGES FOR FEATS ACROSS ALL YOUR RUNS",
            Item.Journal => "CARDS, GEAR AND FOES YOU HAVE SEEN",
            Item.Back => "BACK TO THE MAIN MENU",
            Item.Start when _save is not null => "STARTING A NEW RUN REPLACES YOUR SAVE",
            Item.Debug => DebugMode.Enabled ? $"IN GAME PRESS {DebugMode.KeyName} FOR DEBUG COMMANDS" : "DEVELOPER TOOLS, THIS SESSION ONLY",
            _ => ""
        };
        Gui.TextCenter(b, c, Gui.Clip(hint.ToUpperInvariant(), 62), 128, MenuY + Items.Length * MenuGap + 1, Palette.Lavender);
        Gui.TextCenter(b, c, _options ? "UP/DOWN SELECT  </> CHANGE  ESC BACK" : "UP/DOWN SELECT  ENTER CHOOSE", 128, 137, Palette.DarkGrey);

        if (_unlockFlash > 0) DrawUnlockFlash(b, c, ItemRect(Items.Length - 1));
        if (_about) DrawAbout(b, c);
    }

    /// <summary>The debug unlock: a white flash that fades, and sparks bursting from the new menu item.</summary>
    private void DrawUnlockFlash(SpriteBatch b, PixelCanvas c, Rectangle item)
    {
        float t = 1 - _unlockFlash;
        c.Fill(b, new Rectangle(0, 0, PixelCanvas.Width, PixelCanvas.Height), Palette.White * (_unlockFlash * _unlockFlash * 0.7f));
        var rnd = new Random(8);
        for (int i = 0; i < 24; i++)
        {
            double angle = rnd.NextDouble() * Math.PI * 2;
            float dist = (float)(4 + rnd.NextDouble() * 40) * t;
            int x = item.Center.X + (int)(Math.Cos(angle) * dist * 1.6);
            int y = item.Center.Y + (int)(Math.Sin(angle) * dist * 0.6);
            c.Fill(b, x, y, 1, 1, i % 3 == 0 ? Palette.Yellow : i % 3 == 1 ? Palette.Pink : Palette.White);
        }
        if (_unlockFlash > 0.3f)
            Gui.TextCenter(b, c, "DEBUG UNLOCKED", 128, item.Y - 9, Palette.Yellow, shadow: true);
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
/// Pick 3 of the 8 classes in heroes.json (click or 1–8), then Start (Enter). Esc goes back to the title.
/// Round 11: a 4×2 grid; the hovered (or last picked) class's basic action is described under it.
/// Difficulty and react events were chosen on the title screen and are shown here.
/// </summary>
public sealed class PartySelectScreen : IScreen
{
    private const int PartySize = 3;
    private const int Columns = 4, BoxW = 58, BoxH = 40, BoxGap = 4, GridY = 24;

    private readonly ContentDb _content;
    private readonly Settings _settings;
    private readonly Action<IReadOnlyList<string>> _onStart;
    private readonly Action _onBack;
    private readonly List<HeroClassDef> _classes;
    private readonly List<string> _chosen = new();
    private Point _pointer;
    private float _time;
    private int _focus;

    public PartySelectScreen(ContentDb content, Settings settings, Action<IReadOnlyList<string>> onStart, Action onBack)
    {
        _content = content;
        _settings = settings;
        _onStart = onStart;
        _onBack = onBack;
        _classes = content.Heroes.Values.Take(Columns * 2).ToList();
        var previous = settings.LastParty?.Where(id => _classes.Any(c => c.Id == id)).Distinct().ToList();
        _chosen.AddRange(previous is { Count: PartySize } ? previous : _classes.Take(PartySize).Select(h => h.Id));
    }

    public Point ShakeOffset => Point.Zero;

    private Rectangle BoxRect(int i)
    {
        int cols = Math.Min(Columns, _classes.Count);
        int total = cols * BoxW + (cols - 1) * BoxGap;
        int col = i % Columns, row = i / Columns;
        return new Rectangle((PixelCanvas.Width - total) / 2 + col * (BoxW + BoxGap), GridY + row * (BoxH + 2), BoxW, BoxH);
    }

    private static readonly Rectangle StartButton = new(88, 124, 80, 11);

    public void Update(float dt, Input input)
    {
        _time += dt;
        _pointer = input.Pointer;

        if (input.Pressed(Keys.Escape))
        {
            _onBack();
            return;
        }

        for (int i = 0; i < _classes.Count; i++)
            if (input.Over(BoxRect(i))) _focus = i;

        int digit = input.PressedDigit();
        for (int i = 0; i < _classes.Count; i++)
        {
            if (digit != i && !input.Clicked(BoxRect(i))) continue;
            string id = _classes[i].Id;
            _focus = i;
            if (_chosen.Contains(id)) _chosen.Remove(id);
            else if (_chosen.Count < PartySize) _chosen.Add(id);
            else { Sfx.Play("deny"); continue; }
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
        Gui.TextCenter(b, c, "P I X E L 8", 128, 5, Palette.Yellow, shadow: true);
        Gui.TextCenter(b, c, $"CHOOSE {PartySize} HEROES  ({_chosen.Count}/{PartySize})", 128, 14, Palette.Peach);

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
            s.Draw(b, sprite, r.X + 3, r.Y + 3 - bob);
            Gui.TextRight(b, c, (i + 1).ToString(), r.Right - 3, r.Y + 3, Palette.DarkGrey);
            TinyFont.Draw(b, c, Gui.Clip(cls.Name.ToUpperInvariant(), 9), r.X + 13, r.Y + 5, chosen ? Palette.Yellow : Palette.White);

            int y = r.Y + 14;
            TinyFont.Draw(b, c, $"HP {cls.Hp}", r.X + 3, y, Palette.Green);
            TinyFont.Draw(b, c, $"AC {cls.Ac}", r.X + 31, y, Palette.Blue);
            y += 7;
            TinyFont.Draw(b, c, $"ATK +{cls.AttackMod}", r.X + 3, y, Palette.Orange);
            TinyFont.Draw(b, c, $"DC {cls.SpellDc}", r.X + 31, y, Palette.Lavender);
            y += 8;
            string basic = cls.BasicAction is { } id && _content.Cards.TryGetValue(id, out var bd) ? bd.Name.ToUpperInvariant() : "";
            TinyFont.Draw(b, c, Gui.Clip(basic, 13), r.X + 3, y, Palette.LightGrey);
        }

        // The focused class's basic action, in full.
        if (_focus >= 0 && _focus < _classes.Count && _classes[_focus].BasicAction is { } focusBasic
            && _content.Cards.TryGetValue(focusBasic, out var fb))
        {
            var lines = Gui.Wrap(fb.Text.ToUpperInvariant(), 60).Take(2).ToList();
            for (int i = 0; i < lines.Count; i++)
                Gui.TextCenter(b, c, lines[i], 128, 109 + i * 6 - (lines.Count - 1) * 3, Palette.LightGrey);
        }

        var difficulty = _content.Balance.DifficultyOf(_settings.Difficulty);
        Gui.TextCenter(b, c, $"{difficulty.Name.ToUpperInvariant()}   REACT EVENTS {(_settings.ReactEvents ? "ON" : "OFF")}", 128, 117, Palette.Lavender);

        bool ready = _chosen.Count == PartySize;
        Gui.Button(b, c, StartButton, "ENTER  START", ready && StartButton.Contains(_pointer), ready, Palette.Green);
        Gui.TextCenter(b, c, "1-8 HEROES   ENTER START   ESC BACK", 128, 138, Palette.DarkGrey);
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
