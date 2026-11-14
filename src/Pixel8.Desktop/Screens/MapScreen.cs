using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Pixel8.Core.Combat;
using Pixel8.Core.Dice;
using Pixel8.Core.Dungeon;
using Pixel8.Core.Rewards;
using Pixel8.Core.Runs;
using Pixel8.Desktop.Fx;
using Pixel8.Desktop.Rendering;
using Pixel8.Desktop.Ui;

namespace Pixel8.Desktop.Screens;

/// <summary>
/// Walk a generated Depth with fog of war and the Torch meter.
/// Fight / Elite / Boss tiles hand off to the combat screen through <c>onFight</c>; the game calls
/// <see cref="FightResolved"/> afterwards. Treasure, traps and campfires resolve here.
/// Campfires and merchants open their own screens over the map through <c>overlay</c>.
/// Events open an <see cref="EventScreen"/>. Where the merchant lives depends on the Shop option (<see cref="ShopMode"/>).
/// Controls: arrows/WASD or click an adjacent room · P drink a potion · M shop (Shop: anytime) · R reroll this Depth (debug)
/// · F11 fullscreen · Esc title.
/// </summary>
public sealed class MapScreen : IScreen
{
    // 8×8 rooms on a 16px pitch: each room is one tile, the corridor between two rooms is one tile.
    private const int Cell = 16;
    private const int Room = PixelCanvas.Tile;
    private const int OriginX = 8;
    private const int OriginY = 4;
    private const int PanelX = 124;
    private const int WrapChars = 32;

    private readonly Run _run;
    private readonly Action<Tile, FightRequest> _onFight;
    private readonly Action<Func<Action, IScreen>> _overlay;
    private readonly Action _onQuit;
    private readonly ScreenShake _shake = new();
    private readonly HashSet<Tile> _usedCampfires = new();
    private readonly Dictionary<Tile, Merchant> _merchants = new();
    private Merchant? _depthMerchant;

    private DungeonGrid _grid = null!;
    private Vis[,] _vis = null!;
    private Tile _party = null!;
    private Tile? _previous;
    private int _reroll;
    private float _time;
    private float _flash;
    private Color _flashColor;
    private string _message = "";

    /// <param name="restore">A saved map to resume (Continue); null starts the run's current Depth fresh.</param>
    public MapScreen(Run run, Action<Tile, FightRequest> onFight, Action<Func<Action, IScreen>> overlay, Action onQuit, MapSave? restore = null)
    {
        _run = run;
        _onFight = onFight;
        _overlay = overlay;
        _onQuit = onQuit;
        if (restore is null) NewDepth();
        else Restore(restore);
    }

    private bool _leftMap;

    private void OpenOverlay(Func<Action, IScreen> make)
    {
        _leftMap = true;
        _overlay(make);
    }

    private void StartFight(Tile t, CombatKind kind) => StartFight(t, new FightRequest(kind));

    private void StartFight(Tile t, FightRequest request)
    {
        _leftMap = true;
        _onFight(t, request);
    }

    // ------------------------------------------------------------------ save / load

    /// <summary>Writes the run and this map to the save slot. Called whenever the map is idle (after a move, on return).</summary>
    public void Save()
    {
        var save = _run.ToSave();
        save.Map = new MapSave
        {
            Reroll = _reroll,
            X = _party.X,
            Y = _party.Y,
            PrevX = _previous?.X ?? -1,
            PrevY = _previous?.Y ?? -1,
            Tiles = _grid.Rooms.Select(t => new TileSave { X = t.X, Y = t.Y, Kind = t.Kind, Visited = t.Visited, Exposed = t.Exposed, Mimic = t.Mimic }).ToList(),
            UsedCampfires = _usedCampfires.Select(t => new[] { t.X, t.Y }).ToList(),
            Merchants = _merchants.Select(kv => SaveGame.Merchant(kv.Value, _run.Party, kv.Key.X, kv.Key.Y)).ToList(),
            DepthMerchant = _depthMerchant is null ? null : SaveGame.Merchant(_depthMerchant, _run.Party),
            Message = _message
        };
        SaveSlot.Write(save);
    }

