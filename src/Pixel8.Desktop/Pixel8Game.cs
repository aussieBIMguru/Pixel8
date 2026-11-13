using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Pixel8.Core.Combat;
using Pixel8.Core.Content;
using Pixel8.Core.Dungeon;
using Pixel8.Core.Runs;
using Pixel8.Desktop.Fx;
using Pixel8.Desktop.Rendering;
using Pixel8.Desktop.Screens;
using Pixel8.Desktop.Ui;

namespace Pixel8.Desktop;

/// <summary>
/// Owns the window, the 256×144 canvas and the current screen, and wires the run flow together:
/// title (difficulty, sound, react events, about) → party select → map ⇄ combat / campfire / merchant → card draft → …
/// → boss of Depth 3 → victory (or defeat) → title.
/// </summary>
public sealed class Pixel8Game : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly Input _input = new();
    private SpriteBatch _spriteBatch = null!;
    private PixelCanvas _canvas = null!;
    private SpriteSheet _sprites = null!;
    private ContentDb _content = null!;
    private IScreen _screen = null!;
    private Settings _settings = null!;
    private Run? _run;
    private MapScreen? _map;

    public Pixel8Game()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = PixelCanvas.Width * 5,   // 1280
            PreferredBackBufferHeight = PixelCanvas.Height * 5, // 720
            SynchronizeWithVerticalRetrace = true
        };
        Window.Title = "Pixel8";
        Window.AllowUserResizing = true;
        IsMouseVisible = true;
        Content.RootDirectory = "Content";
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _canvas = new PixelCanvas(GraphicsDevice);
        _sprites = new SpriteSheet(GraphicsDevice);
        _content = ContentDb.Load(ContentDb.FindDataDirectory());
        _settings = Settings.Load();
        Sfx.Load();
        Sfx.Enabled = _settings.Sound;
        ShowTitle();
    }

    // ------------------------------------------------------------------ run flow

    private void ShowTitle()
    {
        _run = null;
        _map = null;
        var save = SaveSlot.Load();
        _screen = new TitleScreen(_content, _settings, ShowPartySelect, Exit, save, save is null ? null : () => ContinueRun(save));
    }

    /// <summary>Title "Continue": rebuild the run and its map from the save slot.</summary>
    private void ContinueRun(RunSave save)
    {
        try
        {
            _run = Pixel8.Core.Runs.Run.FromSave(_content, save);
            _map = new MapScreen(_run, StartFight, ShowOverlay, ShowTitle, save.Map);
        }
        catch (Exception ex) when (ex is InvalidDataException or KeyNotFoundException or ArgumentException or InvalidOperationException)
        {
            SaveSlot.Delete();
            _screen = new MessageScreen("SAVE ERROR", new[] { "That save couldn't be loaded.", "Starting fresh." }, Palette.Red, ShowTitle);
            return;
        }
        var run = _run;
        if (run.PendingLevelUps.Count > 0) _screen = new LevelUpScreen(run, BackToMap);
        else _screen = _map;
    }

    /// <summary>Returns to the map and saves (the map is idle again).</summary>
    private void BackToMap()
    {
        _screen = _map!;
        _map!.Save();
    }

    private void ShowPartySelect() =>
        _screen = new PartySelectScreen(_content, _settings, StartRun, ShowTitle);

    private void StartRun(IReadOnlyList<string> classIds)
    {
        SaveSlot.Delete();
        _run = new Run(_content, (ulong)Environment.TickCount64, classIds, _settings.Difficulty)
        {
            Timing = _settings.ReactEvents ? TimingMode.On : TimingMode.Off,
            Shop = Enum.TryParse<ShopMode>(_settings.Shop, out var shop) ? shop : ShopMode.OnMap
        };
        _map = new MapScreen(_run, StartFight, ShowOverlay, ShowTitle);
        _screen = _map;
    }

    /// <summary>Opens a screen over the map (campfire, merchant); its "done" callback returns to the map.</summary>
    private void ShowOverlay(Func<Action, IScreen> make) => _screen = make(BackToMap);

    private void StartFight(Tile tile, CombatKind kind)
    {
        var combat = _run!.StartCombat(kind);
        _screen = new CombatScreen(combat, outcome => EndFight(tile, combat, outcome));
    }

    private void EndFight(Tile tile, CombatState combat, CombatPhase outcome)
    {
        var run = _run!;
        if (outcome == CombatPhase.Victory) run.AwardXp(combat.XpEarned);
        bool runWon = _map!.FightResolved(tile, outcome);

        if (outcome == CombatPhase.Defeat)
        {
            SaveSlot.Delete();
            _screen = new MessageScreen("DEFEAT", new[] { $"The party fell on Depth {run.Depth}.", $"Gold: {run.Party.Gold}" },
                Palette.Red, ShowTitle);
            return;
        }

        if (outcome != CombatPhase.Victory)
        {
            BackToMap();
            return;
        }

        void AfterRewards()
        {
            if (runWon)
            {
                SaveSlot.Delete();
                _screen = new MessageScreen("VICTORY!", new[] { "The dungeon is conquered.", $"Level {run.Party.Level}  Gold: {run.Party.Gold}" },
                    Palette.Yellow, ShowTitle);
            }
            else
            {
                BackToMap();
            }
        }

        // Victory → card draft → level-ups (if any) → map (or the victory screen after the last boss).
        _screen = new DraftScreen(run, run.CreateDraft(), () =>
        {
            if (run.PendingLevelUps.Count > 0) _screen = new LevelUpScreen(run, AfterRewards);
            else AfterRewards();
        });
    }

    // ------------------------------------------------------------------ loop

    protected override void Update(GameTime gameTime)
    {
        _input.Update(_canvas, IsActive);

        if (_input.Pressed(Keys.F11))
        {
            _graphics.IsFullScreen = !_graphics.IsFullScreen;
            _graphics.ApplyChanges();
        }

        _screen.Update((float)gameTime.ElapsedGameTime.TotalSeconds, _input);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        _canvas.Begin(_spriteBatch, Palette.Black, _screen.ShakeOffset);
        _screen.Draw(_spriteBatch, _canvas, _sprites);
        _canvas.End(_spriteBatch);
        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        Sfx.Unload();
        _canvas.Dispose();
        _sprites.Dispose();
        _spriteBatch.Dispose();
        base.UnloadContent();
    }
}
