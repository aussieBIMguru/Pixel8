using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Pixel8.Core.Dice;
using Pixel8.Core.Dungeon;
using Pixel8.Desktop.Fx;
using Pixel8.Desktop.Rendering;

namespace Pixel8.Desktop.Screens;

/// <summary>
/// M2 prototype: walk a generated Depth with fog of war and the Torch meter.
/// Tiles resolve as placeholders (flash + shake) until combat/loot screens exist.
/// Controls: arrows/WASD or click an adjacent tile · R = new dungeon · F11 fullscreen · Esc quit.
/// </summary>
public sealed class MapScreen
{
    private const int Cell = 16;
    private const int OriginX = 8;
    private const int OriginY = 22;
    private const int PanelX = 132;

    private readonly ScreenShake _shake = new();
    private readonly HashSet<Tile> _usedCampfires = new();

    private DungeonGrid _grid = null!;
    private Vis[,] _vis = null!;
    private Tile _party = null!;
    private ulong _seed;
    private int _depth = 1;
    private int _torch;
    private int _gold;
    private float _time;
    private float _flash;
    private Color _flashColor;
    private string _message = "";
    private KeyboardState _prevKeys;
    private MouseState _prevMouse;

    public MapScreen(ulong seed) => NewDungeon(seed, 1);

    public Point ShakeOffset => _shake.Offset;

    private void NewDungeon(ulong seed, int depth)
    {
        _seed = seed;
        _depth = depth;
        _grid = DungeonGenerator.Generate(seed, new DungeonParams { Depth = depth });
        _party = _grid.Entrance;
        _torch = _grid.Torch;
        _usedCampfires.Clear();
        _message = $"DEPTH {depth}. FIND THE STAIRS.";
        RefreshVisibility();
    }

    private void RefreshVisibility() => _vis = Visibility.Compute(_grid);

    public void Update(GameTime gameTime, PixelCanvas canvas)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _time += dt;
        _flash = Math.Max(0, _flash - dt * 3f);
        _shake.Update(dt);

        var keys = Keyboard.GetState();
        var mouse = Mouse.GetState();
        bool Pressed(Keys k) => keys.IsKeyDown(k) && !_prevKeys.IsKeyDown(k);

        if (Pressed(Keys.Up) || Pressed(Keys.W)) TryMove(Dir.N);
        if (Pressed(Keys.Right) || Pressed(Keys.D)) TryMove(Dir.E);
        if (Pressed(Keys.Down) || Pressed(Keys.S)) TryMove(Dir.S);
        if (Pressed(Keys.Left) || Pressed(Keys.A)) TryMove(Dir.W);
        if (Pressed(Keys.R)) NewDungeon(_seed + 1, 1);

        if (mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released)
        {
            var p = canvas.ToCanvas(mouse.Position);
            if (p.X >= OriginX && p.Y >= OriginY)
            {
                int cx = (p.X - OriginX) / Cell, cy = (p.Y - OriginY) / Cell;
                foreach (var d in Dirs.All)
                {
                    var (dx, dy) = d.Delta();
                    if (_party.X + dx == cx && _party.Y + dy == cy) TryMove(d);
                }
            }
        }