    private void Restore(MapSave s)
    {
        _reroll = s.Reroll;
        _grid = DungeonGenerator.Generate(_run.DungeonSeed(_run.Depth, _reroll), _run.DungeonParamsFor(_run.Depth));
        foreach (var ts in s.Tiles)
        {
            if (ts.X < 0 || ts.Y < 0 || ts.X >= _grid.Width || ts.Y >= _grid.Height) continue;
            var t = _grid[ts.X, ts.Y];
            if (!t.IsRoom) continue;
            t.Kind = ts.Kind;
            t.Visited = ts.Visited;
            t.Exposed = ts.Exposed;
            t.Mimic = ts.Mimic;
        }
        _party = InGrid(s.X, s.Y) ?? _grid.Entrance;
        _previous = InGrid(s.PrevX, s.PrevY);
        _usedCampfires.Clear();
        foreach (var xy in s.UsedCampfires)
            if (xy.Length == 2 && InGrid(xy[0], xy[1]) is { } t) _usedCampfires.Add(t);
        _merchants.Clear();
        foreach (var m in s.Merchants)
            if (InGrid(m.X, m.Y) is { } t) _merchants[t] = SaveGame.Merchant(m, _run.Party, _run.Content);
        _depthMerchant = s.DepthMerchant is null ? null : SaveGame.Merchant(s.DepthMerchant, _run.Party, _run.Content);
        _message = s.Message.Length > 0 ? s.Message : $"DEPTH {_run.Depth}. WELCOME BACK.";
        RefreshVisibility();
    }

    private Tile? InGrid(int x, int y) =>
        x >= 0 && y >= 0 && x < _grid.Width && y < _grid.Height && _grid[x, y].IsRoom ? _grid[x, y] : null;

    public Point ShakeOffset => _shake.Offset;

    private void NewDepth()
    {
        _grid = DungeonGenerator.Generate(_run.DungeonSeed(_run.Depth, _reroll), _run.DungeonParamsFor(_run.Depth));
        _party = _grid.Entrance;
        _previous = null;
        _run.Party.StartDepth(_grid.Torch);
        _usedCampfires.Clear();
        _merchants.Clear();
        _depthMerchant = null;
        _message = $"DEPTH {_run.Depth}. FIND THE STAIRS.";
        RefreshVisibility();
        Save();
    }

    private void RefreshVisibility()
    {
        _vis = Visibility.Compute(_grid);
        _run.AtCampfire = _party.Kind == TileKind.Campfire;   // cursed gear can come off here (round 10)
    }

    /// <summary>How many goal icons (treasure, campfire, merchant, boss) the fog currently shows.</summary>
    private int VisibleIcons() => _grid.Rooms.Count(t => !t.Visited
        && Visibility.VisibleKind(t, _vis[t.X, t.Y]) is TileKind.Treasure or TileKind.Campfire or TileKind.Merchant or TileKind.Boss);

