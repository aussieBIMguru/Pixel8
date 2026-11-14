using Pixel8.Core.Combat;
using Pixel8.Core.Content;
using Pixel8.Core.Dice;
using Pixel8.Core.Dungeon;
using Pixel8.Core.Heroes;
using Pixel8.Core.Randomness;
using Pixel8.Core.Runs;
using Pixel8.Sim;

// Usage:
//   dotnet run --project tools/Pixel8.Sim -- [seed] [dungeons-to-sample]   odds table, ASCII dungeon, generator stats
//   dotnet run --project tools/Pixel8.Sim -- fight [seed] [fights] [difficulty]   one logged fight, win rates, timed-reaction skill table, Depth 1 clear rate per difficulty
//   dotnet run -c Release --project tools/Pixel8.Sim -- run [options]   round 9: whole runs on real dungeons, every difficulty, with targets
//       --runs 400            runs per difficulty (and per sweep combination)
//       --seed 1              first seed
//       --difficulty all      or easy / normal / hard
//       --party mixed         mixed = 12 three-class parties in turn (each of the 8 classes in several); or e.g. fighter,rogue,wizard
//       --set path=value      a fixed data override, e.g. --set enemies.young_green_dragon.hpScale=0.5
//       --sweep path=a,b,c    try each value (several --sweep flags = every combination), e.g. --sweep acCapByDepth=14/15/16,13/14/15
//       --threads 0           0 = all cores
// The AutoPlayer spends Luck on failed attacks and grades timed reactions with AutoPlayer.TimingSkill.
if (args.Length > 0 && args[0].Equals("fight", StringComparison.OrdinalIgnoreCase))
{
    RunFights(args.Length > 1 ? ulong.Parse(args[1]) : 1, args.Length > 2 ? int.Parse(args[2]) : 2000, args.Length > 3 ? args[3] : null);
    return;
}
if (args.Length > 0 && args[0].Equals("run", StringComparison.OrdinalIgnoreCase))
{
    RunSim.Execute(args[1..]);
    return;
}

ulong seed = args.Length > 0 ? ulong.Parse(args[0]) : 1;
int samples = args.Length > 1 ? int.Parse(args[1]) : 2000;

Console.WriteLine("=== Hit chance table (d20 + mod vs AC) ===");
Console.Write("mod\\AC");
for (int ac = 10; ac <= 18; ac++) Console.Write($"{ac,6}");
Console.WriteLine();
for (int mod = 2; mod <= 8; mod++)
{
    Console.Write($"  +{mod,-4}");
    for (int ac = 10; ac <= 18; ac++) Console.Write($"{D20.Odds(mod, ac).Percent,5}%");
    Console.WriteLine();
}

Console.WriteLine();
Console.WriteLine($"=== Sample dungeon (seed {seed}) ===");
var g = DungeonGenerator.Generate(seed);
PrintAscii(g);

Console.WriteLine();
Console.WriteLine($"=== Generator stats over {samples} dungeons ===");
var stats = new Dictionary<TileKind, double>();
double bossDist = 0, torch = 0, deadEnds = 0;
for (ulong s = 1; s <= (ulong)samples; s++)
{
    var d = DungeonGenerator.Generate(s);
    foreach (var t in d.Rooms)
        stats[t.Kind] = stats.GetValueOrDefault(t.Kind) + 1;
    bossDist += d.Boss.Distance;
    torch += d.Torch;
    deadEnds += d.Rooms.Count(t => t.Degree == 1);
}
foreach (var (kind, count) in stats.OrderBy(k => k.Key))
    Console.WriteLine($"  {kind,-10} {count / samples,6:0.0} per depth");
Console.WriteLine($"  Boss distance avg {bossDist / samples:0.0} | Torch avg {torch / samples:0.0} | Dead ends avg {deadEnds / samples:0.0}");

static void PrintAscii(DungeonGrid g)
{
    // Each cell is 3 chars wide; links drawn between cells.
    for (int y = 0; y < g.Height; y++)
    {
        var row = new System.Text.StringBuilder();
        var below = new System.Text.StringBuilder();
        for (int x = 0; x < g.Width; x++)
        {
            var t = g[x, y];
            row.Append(t.IsRoom ? $"[{Glyph(t)}]" : "   ");
            row.Append(t.Has(Dir.E) ? "-" : " ");
            below.Append(t.Has(Dir.S) ? " | " : "   ");
            below.Append(' ');
        }
        Console.WriteLine(row);
        Console.WriteLine(below);
    }
    Console.WriteLine("  @ entrance  B boss  $ treasure (* = exposed)  x fight  X elite  ! trap  ? event  c camp  m shop");
}

