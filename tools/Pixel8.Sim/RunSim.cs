using System.Collections.Concurrent;
using System.Diagnostics;
using Pixel8.Core.Combat;
using Pixel8.Core.Content;

namespace Pixel8.Sim;

/// <summary>
/// Round 9 "run" mode: whole runs on real dungeons per difficulty, with the clear-rate and fight-length targets,
/// optionally swept over data overrides. One table row per (combination, difficulty).
/// </summary>
public static class RunSim
{
    // Targets (design-decisions.md, round 9).
    // Round 12 targets for 5 Depths: % of runs clearing Depth 1 / Depth 3 / all five.
    private static readonly Dictionary<string, (int Depth1, int Depth3, int Run)> ClearTargets = new()
    {
        ["easy"] = (99, 90, 70),
        ["normal"] = (95, 75, 40),
        ["hard"] = (80, 40, 12),
    };
    // Round 12: boss reach-and-beat win% on Normal (+-5), and boss rounds (Lich 8-10, others 6-8).
    private static readonly (string Name, int Win)[] BossTargets =
    {
        ("ogre", 97), ("owlbear", 93), ("dragon", 85), ("hydra", 80), ("lich", 70),
    };
    private static readonly Dictionary<CombatKind, (double Min, double Max)> RoundTargets = new()
    {
        [CombatKind.Normal] = (3, 4),
        [CombatKind.Elite] = (5, 6),
        [CombatKind.Boss] = (6, 8),
    };

    /// <summary>Round 11: eight three-class parties, so every class appears three times (each with a different pair).</summary>
    private static readonly string[][] MixedParties =
    {
        new[] { "fighter", "rogue", "wizard" },
        new[] { "fighter", "rogue", "cleric" },
        new[] { "fighter", "wizard", "cleric" },
        new[] { "rogue", "wizard", "cleric" },
        new[] { "bard", "ranger", "paladin" },
        new[] { "druid", "ranger", "paladin" },
        new[] { "bard", "druid", "fighter" },
        new[] { "paladin", "druid", "bard" },
        new[] { "ranger", "bard", "rogue" },
        new[] { "druid", "wizard", "paladin" },
        new[] { "ranger", "cleric", "bard" },
        new[] { "paladin", "rogue", "druid" },
    };