    public void Update(float dt, Input input)
    {
        _time += dt;
        _flash = Math.Max(0, _flash - dt * 3f);
        _shake.Update(dt);

        if (input.PressedAny(Keys.Up, Keys.W)) TryMove(Dir.N);
        else if (input.PressedAny(Keys.Right, Keys.D)) TryMove(Dir.E);
        else if (input.PressedAny(Keys.Down, Keys.S)) TryMove(Dir.S);
        else if (input.PressedAny(Keys.Left, Keys.A)) TryMove(Dir.W);
        else if (input.Pressed(Keys.Escape))
        {
            _onQuit();
        }
        else if (input.Pressed(Keys.G))
        {
            OpenOverlay(back => new GearScreen(_run, back));
        }
        else if (input.Pressed(Keys.J))
        {
            // Round 11: the Adventurer's Journal.
            OpenOverlay(back => new JournalScreen(_run.Content, back));
        }
        else if (input.Pressed(Keys.P))
        {
            DrinkPotion();
            Save();
        }
        else if (input.Pressed(Keys.M))
        {
            if (_run.Shop == ShopMode.Anytime)
            {
                _depthMerchant ??= _run.CreateMerchant();
                var merchant = _depthMerchant;
                OpenOverlay(back => new MerchantScreen(_run, merchant, back));
            }
            else
            {
                _message = _run.Shop == ShopMode.AtCampfires ? "THE MERCHANT WAITS AT THE CAMPFIRES." : "FIND THE MERCHANT ON THE MAP.";
                Sfx.Play("deny");
            }
        }
        else if (input.Pressed(Keys.R))
        {
            _reroll++;
            NewDepth();
        }
        else if (input.LeftClick)
        {
            var p = input.Pointer;
            if (p.X >= OriginX - Room / 2 && p.Y >= OriginY - Room / 2)
            {
                int cx = (p.X - OriginX + Room / 2) / Cell, cy = (p.Y - OriginY + Room / 2) / Cell;
                foreach (var d in Dirs.All)
                {
                    var (dx, dy) = d.Delta();
                    if (_party.X + dx == cx && _party.Y + dy == cy)
                    {
                        TryMove(d);
                        break;
                    }
                }
            }
        }
    }

    private void TryMove(Dir d)
    {
        if (!_party.Has(d))
        {
            _shake.Trigger(1, 0.08f); // bump into wall
            return;
        }

        _leftMap = false;
        _previous = _party;
        _party = _grid.Neighbour(_party, d)!;
        _run.Party.Torch = Math.Max(0, _run.Party.Torch - 1);

        bool firstVisit = !_party.Visited;
        _party.Visited = true;
        int iconsBefore = VisibleIcons();
        RefreshVisibility();
        Sfx.Play("step", 0.8f, (float)(Random.Shared.NextDouble() * 0.3 - 0.15));
        // A treasure, campfire, merchant or the boss came into view.
        if (VisibleIcons() > iconsBefore) Sfx.Play("reveal");
        if (_run.Party.Torch == 3) Sfx.Play("torch_low");

        // Fights you fled from are still there; campfires can be revisited (but only rested at once).
        bool hostile = _party.Kind is TileKind.Encounter or TileKind.Elite or TileKind.Boss;
        if (firstVisit || hostile || _party.Kind is TileKind.Campfire or TileKind.Merchant) Resolve(_party);
        if (_run.Party.Torch == 0 && _message.Length == 0) _message = "DARKNESS! HEROES ATTACK WITH DISADVANTAGE.";
        // Only save while the map is idle: a fight or overlay saves when the party gets back (Pixel8Game).
        // Quitting mid-fight therefore resumes from before the step onto that tile.
        if (!_leftMap) Save();
    }

