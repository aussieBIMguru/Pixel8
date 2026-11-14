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
/// → boss of Depth 5 (the Lich) → victory (or defeat) → title.
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
    /// <summary>The run just ended (victory or defeat screen): the sting plays and the music stops.</summary>
    private bool _runEnded;

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
        string dataDir = ContentDb.FindDataDirectory();
        _content = ContentDb.Load(dataDir);
        _settings = Settings.Load();
        Sfx.Load();
        Music.Load(Path.Combine(dataDir, "music"));
        _settings.ApplyAudio();
        ShowTitle();
    }

    // ------------------------------------------------------------------ run flow

    private void ShowTitle()
    {
        _run = null;
        _map = null;
        _runEnded = false;
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
        run.Noticed = JournalStore.Tracker.Note;
        AchievementStore.Report(AchievementStore.Tracker.RunStarted(run));
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
        _run.Noticed = JournalStore.Tracker.Note;
        _map = new MapScreen(_run, StartFight, ShowOverlay, ShowTitle);
        _screen = _map;
        AchievementStore.Report(AchievementStore.Tracker.RunStarted(_run));
    }

    /// <summary>Opens a screen over the map (campfire, merchant); its "done" callback returns to the map.</summary>
    private void ShowOverlay(Func<Action, IScreen> make) => _screen = make(BackToMap);

    private void StartFight(Tile tile, FightRequest request)
    {
        var combat = _run!.StartCombat(request);
        _screen = new CombatScreen(combat, outcome => EndFight(tile, combat, outcome, request));
    }

    private void EndFight(Tile tile, CombatState combat, CombatPhase outcome, FightRequest request)
    {
        var run = _run!;
        if (outcome == CombatPhase.Victory) run.AwardXp(combat.XpEarned);
        if (outcome == CombatPhase.Fled) run.Flees++;
        bool runWon = _map!.FightResolved(tile, outcome);
        AchievementStore.Report(AchievementStore.Tracker.FightEnded(run, combat, request.Mimic));
        JournalStore.Tracker.FightEnded(run, combat);

        if (outcome == CombatPhase.Defeat)
        {
            AchievementStore.Report(AchievementStore.Tracker.RunEnded(run, won: false));
            _runEnded = true;
            Music.Sting("defeat");
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

        Music.Sting("victory");

        void AfterRewards()
        {
            if (runWon)
            {
                AchievementStore.Report(AchievementStore.Tracker.RunEnded(run, won: true));
                _runEnded = true;
                SaveSlot.Delete();
                _screen = new MessageScreen("VICTORY!", new[] { "The dungeon is conquered.", $"Level {run.Party.Level}  Gold: {run.Party.Gold}" },
                    Palette.Yellow, ShowTitle);
            }
            else
            {
                BackToMap();
            }
        }

        void LevelUps()
        {
            if (run.PendingLevelUps.Count > 0) _screen = new LevelUpScreen(run, AfterRewards);
            else AfterRewards();
        }

        // Round 10 gear loot: mimics always pay out; other fights roll their table (none after the final boss).
        var source = request.Mimic ? LootSource.Mimic
            : combat.Kind switch { CombatKind.Boss => LootSource.Boss, CombatKind.Elite => LootSource.Elite, _ => LootSource.Normal };
        var loot = runWon ? null : run.RollLoot(source);

        // Victory → card draft → gear loot (if any) → level-ups (if any) → map (or the victory screen after the last boss).
        _screen = new DraftScreen(run, run.CreateDraft(), () =>
        {
            if (loot is not null) _screen = new LootScreen(run, loot, _ => LevelUps());
            else LevelUps();
        });
    }

    // ------------------------------------------------------------------ debug (round 8)

    private int _debugEnemy;

    /// <summary>
    /// Opens the debug panel over the current screen. Map: level up, next Depth, start any fight.
    /// Fight: win it. Anywhere: cannot die, full heal, gold, Luck, Torch.
    /// </summary>
    private void OpenDebug()
    {
        var run = _run!;
        var under = _screen;
        var combat = (under as CombatScreen)?.Combat;
        bool onMap = _map is not null && ReferenceEquals(under, _map);
        var enemies = _content.Enemies.Values.OrderBy(e => e.Tier).ThenBy(e => e.MinDepth).ThenBy(e => e.Id).ToList();
        _debugEnemy = Math.Clamp(_debugEnemy, 0, Math.Max(0, enemies.Count - 1));
        void Close() => _screen = under;

        void Fight(FightRequest request)
        {
            Close();
            StartFight(MapScreen.DebugTile(), request);
        }

        var commands = new List<DebugCommand>
        {
            new("+1 party level", () =>
            {
                run.AwardXp(Math.Max(1, run.NextLevelXp - run.Party.Xp));
                // On the map the picks happen now; in a fight they wait for the victory screen.
                if (onMap) _screen = new LevelUpScreen(run, BackToMap);
            }),
            new(() => run.Depth < Pixel8.Core.Runs.Run.FinalDepth ? $"Go down to Depth {run.Depth + 1}" : "Go down (last Depth)", () =>
            {
                _map!.DebugNextDepth();
                Close();
            }, () => onMap && _map!.CanGoDeeper),
            new("Fight: normal", () => Fight(new FightRequest(CombatKind.Normal)), () => onMap),
            new("Fight: elite", () => Fight(new FightRequest(CombatKind.Elite)), () => onMap),
            new("Fight: this Depth's boss", () => Fight(new FightRequest(CombatKind.Boss)), () => onMap),
            new(() => enemies.Count == 0 ? "Fight: (no enemies)" : $"Fight: {enemies[_debugEnemy].Name} (T{enemies[_debugEnemy].Tier})", () =>
            {
                var def = enemies[_debugEnemy];
                var kind = def.Tier >= 3 ? CombatKind.Boss : def.Tier == 2 ? CombatKind.Elite : CombatKind.Normal;
                Fight(new FightRequest(kind, Enemies: new[] { def.Id }));
            }, () => onMap && enemies.Count > 0,
               delta => _debugEnemy = (_debugEnemy + delta + enemies.Count) % Math.Max(1, enemies.Count)),
            new("Win this fight", () =>
            {
                combat!.DebugWin();
                Close();
            }, () => combat is { IsOver: false }),
            new(() => "Cannot die: " + (run.Invulnerable ? "ON" : "OFF"), () =>
            {
                run.Invulnerable = !run.Invulnerable;
                if (combat is not null) combat.Invulnerable = run.Invulnerable;
            }),
            new(() => "Skip events + traps: " + (DebugMode.SkipEvents ? "ON" : "OFF"), () => DebugMode.SkipEvents = !DebugMode.SkipEvents),
            new(() => "Skip fights (not bosses): " + (DebugMode.SkipFights ? "ON" : "OFF"), () => DebugMode.SkipFights = !DebugMode.SkipFights),
            new("Edit decks...", () => _screen = new DeckEditorScreen(run.Party.Heroes, _content, run.LootRng, Close), () => combat is null),
            new("Rest party (campfire rest)", () => run.Rest(), () => combat is null),
            new("Full heal", () =>
            {
                if (combat is not null) combat.DebugHealAll();
                else
                    foreach (var h in run.Party.Heroes.Where(h => !h.IsDead))
                    {
                        if (h.IsDowned) h.Revive(h.MaxHp);
                        h.Hp = h.MaxHp;
                    }
            }),
            new("+100 gold", () => run.Party.Gold += 100),
            new("Gear: random Rare to the pack", () =>
            {
                if (run.RollGear(Pixel8.Core.Cards.Rarity.Rare) is { } g) run.TakeLoot(g);
            }, () => combat is null),
            new("Achievements: unlock all", () => AchievementStore.Tracker.UnlockAll()),
            new("Achievements: reset", () => AchievementStore.Tracker.Reset()),
            new("Journal: reveal all", () => JournalStore.Tracker.RevealAll(_content)),
            new("Journal: reset", () => JournalStore.Tracker.Reset()),
            new("+1 Luck", () => run.Party.Luck++),
            new("+6 Torch", () => run.Party.Torch += 6),
        };

        string context = combat is not null ? "FIGHT" : onMap ? $"MAP D{run.Depth}" : "MENU";
        _screen = new DebugScreen(under, context, commands, Close);
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

        if (DebugMode.Enabled && _run is not null && _screen is not DebugScreen && _input.Pressed(DebugMode.Key))
        {
            OpenDebug();
            base.Update(gameTime);
            return;   // the same key press must not close the panel again this frame
        }

        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _screen.Update(dt, _input);
        if (_run is not null)
        {
            // Round 10: a run that ever had debug mode on doesn't count for achievements.
            if (DebugMode.Enabled) _run.Debugged = true;
            if (!_runEnded)
            {
                AchievementStore.Report(AchievementStore.Tracker.Check(_run));
                JournalStore.Tracker.Observe(_run);
            }
        }
        else if (AchievementStore.Tracker.Dirty) AchievementStore.Report(Array.Empty<Pixel8.Core.Achievements.AchievementDef>());
        JournalStore.SaveIfDirty();
        Toasts.Update(dt);
        Music.Play(TrackFor(_screen));
        Music.Update(dt);
        base.Update(gameTime);
    }

    /// <summary>
    /// Round 9 music: the title theme outside a run, the Depth's map theme on the map and its overlays,
    /// the combat or boss theme in fights, silence on the victory/defeat screens (their sting plays instead).
    /// The debug panel keeps whatever was playing.
    /// </summary>
    private string? TrackFor(IScreen screen) => screen switch
    {
        DebugScreen => Music.Wanted,
        CombatScreen fight => fight.Combat.Kind == CombatKind.Boss ? "boss" : "combat",
        _ when _runEnded => null,
        _ when _run is null => "title",
        _ => $"map{Math.Clamp(_run.Depth, 1, Pixel8.Core.Runs.Run.FinalDepth)}"
    };

    protected override void Draw(GameTime gameTime)
    {
        _canvas.Begin(_spriteBatch, Palette.Black, _screen.ShakeOffset);
        _screen.Draw(_spriteBatch, _canvas, _sprites);
        if (DebugMode.Enabled && _run is not null && _screen is not DebugScreen)
        {
            // A small reminder that debug mode is on, and which key opens it.
            string badge = $"DBG {DebugMode.KeyName}" + (_run.Invulnerable ? " GOD" : "");
            TinyFont.DrawShadowed(_spriteBatch, _canvas, badge, 1, PixelCanvas.Height - 6, Palette.Pink);
        }
        Toasts.Draw(_spriteBatch, _canvas, _sprites);
        _canvas.End(_spriteBatch);
        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        Music.Unload();
        Sfx.Unload();
        _canvas.Dispose();
        _sprites.Dispose();
        _spriteBatch.Dispose();
        base.UnloadContent();
    }
}