    public static void Execute(string[] args)
    {
        int runs = 400, threads = 0;
        ulong seed = 1;
        string difficulty = "all", partyArg = "mixed";
        var fixedPatches = new List<DataPatch>();
        var sweeps = new List<(string Path, string[] Values)>();

        for (int i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
            switch (args[i].ToLowerInvariant())
            {
                case "--runs": runs = int.Parse(Next()); break;
                case "--seed": seed = ulong.Parse(Next()); break;
                case "--difficulty": difficulty = Next().ToLowerInvariant(); break;
                case "--party": partyArg = Next().ToLowerInvariant(); break;
                case "--threads": threads = int.Parse(Next()); break;
                case "--set":
                {
                    var (path, value) = SplitAssignment(Next());
                    fixedPatches.Add(DataPatch.Parse(path, value));
                    break;
                }
                case "--sweep":
                {
                    var (path, values) = SplitAssignment(Next());
                    sweeps.Add((path, values.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
                    break;
                }
                default: throw new ArgumentException($"Unknown option '{args[i]}'. See the usage at the top of Program.cs.");
            }
        }

        string source = ContentDb.FindDataDirectory();
        var parties = partyArg == "mixed" ? MixedParties : new[] { partyArg.Split(',', StringSplitOptions.TrimEntries) };
        var combos = Combinations(sweeps).ToList();
        if (combos.Count > 64) throw new ArgumentException($"{combos.Count} sweep combinations is too many (max 64).");

        var baseContent = ContentDb.Load(source);
        var difficulties = difficulty == "all"
            ? baseContent.Balance.Difficulties.Keys.ToList()
            : new List<string> { difficulty };

        Console.WriteLine($"=== Run sim: {runs} runs x {difficulties.Count} difficulties x {combos.Count} combination(s), party {partyArg}, seed {seed} ===");
        if (fixedPatches.Count > 0) Console.WriteLine("Fixed: " + string.Join("  ", fixedPatches.Select(p => p.Label)));
        Console.WriteLine("Targets (round 12): clear D1/D3/run Easy 99/90/70, Normal 95/75/40, Hard 80/40/12 (+-5/+-5/+-4). Rounds Normal 3-4, Elite 5-6, Boss 6-8 (judged on Normal).");
        Console.WriteLine("Bosses on Normal: Ogre 97, Owlbear 93, Dragon 85, Hydra 80, Lich 70% (+-5), Lich 8-10 rounds. Classes: within +-5 of the Normal average.");
        Console.WriteLine("Columns: D1/D3/RUN = % of runs that cleared Depth 1, Depth 3, all five. N/E/B RD = average rounds per normal/elite/boss fight.");
        Console.WriteLine("         BASIC = share of hero Actions spent on Strike or Fire Bolt. RRM = Rearms per fight. LVL = party level at the end. '*' = on target.");
        Console.WriteLine();
        Console.WriteLine($"{"Combination",-44}{"Diff",-8}{"D1",6}{"D3",6}{"RUN",6}{"N RD",7}{"E RD",7}{"B RD",7}{"BASIC",7}{"RRM",6}{"LVL",6}  Top deaths");

        var watch = Stopwatch.StartNew();
        var bossRows = new List<string>();
        var gearRows = new List<string>();
        var classRows = new List<string>();
        foreach (var combo in combos)
        {
            var patches = fixedPatches.Concat(combo).ToList();
            string dir = patches.Count == 0 ? source : DataFolder.Patched(source, patches);
            try
            {
                var content = patches.Count == 0 ? baseContent : ContentDb.Load(dir);
                string label = combo.Count == 0 ? "(data as is)" : string.Join(" ", combo.Select(p => p.Label));
                foreach (var diff in difficulties)
                {
                    var logs = Simulate(content, diff, parties, runs, seed, threads);
                    Console.WriteLine(Row(label, content.Balance.DifficultyOf(diff).Name, diff, logs));
                    bossRows.AddRange(BossRows(label, content.Balance.DifficultyOf(diff).Name, logs));
                    gearRows.Add(GearRow(label, content.Balance.DifficultyOf(diff).Name, logs));
                    classRows.Add(ClassRow(label, content.Balance.DifficultyOf(diff).Name, logs));
                }
            }
            finally
            {
                if (dir != source) DataFolder.Delete(dir);
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{"Bosses: combination",-44}{"Diff",-8}{"Boss",-22}{"Fights",8}{"Win%",8}{"Rounds",8}");
        foreach (var r in bossRows) Console.WriteLine(r);
        Console.WriteLine();
        Console.WriteLine($"{"Gear: combination",-44}{"Diff",-8}{"Found/run",10}{"Mimics/run",11}{"Rares worn",11}{"Armed%",8}");
        foreach (var r in gearRows) Console.WriteLine(r);
        Console.WriteLine();
        Console.WriteLine($"{"Classes (run win% / avg Depths cleared)",-44}{"Diff",-8}" + string.Join("", ClassIds.Select(c => $"{c.ToUpperInvariant(),11}")));
        foreach (var r in classRows) Console.WriteLine(r);
        Console.WriteLine();
        if (!Explorer.Stalemates.IsEmpty)
        {
            Console.WriteLine($"Stalemates (fights that hit the decision cap): {Explorer.Stalemates.Count}. First few:");
            foreach (var s in Explorer.Stalemates.Take(6)) Console.WriteLine("  " + s);
            Console.WriteLine();
        }
        Console.WriteLine($"Done in {watch.Elapsed.TotalSeconds:0.0}s.");
    }

    private static (string, string) SplitAssignment(string s)
    {
        int eq = s.IndexOf('=');
        if (eq <= 0) throw new ArgumentException($"Expected path=value, got '{s}'.");
        return (s[..eq].Trim(), s[(eq + 1)..].Trim());
    }

    private static IEnumerable<List<DataPatch>> Combinations(List<(string Path, string[] Values)> sweeps)
    {
        IEnumerable<List<DataPatch>> acc = new[] { new List<DataPatch>() };
        foreach (var (path, values) in sweeps)
            acc = acc.SelectMany(list => values.Select(v => new List<DataPatch>(list) { DataPatch.Parse(path, v) })).ToList();
        return acc;
    }

    private static List<RunLog> Simulate(ContentDb content, string difficulty, string[][] parties, int runs, ulong seed, int threads)
    {
        var logs = new ConcurrentBag<(int, RunLog)>();
        var options = new ParallelOptions { MaxDegreeOfParallelism = threads > 0 ? threads : Environment.ProcessorCount };
        Parallel.For(0, runs, options, i =>
        {
            var party = parties[i % parties.Length];
            var log = new Explorer(content, seed + (ulong)i, difficulty, party).Play();
            logs.Add((i, log));
        });
        return logs.OrderBy(x => x.Item1).Select(x => x.Item2).ToList();
    }

    private static string Row(string label, string name, string diffId, List<RunLog> logs)
    {
        int n = Math.Max(1, logs.Count);
        double d1 = 100.0 * logs.Count(l => l.DepthsCleared >= 1) / n;
        double d2 = 100.0 * logs.Count(l => l.DepthsCleared >= 3) / n;
        double won = 100.0 * logs.Count(l => l.Won) / n;
        var fights = logs.SelectMany(l => l.Fights).ToList();
        double Rounds(CombatKind k)
        {
            var f = fights.Where(x => x.Kind == k && x.Won).ToList();
            return f.Count == 0 ? 0 : f.Average(x => x.Rounds);
        }
        int actions = fights.Sum(f => f.HeroActions);
        double basic = actions == 0 ? 0 : 100.0 * fights.Sum(f => f.BasicAttacks) / actions;
        double rearms = fights.Count == 0 ? 0 : (double)fights.Sum(f => f.Rearms) / fights.Count;
        double level = logs.Average(l => l.Level);
        string deaths = string.Join(", ", logs.Where(l => !l.Won).GroupBy(l => l.DeathCause)
            .OrderByDescending(g => g.Count()).Take(3).Select(g => $"{g.Key} {100.0 * g.Count() / n:0}%"));

        bool judgeRounds = diffId == "normal";
        string Clear(double v, int? target, int tol) => $"{v,5:0}{(target is int t && Math.Abs(v - t) <= tol ? "*" : " ")}";
        string Rd(CombatKind k)
        {
            double v = Rounds(k);
            var (lo, hi) = RoundTargets[k];
            return $"{v,6:0.0}{(judgeRounds && v >= lo && v <= hi ? "*" : " ")}";
        }
        var target = ClearTargets.TryGetValue(diffId, out var ct) ? ct : ((int, int, int)?)null;
        string cut = label.Length > 43 ? label[..40] + "..." : label;
        return $"{cut,-44}{name,-8}{Clear(d1, target?.Item1, 5)}{Clear(d2, target?.Item2, 5)}{Clear(won, target?.Item3, 4)}" +
               $"{Rd(CombatKind.Normal)}{Rd(CombatKind.Elite)}{Rd(CombatKind.Boss)}{basic,6:0}%{rearms,6:0.0}{level,6:0.0}  {deaths}";
    }

    private static readonly string[] ClassIds = { "fighter", "rogue", "wizard", "cleric", "bard", "ranger", "druid", "paladin" };

    /// <summary>Round 11: for each class, the win% and average Depths cleared of the runs it was in.</summary>
    private static string ClassRow(string label, string name, List<RunLog> logs)
    {
        string cut = label.Length > 43 ? label[..40] + "..." : label;
        bool judge = name.Equals("Normal", StringComparison.OrdinalIgnoreCase);
        double avg = 100.0 * logs.Count(l => l.Won) / Math.Max(1, logs.Count);
        var cells = ClassIds.Select(c =>
        {
            var with = logs.Where(l => l.Classes.Contains(c)).ToList();
            if (with.Count == 0) return $"{"-",11}";
            double win = 100.0 * with.Count(l => l.Won) / with.Count;
            string star = judge && Math.Abs(win - avg) <= 5 ? "*" : " ";
            return $"{win,4:0}%{star}/{with.Average(l => l.DepthsCleared),4:0.0}";
        });
        return $"{cut,-44}{name,-8}" + string.Join("", cells);
    }

    private static string GearRow(string label, string name, List<RunLog> logs)
    {
        string cut = label.Length > 43 ? label[..40] + "..." : label;
        int n = Math.Max(1, logs.Count);
        return $"{cut,-44}{name,-8}{logs.Average(l => l.GearFound),10:0.0}{logs.Average(l => l.Mimics),11:0.00}" +
               $"{logs.Average(l => l.RaresEquipped),11:0.0}{100.0 * logs.Count(l => l.FullyArmed) / n,7:0}%";
    }

    private static IEnumerable<string> BossRows(string label, string name, List<RunLog> logs)
    {
        string cut = label.Length > 43 ? label[..40] + "..." : label;
        foreach (var g in logs.SelectMany(l => l.Fights).Where(f => f.Kind == CombatKind.Boss)
                     .GroupBy(f => f.Lead).OrderBy(g => g.Min(f => f.Depth)))
        {
            double win = 100.0 * g.Count(f => f.Won) / g.Count();
            double rounds = g.Average(f => f.Rounds);
            bool judge = name.Equals("Normal", StringComparison.OrdinalIgnoreCase);
            var bt = BossTargets.FirstOrDefault(b => g.Key.Contains(b.Name, StringComparison.OrdinalIgnoreCase));
            bool lich = bt.Name == "lich";
            string winStar = judge && bt.Name is not null && Math.Abs(win - bt.Win) <= 5 ? "*" : " ";
            string rdStar = judge && rounds >= (lich ? 8 : 6) && rounds <= (lich ? 10 : 8) ? "*" : " ";
            yield return $"{cut,-44}{name,-8}{g.Key,-22}{g.Count(),8}{win,6:0}%{winStar}{rounds,7:0.0}{rdStar}";
        }
    }
}