    private void Resolve(Tile t)
    {
        _message = "";
        var party = _run.Party;
        if (DebugMode.Enabled)
        {
            bool skipEvent = DebugMode.SkipEvents && t.Kind is TileKind.Event or TileKind.Trap;
            bool skipFight = DebugMode.SkipFights && t.Kind is TileKind.Encounter or TileKind.Elite;
            if (skipEvent || skipFight)
            {
                _message = $"DEBUG: SKIPPED {t.Kind}".ToUpperInvariant();
                Clear(t);
                return;
            }
        }
        switch (t.Kind)
        {
            case TileKind.Treasure when t.Mimic:
                // Round 10: a hidden treasure that bites back. Elite rules (no fleeing); winning pays the mimic's hoard.
                _shake.Trigger(3, 0.4f);
                Flash(Palette.Red, 0.4f);
                Sfx.Play("roar");
                _message = "THE CHEST HAS TEETH! A MIMIC!";
                StartFight(t, new FightRequest(CombatKind.Elite, Enemies: new[] { "mimic" }, Mimic: true));
                break;
            case TileKind.Treasure:
            {
                int gold = _run.OpenTreasure();
                Flash(Palette.Yellow, 0.5f);
                Sfx.Play("coins");
                _message = $"TREASURE! +{gold} GOLD";
                Clear(t);
                // Round 10: treasure may hold gear too.
                if (_run.RollLoot(LootSource.Treasure) is { } loot)
                {
                    string goldLine = _message;
                    OpenOverlay(back => new LootScreen(_run, loot, msg =>
                    {
                        _message = $"{goldLine}. {msg}";
                        back();
                    }));
                }
                break;
            }
            case TileKind.Encounter:
                _shake.Trigger(2, 0.25f);
                StartFight(t, CombatKind.Normal);
                break;
            case TileKind.Elite:
                _shake.Trigger(3, 0.35f);
                StartFight(t, CombatKind.Elite);
                break;
            case TileKind.Boss:
                _shake.Trigger(4, 0.5f);
                StartFight(t, CombatKind.Boss);
                break;
            case TileKind.Trap:
            {
                var trap = _run.SpringTrap();
                if (trap is null) break;
                if (trap.Spotted)
                {
                    Sfx.Play("trap_spot");
                    _message = $"TRAP! {trap.Spotter.Name} spots it ({trap.Save.Total} vs {trap.Dc})".ToUpperInvariant();
                }
                else
                {
                    _shake.Trigger(2, 0.2f);
                    Flash(Palette.Red, 0.3f);
                    Sfx.Play("hurt");
                    _message = $"TRAP! {trap.Victim!.Name} takes {trap.Damage} ({trap.Save.Total} vs {trap.Dc})".ToUpperInvariant();
                }
                Clear(t);
                break;
            }
            case TileKind.Event:
            {
                Flash(Palette.Lavender, 0.4f);
                Clear(t);
                var ev = _run.PickEvent();
                if (ev is null)
                {
                    _message = "AN EMPTY ROOM. NOTHING STIRS.";
                    break;
                }
                _message = ev.Title.ToUpperInvariant();
                OpenOverlay(back => new EventScreen(_run, ev, (msg, fight) =>
                {
                    _message = msg;
                    if (fight is null)
                    {
                        back();
                        return;
                    }
                    // The event started a fight on this (now cleared) tile; the map saves when the party gets back.
                    _shake.Trigger(fight.Kind == CombatKind.Elite ? 3 : 2, 0.3f);
                    StartFight(t, fight);
                }));
                break;
            }
            case TileKind.Campfire when !_usedCampfires.Contains(t):
            {
                _usedCampfires.Add(t);
                int torch = _run.Content.Balance.CampfireTorch;
                party.Torch += torch;
                Flash(Palette.Orange, 0.5f);
                Sfx.Play("fire");
                _message = $"CAMPFIRE: +{torch} TORCH.";
                Merchant? fireShop = null;
                if (_run.Shop == ShopMode.AtCampfires)
                {
                    if (!_merchants.TryGetValue(t, out fireShop))
                        _merchants[t] = fireShop = _run.CreateMerchant();
                }
                OpenOverlay(back => new CampfireScreen(_run, choice =>
                {
                    _message = choice;
                    back();
                }, fireShop));
                break;
            }
            case TileKind.Campfire when _run.Shop == ShopMode.AtCampfires && _merchants.TryGetValue(t, out var campShop):
                _message = "THE FIRE HAS BURNT LOW, BUT THE MERCHANT LINGERS.";
                OpenOverlay(back => new MerchantScreen(_run, campShop, back));
                break;
            case TileKind.Campfire:
                _message = "THE FIRE HAS BURNT LOW.";
                break;
            case TileKind.Merchant:
            {
                if (!_merchants.TryGetValue(t, out var merchant))
                    _merchants[t] = merchant = _run.CreateMerchant();
                _message = "THE MERCHANT WAVES YOU OFF. COME BACK ANY TIME.";
                OpenOverlay(back => new MerchantScreen(_run, merchant, back));
                break;
            }
        }
    }