        _prevKeys = keys;
        _prevMouse = mouse;
    }

    private void TryMove(Dir d)
    {
        if (!_party.Has(d))
        {
            _shake.Trigger(1, 0.08f); // bump into wall
            return;
        }

        _party = _grid.Neighbour(_party, d)!;
        _torch = Math.Max(0, _torch - 1);

        bool firstVisit = !_party.Visited;
        _party.Visited = true;
        if (firstVisit || _party.Kind == TileKind.Campfire) Resolve(_party);
        if (_torch == 0 && _message.Length == 0) _message = "DARKNESS! DISADVANTAGE ON CHECKS.";

        RefreshVisibility();
    }

    /// <summary>Placeholder resolution — each branch becomes a real screen in M3/M4.</summary>
    private void Resolve(Tile t)
    {
        _message = "";
        switch (t.Kind)
        {
            case TileKind.Treasure:
                _gold += 25;
                Flash(Palette.Yellow, 0.5f);
                _message = "TREASURE! +25 GOLD";
                Clear(t);
                break;
            case TileKind.Encounter:
                _shake.Trigger(2, 0.25f);
                Flash(Palette.Red, 0.4f);
                _message = "AMBUSH! (COMBAT SCREEN TODO)";
                Clear(t);
                break;
            case TileKind.Elite:
                _shake.Trigger(3, 0.35f);
                Flash(Palette.DarkPurple, 0.6f);
                _message = "ELITE FOE! (COMBAT SCREEN TODO)";
                Clear(t);
                break;
            case TileKind.Trap:
            {
                // Demo of the shared odds function: Rogue DEX +5 vs DC 13
                var odds = D20.Odds(5, 13);
                _shake.Trigger(1, 0.15f);
                Flash(Palette.Orange, 0.3f);
                _message = $"TRAP! DEX DC 13: {odds}";
                Clear(t);
                break;
            }
            case TileKind.Event:
                Flash(Palette.Lavender, 0.4f);
                _message = "A STRANGE SHRINE... (EVENT TODO)";
                Clear(t);
                break;
            case TileKind.Campfire when !_usedCampfires.Contains(t):
                _usedCampfires.Add(t);
                _torch += 6;
                Flash(Palette.Orange, 0.5f);
                _message = "CAMPFIRE: REST / RESTOCK. +6 TORCH";
                break;
            case TileKind.Merchant:
                _message = "MERCHANT (SHOP TODO)";
                break;
            case TileKind.Boss:
                _shake.Trigger(4, 0.6f);
                Flash(Palette.White, 0.8f);
                NewDungeon(_seed + 1000, _depth + 1);
                _message = $"BOSS DEFEATED! DESCENDING TO DEPTH {_depth}";
                break;
        }
    }

    private static void Clear(Tile t)
    {
        t.Kind = TileKind.Empty;
        t.Exposed = false;
    }

    private void Flash(Color c, float strength)
    {
        _flashColor = c;
        _flash = strength;
    }

    // ------------------------------------------------------------------ drawing

    public void Draw(SpriteBatch batch, PixelCanvas canvas)
    {
        DrawMap(batch, canvas);
        DrawPanel(batch, canvas);

        if (_flash > 0)
            canvas.Fill(batch, -8, -8, PixelCanvas.Width + 16, PixelCanvas.Height + 16, _flashColor * (_flash * 0.5f));
    }

    private void DrawMap(SpriteBatch batch, PixelCanvas canvas)
    {
        TinyFont.DrawShadowed(batch, canvas, $"PIXEL8 - DEPTH {_depth}", OriginX, 8, Palette.Peach);

        // frame
        canvas.Outline(batch, new Rectangle(OriginX - 2, OriginY - 2,
            _grid.Width * Cell + 4, _grid.Height * Cell + 4), Palette.DarkBlue);

        foreach (var t in _grid.All)
        {
            var v = _vis[t.X, t.Y];
            var r = new Rectangle(OriginX + t.X * Cell + 2, OriginY + t.Y * Cell + 2, 12, 12);

            if (v == Vis.Dark)
            {
                if (t.Exposed) DrawGlint(batch, canvas, r);
                continue;
            }

            var floor = v switch
            {
                Vis.Known => Palette.DarkGrey,
                Vis.Near => Palette.DarkBlue,
                _ => Palette.Black
            };
            canvas.Fill(batch, r, floor);
            if (v == Vis.Far) canvas.Outline(batch, r, Palette.DarkBlue);

            // corridor stubs (two halves meet in the 4px gap between cells)
            var corridor = v == Vis.Known ? Palette.DarkGrey : Palette.DarkBlue;
            if (t.Has(Dir.N)) canvas.Fill(batch, r.X + 4, r.Y - 2, 4, 2, corridor);
            if (t.Has(Dir.S)) canvas.Fill(batch, r.X + 4, r.Bottom, 4, 2, corridor);
            if (t.Has(Dir.W)) canvas.Fill(batch, r.X - 2, r.Y + 4, 2, 4, corridor);
            if (t.Has(Dir.E)) canvas.Fill(batch, r.Right, r.Y + 4, 2, 4, corridor);

            var kind = Visibility.VisibleKind(t, v);
            if (kind is null)
                canvas.Fill(batch, r.X + 5, r.Y + 5, 2, 2, Palette.Lavender); // unknown
            else
                DrawIcon(batch, canvas, r, kind.Value);
        }

        // party marker (blinks)
        if ((int)(_time * 3) % 3 != 0)
        {
            var pr = new Rectangle(OriginX + _party.X * Cell + 4, OriginY + _party.Y * Cell + 4, 8, 8);
            canvas.Outline(batch, pr, Palette.White);
            canvas.Fill(batch, pr.X + 3, pr.Y + 3, 2, 2, Palette.White);
        }
    }

    private void DrawIcon(SpriteBatch batch, PixelCanvas canvas, Rectangle r, TileKind kind)
    {
        Color? c = kind switch
        {
            TileKind.Treasure => Palette.Yellow,
            TileKind.Encounter => Palette.Red,
            TileKind.Elite => Palette.DarkPurple,
            TileKind.Trap => Palette.Orange,
            TileKind.Event => Palette.Lavender,
            TileKind.Campfire => Palette.Orange,
            TileKind.Merchant => Palette.Green,
            TileKind.Boss => Palette.Pink,
            TileKind.Entrance => Palette.Blue,
            _ => null
        };
        if (c is null) return;

        switch (kind)
        {
            case TileKind.Boss:
                bool pulse = (int)(_time * 2) % 2 == 0;
                canvas.Fill(batch, r.X + 1, r.Y + 1, 10, 10, pulse ? Palette.Pink : Palette.DarkPurple);
                canvas.Fill(batch, r.X + 3, r.Y + 4, 2, 2, Palette.Black); // skull eyes
                canvas.Fill(batch, r.X + 7, r.Y + 4, 2, 2, Palette.Black);
                break;
            case TileKind.Campfire:
                bool flicker = (int)(_time * 6) % 2 == 0;
                canvas.Fill(batch, r.X + 4, r.Y + 8, 4, 2, Palette.Brown);
                canvas.Fill(batch, r.X + 5, r.Y + (flicker ? 3 : 4), 2, 5, c.Value);
                break;
            case TileKind.Elite:
                canvas.Fill(batch, r.X + 2, r.Y + 2, 8, 8, c.Value);
                canvas.Fill(batch, r.X + 4, r.Y + 4, 4, 4, Palette.Red);
                break;
            case TileKind.Treasure:
                canvas.Fill(batch, r.X + 3, r.Y + 4, 6, 5, Palette.Brown);
                canvas.Fill(batch, r.X + 3, r.Y + 6, 6, 1, c.Value);
                break;
            default:
                canvas.Fill(batch, r.X + 3, r.Y + 3, 6, 6, c.Value);
                break;
        }
    }

    private void DrawGlint(SpriteBatch batch, PixelCanvas canvas, Rectangle r)
    {
        bool on = (int)(_time * 2 + r.X) % 2 == 0;
        var c = on ? Palette.Yellow : Palette.Brown;
        canvas.Fill(batch, r.X + 5, r.Y + 3, 2, 6, c);
        canvas.Fill(batch, r.X + 3, r.Y + 5, 6, 2, c);
    }

    private void DrawPanel(SpriteBatch batch, PixelCanvas canvas)
    {
        int x = PanelX, y = OriginY;

        // Torch meter
        TinyFont.DrawShadowed(batch, canvas, $"TORCH {_torch}", x, y, Palette.Orange);
        y += 8;
        int max = Math.Max(_grid.Torch, _torch);
        canvas.Fill(batch, x, y, max * 4 + 2, 6, Palette.DarkBlue);
        canvas.Fill(batch, x + 1, y + 1, _torch * 4, 4, _torch > 3 ? Palette.Orange : Palette.Red);
        y += 12;

        TinyFont.DrawShadowed(batch, canvas, $"GOLD {_gold}", x, y, Palette.Yellow);
        y += 10;

        // Shared odds demo — same function the combat UI will use
        var strike = D20.Odds(5, 13);
        var blessed = D20.Odds(5, 13, RollMode.Advantage);
        TinyFont.DrawShadowed(batch, canvas, "STRIKE +5 VS AC 13", x, y, Palette.LightGrey);
        y += 7;
        TinyFont.Draw(batch, canvas, $"HIT {strike}   HIDDEN {blessed}", x, y, Palette.White);
        y += 14;

        // Legend
        (string Label, TileKind Kind)[] legend =
        {
            ("CHEST", TileKind.Treasure), ("FIGHT", TileKind.Encounter), ("ELITE", TileKind.Elite),
            ("TRAP", TileKind.Trap), ("EVENT", TileKind.Event), ("CAMP", TileKind.Campfire),
            ("SHOP", TileKind.Merchant), ("BOSS", TileKind.Boss)
        };
        for (int i = 0; i < legend.Length; i++)
        {
            int lx = x + (i % 2) * 60, ly = y + (i / 2) * 14;
            DrawIcon(batch, canvas, new Rectangle(lx - 2, ly - 3, 12, 12), legend[i].Kind);
            TinyFont.Draw(batch, canvas, legend[i].Label, lx + 12, ly + 1, Palette.LightGrey);
        }
        y += 4 * 14 + 4;

        // Message line
        TinyFont.DrawShadowed(batch, canvas, _message, x, y, Palette.Peach);

        // Controls
        TinyFont.Draw(batch, canvas, "ARROWS/CLICK MOVE  R NEW  F11 FULL", x, PixelCanvas.Height - 18, Palette.DarkGrey);
        TinyFont.Draw(batch, canvas, $"SEED {_seed % 100000}", x, PixelCanvas.Height - 10, Palette.DarkGrey);
    }
}