static char Glyph(Tile t) => t.Kind switch
{
    TileKind.Entrance => '@',
    TileKind.Boss => 'B',
    TileKind.Treasure => t.Exposed ? '*' : '$',
    TileKind.Encounter => 'x',
    TileKind.Elite => 'X',
    TileKind.Trap => '!',
    TileKind.Event => '?',
    TileKind.Campfire => 'c',
    TileKind.Merchant => 'm',
    _ => ' '
};

static void RunFights(ulong seed, int fights, string? difficultyId)
{
    var content = ContentDb.Load(ContentDb.FindDataDirectory());
    var difficulty = content.Balance.DifficultyOf(difficultyId);
    Console.WriteLine($"Difficulty for the matchup table: {difficulty.Name}");

    Console.WriteLine($"=== Logged fight (seed {seed}): Fighter vs Goblin ===");
    var log = Fight(content, seed, new[] { "fighter" }, new[] { "goblin" }, CombatKind.Normal, difficulty);
    foreach (var e in log.Events) Console.WriteLine(GameEventText.Describe(e));

    Console.WriteLine();
    Console.WriteLine($"=== Win rates over {fights} fights (greedy AutoPlayer) ===");
    Console.WriteLine($"{"Matchup",-46}{"Win%",6}{"Rounds",8}{"HP lost",9}{"Deaths",8}");
    var party = new[] { "fighter", "rogue", "wizard" };
    var matchups = new (string Name, string[] Heroes, string[] Enemies, CombatKind Kind)[]
    {
        ("Fighter vs Goblin", new[] { "fighter" }, new[] { "goblin" }, CombatKind.Normal),
        ("Wizard vs Goblin", new[] { "wizard" }, new[] { "goblin" }, CombatKind.Normal),
        ("F/R/W vs 3 Goblins", party, new[] { "goblin", "goblin", "goblin" }, CombatKind.Normal),
        ("F/R/W vs 2 Orcs", party, new[] { "orc", "orc" }, CombatKind.Normal),
        ("F/R/W vs Bugbear + 2 Kobolds (elite)", party, new[] { "bugbear", "kobold", "kobold" }, CombatKind.Elite),
        ("F/R/C vs Ogre (boss)", new[] { "fighter", "rogue", "cleric" }, new[] { "ogre" }, CombatKind.Boss),
    };
    foreach (var m in matchups)
    {
        int wins = 0, rounds = 0, hpLost = 0, deaths = 0;
        for (int i = 0; i < fights; i++)
        {
            var c = Fight(content, seed + (ulong)i, m.Heroes, m.Enemies, m.Kind, difficulty);
            if (c.Phase == CombatPhase.Victory) wins++;
            rounds += c.Round;
            hpLost += c.Heroes.Sum(h => h.MaxHp - h.Hp);
            deaths += c.Heroes.Count(h => h.IsDead);
        }
        Console.WriteLine($"{m.Name,-46}{100.0 * wins / fights,5:0.0}%{(double)rounds / fights,8:0.0}{(double)hpLost / fights,9:0.0}{(double)deaths / fights,8:0.00}");
    }

    RunTimingTable(content, seed, fights, difficulty);
    RunDepthPaths(content, seed, Math.Max(200, fights / 2));
}