    /// <summary>Called by the game when a fight started from <paramref name="tile"/> ends. Returns true when the run is won.</summary>
    public bool FightResolved(Tile tile, CombatPhase outcome)
    {
        if (tile.X < 0)
        {
            // A debug fight: it isn't on the map, so nothing here changes.
            _message = $"DEBUG FIGHT: {outcome}".ToUpperInvariant();
            return false;
        }
        switch (outcome)
        {
            case CombatPhase.Victory when tile.Kind == TileKind.Boss:
                if (_run.Depth >= Run.FinalDepth) return true;
                _run.Depth++;
                _reroll = 0;
                NewDepth();
                Sfx.Play("stairs");
                Flash(Palette.White, 0.8f);
                _message = $"THE BOSS FALLS! DOWN TO DEPTH {_run.Depth}.";
                break;
            case CombatPhase.Victory:
                Clear(tile);
                _message = "THE ROOM IS CLEAR.";
                break;
            case CombatPhase.Fled:
                if (_previous is not null) _party = _previous;
                _message = "YOU FLED BACK THE WAY YOU CAME.";
                RefreshVisibility();
                break;
        }
        return false;
    }

    // ------------------------------------------------------------------ debug (round 8)

    /// <summary>A tile that isn't on the map, for fights started from the debug panel.</summary>
    public static Tile DebugTile() => new(-1, -1) { Kind = TileKind.Empty };

    public bool CanGoDeeper => _run.Depth < Run.FinalDepth;

    /// <summary>Debug: skip to the next Depth (a fresh map), as if the boss had fallen.</summary>
    public void DebugNextDepth()
    {
        if (!CanGoDeeper) return;
        _run.Depth++;
        _reroll = 0;
        NewDepth();
        Flash(Palette.Pink, 0.6f);
        _message = $"DEBUG: DOWN TO DEPTH {_run.Depth}.";
    }

    /// <summary>P: the most hurt living hero drinks the first healing item in the Bag.</summary>
    private void DrinkPotion()
    {
        var party = _run.Party;
        string? item = party.Bag.FirstOrDefault();
        var hero = party.Heroes.Where(h => !h.IsDead && h.Hp < h.MaxHp).OrderBy(h => (double)h.Hp / h.MaxHp).FirstOrDefault();
        if (item is null) { _message = "THE BAG IS EMPTY."; Sfx.Play("deny"); return; }
        if (hero is null) { _message = "NOBODY NEEDS HEALING."; Sfx.Play("deny"); return; }
        int healed = _run.UseItemOutOfCombat(item, hero);
        if (healed < 0) { _message = "THAT CAN'T BE USED HERE."; Sfx.Play("deny"); return; }
        Flash(Palette.Green, 0.3f);
        Sfx.Play("heal");
        _message = $"{hero.Name} drinks a potion: +{healed} HP".ToUpperInvariant();
    }

    private static void Clear(Tile t)
    {
        t.Kind = TileKind.Empty;
        t.Exposed = false;
        t.Mimic = false;
    }

    private void Flash(Color c, float strength)
    {
        _flashColor = c;
        _flash = strength;
    }

    // ------------------------------------------------------------------ drawing

    public void Draw(SpriteBatch batch, PixelCanvas canvas, SpriteSheet sprites)
    {
        DrawMap(batch, canvas, sprites);
        DrawPanel(batch, canvas, sprites);

        if (_flash > 0)
            canvas.Fill(batch, -8, -8, PixelCanvas.Width + 16, PixelCanvas.Height + 16, _flashColor * (_flash * 0.5f));
    }

