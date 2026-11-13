# Pixel8

A pixel-art dungeon-crawling deckbuilder: a party of 3 heroes, a hidden tile grid, and d20 checks with visible odds.
The full design lives in the **Pixel8 – Game Design Document**.

## Open & run (Visual Studio 2022 17.8+, .NET 8 SDK)

1. Open `Pixel8.sln`.
2. Set **Pixel8.Desktop** as the startup project and press F5. NuGet restores MonoGame automatically.
3. Controls: arrows, WASD or click an adjacent tile to move · **R** new dungeon · **F11** fullscreen · **Esc** quit.

Command line:

```
dotnet test                                   # rules + generator tests
dotnet run --project tools/Pixel8.Sim -- 42   # odds table, ASCII dungeon for seed 42, generator stats
dotnet run --project src/Pixel8.Desktop
```

## Layout

| Project | What |
|---|---|
| `src/Pixel8.Core` | All rules. No MonoGame references; deterministic via the seeded `Rng`. |
| `src/Pixel8.Desktop` | MonoGame DesktopGL app: 320×180 canvas, integer scaling, built-in 3×5 font, map screen. |
| `tests/Pixel8.Tests` | xUnit: d20 odds vs exhaustive enumeration, generator invariants over 200 seeds, deck rules. |
| `tools/Pixel8.Sim` | Balance console. It becomes the Monte Carlo run simulator in M5. |
| `data/` | Card JSON, wired into the content loader in M1. |

## Status

- **M0 Scaffold:** done.
- **M2 Dungeon:** walkable prototype with fog of war and the Torch meter. Tiles resolve as placeholder flashes for now.
- **M1 Core rules:** dice, odds and decks are done. Next up: `CombatState`, `EffectRegistry` and the JSON content loader.

> Note: the app project is named `Pixel8.Desktop`, not `Pixel8.Game`, so its namespace doesn't clash with MonoGame's `Game` class.