/// <summary>How much timed reactions on big attacks matter, by modelled player skill.</summary>
static void RunTimingTable(ContentDb content, ulong seed, int fights, DifficultyDef difficulty)
{
    var saved = AutoPlayer.TimingSkill;
    var profiles = new (string Name, (int, int, int, int) Skill)[]
    {
        ("Always miss", (1, 0, 0, 0)),
        ($"Average {saved.Miss}/{saved.Ok}/{saved.Good}/{saved.Perfect}", saved),
        ("Always Ok (timing off)", (0, 1, 0, 0)),
        ("Always perfect", (0, 0, 0, 1)),
    };
    var party = new[] { "fighter", "rogue", "cleric" };
    var matchups = new (string Name, string[] Enemies, CombatKind Kind)[]
    {
        ("Ogre (boss)", new[] { "ogre" }, CombatKind.Boss),
        ("Bugbear + 2 Kobolds (elite)", new[] { "bugbear", "kobold", "kobold" }, CombatKind.Elite),
    };

    Console.WriteLine();
    Console.WriteLine($"=== Timed reactions (big attacks), F/R/C, {fights} fights each. Grades Miss/Ok/Good/Perfect ===");
    Console.WriteLine($"{"Skill",-30}" + string.Concat(matchups.Select(m => $"{m.Name + " win%",30}{"HP lost",9}")));
    foreach (var (name, skill) in profiles)
    {
        AutoPlayer.TimingSkill = skill;
        Console.Write($"{name,-30}");
        foreach (var m in matchups)
        {
            int wins = 0, hpLost = 0;
            for (int i = 0; i < fights; i++)
            {
                var c = Fight(content, seed + (ulong)i, party, m.Enemies, m.Kind, difficulty);
                if (c.Phase == CombatPhase.Victory) wins++;
                hpLost += c.Heroes.Sum(h => h.MaxHp - h.Hp);
            }
            Console.Write($"{100.0 * wins / fights,29:0.0}%{(double)hpLost / fights,9:0.0}");
        }
        Console.WriteLine();
    }
    AutoPlayer.TimingSkill = saved;
}

static CombatState Fight(ContentDb content, ulong seed, string[] heroes, string[] enemies, CombatKind kind, DifficultyDef difficulty)
{
    var root = new Rng(seed);
    var party = Party.Create(content, heroes, root.Fork("party"), difficulty);
    party.StartDepth(20);
    var combat = new CombatState(party, enemies.Select(id => Encounters.Spawn(content, id, null, difficulty)),
        content, root.Fork("combat"), kind, difficulty);
    AutoPlayer.RunToEnd(combat);
    return combat;
}

/// <summary>
/// A typical Depth 1 through the real Run API: 2 fights, elite, campfire, fight, boss.
/// The AutoPlayer drafts the first card offered after each win and takes +HP on level-ups.
/// Prints how far parties get per difficulty.
/// </summary>
static void RunDepthPaths(ContentDb content, ulong seed, int runs)
{
    var steps = new[] { "Fight", "Fight", "Elite", "Camp", "Fight", "Boss" };
    Console.WriteLine();
    Console.WriteLine($"=== Depth 1 path over {runs} runs (F/R/W, greedy AutoPlayer, drafts first offer) ===");
    Console.WriteLine($"{"Difficulty",-12}" + string.Concat(steps.Select(s => $"{s,8}")) + $"{"HP left",9}");
    foreach (var id in content.Balance.Difficulties.Keys)
    {
        var reached = new int[steps.Length];
        double hpLeft = 0;
        int cleared = 0, levels = 0;
        for (int i = 0; i < runs; i++)
        {
            var run = new Run(content, seed + (ulong)i, new[] { "fighter", "rogue", "wizard" }, id);
            run.Party.StartDepth(20);
            bool alive = true;
            for (int s = 0; s < steps.Length && alive; s++)
            {
                if (steps[s] == "Camp")
                {
                    run.Rest();
                    reached[s]++;
                    continue;
                }
                var kind = steps[s] switch { "Elite" => CombatKind.Elite, "Boss" => CombatKind.Boss, _ => CombatKind.Normal };
                var combat = run.StartCombat(kind);
                if (AutoPlayer.RunToEnd(combat) != CombatPhase.Victory)
                {
                    alive = false;
                    break;
                }
                reached[s]++;
                run.AwardXp(combat.XpEarned);
                AutoPlayer.ResolveLevelUps(run);
                var draft = run.CreateDraft();
                if (draft.Offers.Count > 0) draft.Choose(draft.Offers[0], run.LootRng);
            }
            if (alive)
            {
                levels += run.Party.Level;
                cleared++;
                hpLeft += run.Party.Heroes.Sum(h => h.Hp) / (double)run.Party.Heroes.Sum(h => h.MaxHp);
            }
        }
        Console.WriteLine($"{content.Balance.DifficultyOf(id).Name,-12}" +
            string.Concat(reached.Select(r => $"{100.0 * r / runs,7:0}%")) +
            $"{(cleared == 0 ? 0 : 100 * hpLeft / cleared),8:0}%" +
            $"   avg level at boss kill {(cleared == 0 ? 0 : (double)levels / cleared):0.0}");
    }
}