    private void DrawMap(SpriteBatch batch, PixelCanvas canvas, SpriteSheet sprites)
    {
        canvas.Outline(batch, new Rectangle(OriginX - 4, OriginY - 4, _grid.Width * Cell, _grid.Height * Cell), Palette.DarkBlue);

        foreach (var t in _grid.All)
        {
            var v = _vis[t.X, t.Y];
            int x = OriginX + t.X * Cell, y = OriginY + t.Y * Cell;

            if (v == Vis.Dark)
            {
                if (t.Exposed && (int)(_time * 2 + t.X) % 2 == 0) sprites.Draw(batch, "glint", x, y);
                continue;
            }

            var tone = v == Vis.Known ? Palette.DarkGrey : Palette.DarkBlue;

            // corridor stubs: half a tile each side, so two linked rooms meet in the middle
            if (t.Has(Dir.N)) canvas.Fill(batch, x + 2, y - 4, 4, 4, tone);
            if (t.Has(Dir.S)) canvas.Fill(batch, x + 2, y + Room, 4, 4, tone);
            if (t.Has(Dir.W)) canvas.Fill(batch, x - 4, y + 2, 4, 4, tone);
            if (t.Has(Dir.E)) canvas.Fill(batch, x + Room, y + 2, 4, 4, tone);

            if (v == Vis.Far)
                canvas.Outline(batch, new Rectangle(x, y, Room, Room), Palette.DarkBlue);
            else
                sprites.Draw(batch, "floor", x, y, tone);

            if (t == _party) continue;
            var kind = Visibility.VisibleKind(t, v);
            if (kind is null)
                sprites.Draw(batch, "unknown", x, y);
            else
                DrawIcon(batch, sprites, x, y, kind.Value);
        }

        // The party is drawn as its first standing hero, bobbing a pixel.
        int bob = (int)(_time * 4) % 2;
        var lead = _run.Party.Heroes.FirstOrDefault(h => h.CanAct) ?? _run.Party.Heroes[0];
        string heroSprite = sprites.Has("hero." + lead.Class.Id) ? "hero." + lead.Class.Id : "hero";
        sprites.Draw(batch, heroSprite, OriginX + _party.X * Cell, OriginY + _party.Y * Cell - bob);
    }

    private void DrawIcon(SpriteBatch batch, SpriteSheet sprites, int x, int y, TileKind kind)
    {
        switch (kind)
        {
            case TileKind.Boss:
                sprites.Draw(batch, "boss", x, y, (int)(_time * 2) % 2 == 0 ? Color.White : Palette.Pink);
                break;
            case TileKind.Campfire:
                sprites.Draw(batch, (int)(_time * 6) % 2 == 0 ? "camp0" : "camp1", x, y);
                break;
            default:
                string? name = SpriteFor(kind);
                if (name is not null) sprites.Draw(batch, name, x, y);
                break;
        }
    }

    private static string? SpriteFor(TileKind kind) => kind switch
    {
        TileKind.Treasure => "chest",
        TileKind.Encounter => "fight",
        TileKind.Elite => "elite",
        TileKind.Trap => "trap",
        TileKind.Event => "event",
        TileKind.Campfire => "camp0",
        TileKind.Merchant => "shop",
        TileKind.Boss => "boss",
        TileKind.Entrance => "stairs",
        _ => null
    };

