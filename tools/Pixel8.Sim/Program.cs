using Pixel8.Core.Dice;
using Pixel8.Core.Dungeon;

// Usage:  dotnet run --project tools/Pixel8.Sim -- [seed] [dungeons-to-sample]
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
