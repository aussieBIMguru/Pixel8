# Pixel8

A pixel-art dungeon-crawling deckbuilder: a party of 3 heroes, a hidden tile grid, and d20 checks with visible odds.
The full design lives in the **Pixel8 – Game Design Document**.

## Open & run (Visual Studio 2022 17.8+, .NET 8 SDK)

1. Open `Pixel8.sln`.
2. Set **Pixel8.Desktop** as the startup project and press F5. NuGet restores MonoGame automatically.
3. **Title screen:** ↑/↓ to pick, ←/→ to change. Continue (when there's a saved run), Start/New game, Difficulty (Easy / Normal / Hard carousel), Shop (on map / at campfires / anytime), Sound on/off, React events (timed dodges) on/off, About, Quit. Options are saved to `%LocalAppData%/Pixel8/settings.json`; the run auto-saves to `save.json` there whenever you're on the map (a fight in progress isn't saved: Continue puts you back before you stepped onto that tile).
4. Pick 3 heroes (click or 1–4), then Enter (Esc goes back). On the map: arrows/WASD or click an adjacent room · **P** drink a potion · **M** shop (Shop: anytime) · **R** reroll the Depth · **F11** fullscreen · **Esc** back to the title. "?" rooms are events; the bar at the bottom of the panel is party XP.
5. In combat: click a card (or 1–5), then a target (or 1–4) · **S** Strike · **G** Guard · **C** class action (Second Wind / Cunning / Fire Bolt / Spare the Dying) · **B** use a Bag item (Quick) · **E** end turn · **F** flee · **Esc**/right-click cancels · **Y/N** for reactions and Luck rerolls (one reroll per fight) · **Space** (or click) in the zone when a ⚡ big attack lands · hold Space or the mouse to speed up animations and dice.
6. Campfires: **1** Rest (heal ⅓, restock) or **2** Upgrade a card (**3** Shop when the shop lives at campfires). Merchants: **1–3** buy a card, click potions, card removal or upgrade · **Esc** leave.
7. Level-ups (party XP from fights): each hero picks **1** +HP, **2** upgrade a card or **3** remove a card. With the torch out (Torch 0), heroes attack with disadvantage until a campfire.

Command line:

```
dotnet test                                   # rules + generator tests
dotnet run --project tools/Pixel8.Sim -- 42         # odds table, ASCII dungeon for seed 42, generator stats
dotnet run --project tools/Pixel8.Sim -- fight 1    # logged fight, win rates, Depth 1 clear rate per difficulty
dotnet run --project tools/Pixel8.Sim -- fight 1 2000 hard   # same, matchup table on Hard
dotnet run --project src/Pixel8.Desktop
```

## Layout

| Project | What |
|---|---|
| `src/Pixel8.Core` | All rules. No MonoGame references; deterministic via the seeded `Rng`. `Content/` JSON loader, `Heroes/` Hero + Party, `Effects/` effect registry, `Combat/` CombatState + event log, `Rewards/` card draft. |
| `src/Pixel8.Desktop` | MonoGame DesktopGL app: 256×144 canvas (32×18 tiles of 8×8), integer scaling, 16-colour PICO-8 palette, 8×8 sprites defined in code (`Rendering/Sprites.cs`), 3×5 font. `Screens/`: party select, map, combat, draft, message. `Ui/`: input + drawing helpers. |
| `tests/Pixel8.Tests` | xUnit: d20 odds, generator invariants, deck rules, content validation, combat (termination, determinism, Block, Downed, Shield reaction, Marked, Taunt, Flee), card draft, difficulty, roll breakdowns, Luck, timing grades, upgrades, merchant. |
| `tools/Pixel8.Sim` | Balance console. It becomes the Monte Carlo run simulator in M5. |
| `data/` | `cards` (with `upgrade` blocks and class basics), `heroes` (`basicAction`), `events` (map events), `enemies` (5e SRD 5.1, `big` attacks), `items`, `balance` (difficulty presets with `timingWindow`, merchant `prices`) JSON, loaded and validated by `ContentDb`. |

## Status

- **M0 Scaffold:** done.
- **M2 Dungeon:** walkable prototype with fog of war and the Torch meter, now in the 8×8 pixel style. Tiles resolve as placeholder flashes for now.
- **M1 Core rules:** dice, odds, decks, Hero/Party, JSON content, effects, `CombatState` (initiative, intents with odds, Action/Quick/Reaction, reaction prompts, conditions, Downed + death saves, flee), card draft, `Run` + `Encounters`.
- **M3 Combat screen:** playable loop: party select → map → combat (event-log playback, hand, targeting with live odds, intents, Shield prompt, flee) → card draft → boss → next Depth → victory or defeat. Campfires heal a third and restock; traps roll a save.
- **M4 (session 5):** title screen with options, dice roll strip (every d20 and damage roll animated with labelled modifiers), Luck rerolls, timed reactions on big attacks, campfire Rest/Upgrade, merchant (cards, potions, card removal, upgrades), chiptune sound effects, pixel "8" app icon.
- **Session 6:** torch-out darkness, class basic actions, potions in combat, 7 map events (`data/events.json`), party XP and level-ups, a merchant on every Depth plus the Shop option, attack lunge and spell sparkle animations.
- **Session 7:** chunky 8-bit hit and spell effects (slash, smash, stab, bite, arrows, fire bolt, fire, magic missiles, radiant beam, heal, shield, death poof, crit burst; picked by `fx` in the data), hero attack/cast frames, save/load with Continue, slightly faster levels.
- **Next:** events that start fights, music, more enemies and cards.

Combat rules locked 2026-11-13: no formation; exposed treasure is never a mimic; after a fight, 3 cards (one per hero), pick one or skip for gold; flee Normal fights for Torch + gold.

## Releasing (installer)

Same routine as Throng. Needs the .NET 8 SDK and [Inno Setup 6](https://jrsoftware.org/isinfo.php) (a build tool; not shipped).

1. Set the version in `Directory.Build.props` (`<Version>`, currently **0.1.0**). That is the only place it lives.
2. Close Visual Studio and double-click `publish.bat` (or `powershell -ExecutionPolicy Bypass -File build\publish.ps1`).
   It runs the tests, publishes Pixel8.Desktop self-contained for win-x64 to `artifacts\app\` and builds
   `artifacts\Pixel8-Setup-<version>.exe` plus its SHA-256. Switches: `-SkipTests`, `-InnoCompiler <path to ISCC.exe>`,
   `-Sign -CertThumbprint <thumbprint>` (signing is off for now).

The setup installs per user (no admin) to `%LocalAppData%\Programs\Pixel8` and adds a Start menu shortcut (desktop
optional). Uninstalling keeps your settings and saved run (`%LocalAppData%\Pixel8`). Unsigned builds show a
SmartScreen warning the first time (More info ▸ Run anyway). Add an entry to [CHANGELOG.md](CHANGELOG.md), then tag the
commit (`v0.1.0`) and attach the setup and its `.sha256.txt` to a GitHub release. `clean.bat` removes all bin/obj folders.

Credits, licences and inspirations are listed in [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md). Spotted a problem or a possible IP issue? Please open a GitHub issue.

> Note: the app project is named `Pixel8.Desktop`, not `Pixel8.Game`, so its namespace doesn't clash with MonoGame's `Game` class.