    private void DrawPanel(SpriteBatch batch, PixelCanvas canvas, SpriteSheet sprites)
    {
        var party = _run.Party;
        int x = PanelX, y = 4;

        TinyFont.DrawShadowed(batch, canvas, $"PIXEL8  DEPTH {_run.Depth}/{Run.FinalDepth}", x, y, Palette.Peach);
        Gui.TextRight(batch, canvas, _run.Difficulty.Name.ToUpperInvariant(), 252, y, Palette.Lavender, shadow: true);
        y += 10;

        TinyFont.DrawShadowed(batch, canvas, $"TORCH {party.Torch}", x, y, Palette.Orange);
        Gui.TextCenter(batch, canvas, $"LUCK {party.Luck}  POT {party.Bag.Count}", 188, y, Palette.Green, shadow: true);
        Gui.TextRight(batch, canvas, $"GOLD {party.Gold}", 252, y, Palette.Yellow, shadow: true);
        y += 7;
        int max = Math.Min(Math.Max(_grid.Torch, party.Torch), 62);
        canvas.Fill(batch, x, y, max * 2 + 2, 4, Palette.DarkBlue);
        canvas.Fill(batch, x + 1, y + 1, Math.Min(party.Torch, max) * 2, 2, party.Torch > 3 ? Palette.Orange : Palette.Red);
        y += 8;

        // Party: sprite, name, HP, and cards in hand + draw pile
        foreach (var h in party.Heroes)
        {
            string sprite = h.IsDead ? "i.skull" : sprites.Has("hero." + h.Class.Id) ? "hero." + h.Class.Id : "hero";
            sprites.Draw(batch, sprite, x, y);
            TinyFont.Draw(batch, canvas, Gui.Clip(h.Name.ToUpperInvariant(), 8), x + 11, y + 2, h.IsDead ? Palette.DarkGrey : Palette.White);
            Gui.HpBar(batch, canvas, x + 46, y + 3, 40, h.Hp, h.MaxHp);
            TinyFont.Draw(batch, canvas, h.IsDead ? "DEAD" : $"{h.Hp}/{h.MaxHp}", x + 90, y + 2, Palette.LightGrey);
            Gui.TextRight(batch, canvas, $"{h.Deck.Hand.Count}+{h.Deck.DrawPile.Count}", 252, y + 2, Palette.DarkGrey);
            y += 10;
        }
        y += 2;

        // Legend: 4 columns × 2 rows of 8×8 icons
        (string Label, TileKind Kind)[] legend =
        {
            ("CHEST", TileKind.Treasure), ("FIGHT", TileKind.Encounter), ("ELITE", TileKind.Elite), ("TRAP", TileKind.Trap),
            ("EVENT", TileKind.Event), ("CAMP", TileKind.Campfire), ("SHOP", TileKind.Merchant), ("BOSS", TileKind.Boss)
        };
        for (int i = 0; i < legend.Length; i++)
        {
            int lx = x + (i % 4) * 33, ly = y + (i / 4) * 10;
            DrawIcon(batch, sprites, lx, ly, legend[i].Kind);
            TinyFont.Draw(batch, canvas, legend[i].Label, lx + 10, ly + 2, Palette.LightGrey);
        }
        y += 2 * 10 + 4;

        foreach (string line in Gui.Wrap(_message, WrapChars).Take(3))
        {
            TinyFont.DrawShadowed(batch, canvas, line, x, y, Palette.Peach);
            y += 7;
        }

        // Party level and XP towards the next level
        int xpY = PixelCanvas.Height - 25;
        int prev = _run.Content.Balance.XpForLevel(party.Level), next = _run.NextLevelXp;
        TinyFont.Draw(batch, canvas, $"LV {party.Level}", x, xpY, Palette.Yellow);
        Gui.TextRight(batch, canvas, $"XP {party.Xp}/{next}", 252, xpY, Palette.LightGrey);
        int barX = x + 24, barW = 252 - 48 - barX;
        canvas.Fill(batch, barX, xpY + 1, barW, 3, Palette.DarkBlue);
        int fill = next > prev ? (int)Math.Round((barW - 2) * Math.Clamp((party.Xp - prev) / (double)(next - prev), 0, 1)) : 0;
        canvas.Fill(batch, barX + 1, xpY + 2, fill, 1, Palette.Yellow);

        TinyFont.Draw(batch, canvas, _run.Shop == ShopMode.Anytime ? "G GEAR J BOOK P POT M SHOP" : "G GEAR  J JOURNAL  P POTION",
            x, PixelCanvas.Height - 15, Palette.DarkGrey);
        TinyFont.Draw(batch, canvas, $"F11 FULL  ESC TITLE  SEED {_run.Seed % 100000}", x, PixelCanvas.Height - 8, Palette.DarkGrey);
    }
}
