using Pixel8.Core.Randomness;

namespace Pixel8.Core.Dungeon;

/// <summary>All generation knobs live here so they can move to data/balance.json later.</summary>
public sealed record DungeonParams
{
    public int Width { get; init; } = 7;
    public int Height { get; init; } = 9;
    public int Depth { get; init; } = 1;
    public int TargetRooms { get; init; } = 44;
    public double ExtraLinkRatio { get; init; } = 0.18;
    public double TorchMultiplier { get; init; } = 1.6;
    public int ExposedTreasure { get; init; } = 3;
    public double DeadEndTreasureChance { get; init; } = 0.7;
    public int MinElites { get; init; } = 1;
    public int MaxElites { get; init; } = 2;
    /// <summary>Place a merchant tile (shop mode "on map"). Off when the shop lives at campfires or is always open.</summary>
    public bool Merchant { get; init; } = true;

    /// <summary>Weights for ordinary rooms that aren't otherwise forced.</summary>
    public IReadOnlyList<(TileKind Item, int Weight)> RoomTable { get; init; } = new[]
    {
        (TileKind.Empty, 35),
        (TileKind.Encounter, 30),
        (TileKind.Trap, 12),
        (TileKind.Event, 10),
        (TileKind.Treasure, 8),
    };
}

/// <summary>
/// Generates one Depth:
/// 1. randomised Prim's spanning tree over the whole grid,
/// 2. prune random leaves to rock until the target room count is reached,
/// 3. add extra links for loops,
/// 4. BFS from the entrance; the farthest room becomes the boss,
/// 5. place campfires, dead-end rewards, elites, filler rooms, then apply safety rules.
/// </summary>
public static class DungeonGenerator
{
    public static DungeonGrid Generate(ulong seed, DungeonParams? p = null)
    {
        p ??= new DungeonParams();
        var rng = new Rng(seed).Fork($"dungeon-{p.Depth}");
        var g = new DungeonGrid(p.Width, p.Height) { Seed = seed, Depth = p.Depth };
        var entrance = g[p.Width / 2, p.Height - 1];

        BuildSpanningTree(g, entrance, rng);
        int rooms = PruneToTarget(g, entrance, p.TargetRooms, rng);
        AddLoops(g, rooms, p.ExtraLinkRatio, rng);

        // Distances and boss
        var dist = g.Distances(new[] { entrance });
        foreach (var t in g.All) t.Distance = dist[t.X, t.Y];

        var boss = g.Rooms
            .OrderByDescending(t => t.Distance)
            .ThenBy(t => t.Y)
            .ThenBy(t => t.X)
            .First();

        entrance.Kind = TileKind.Entrance;
        boss.Kind = TileKind.Boss;
        g.Entrance = entrance;
        g.Boss = boss;
        g.CriticalPath = TracePath(g, boss);

        var fixedTiles = new HashSet<Tile> { entrance, boss };
        PlaceCampfires(g, fixedTiles);
        PlaceDeadEnds(g, fixedTiles, p, rng);
        PlaceMerchant(g, fixedTiles, p, rng);
        FillRemaining(g, fixedTiles, p, rng);
        EnforceElites(g, fixedTiles, p, rng);
        ApplySafetyRules(g);
        ExposeTreasure(g, p, rng);

        g.Torch = (int)Math.Ceiling(boss.Distance * p.TorchMultiplier);
        entrance.Visited = true;
        return g;
    }

    private static void BuildSpanningTree(DungeonGrid g, Tile start, Rng rng)
    {
        foreach (var t in g.All) t.Kind = TileKind.Empty;

        var inTree = new HashSet<Tile> { start };
        var frontier = new List<(Tile From, Dir D)>();

        void AddFrontier(Tile t)
        {
            foreach (var d in Dirs.All)
                if (g.Neighbour(t, d) is { } n && !inTree.Contains(n))
                    frontier.Add((t, d));
        }

        AddFrontier(start);
        while (frontier.Count > 0)
        {
            int i = rng.Next(frontier.Count);
            var (from, d) = frontier[i];
            frontier[i] = frontier[^1];
            frontier.RemoveAt(frontier.Count - 1);

            var to = g.Neighbour(from, d)!;
            if (inTree.Contains(to)) continue;

            g.Link(from, d);
            inTree.Add(to);
            AddFrontier(to);
        }
    }

    /// <summary>Removing only leaves keeps the tree connected.</summary>
    private static int PruneToTarget(DungeonGrid g, Tile entrance, int target, Rng rng)
    {
        int rooms = g.Width * g.Height;
        while (rooms > target)
        {
            var leaves = g.Rooms.Where(t => t.Degree == 1 && t != entrance).ToList();
            if (leaves.Count == 0) break;

            var leaf = rng.Pick(leaves);
            foreach (var d in Dirs.All)
                if (leaf.Has(d)) g.Unlink(leaf, d);
            leaf.Kind = TileKind.Rock;
            rooms--;
        }
        return rooms;
    }

    private static void AddLoops(DungeonGrid g, int rooms, double ratio, Rng rng)
    {
        var candidates = new List<(Tile T, Dir D)>();
        foreach (var t in g.Rooms)
            foreach (var d in new[] { Dir.E, Dir.S })
                if (g.Neighbour(t, d) is { IsRoom: true } && !t.Has(d))
                    candidates.Add((t, d));

        rng.Shuffle(candidates);
        int extra = (int)Math.Round(rooms * ratio);
        foreach (var (t, d) in candidates.Take(extra))
            g.Link(t, d);
    }

    private static List<Tile> TracePath(DungeonGrid g, Tile boss)
    {
        var path = new List<Tile> { boss };
        var current = boss;
        while (current.Distance > 0)
        {
            current = g.LinkedNeighbours(current).First(n => n.Distance == current.Distance - 1);
            path.Add(current);
        }
        path.Reverse();
        return path;
    }

    private static void PlaceCampfires(DungeonGrid g, HashSet<Tile> fixedTiles)
    {
        var path = g.CriticalPath;
        int bossDist = g.Boss.Distance;

        // Mid-depth campfire around 50% of the way
        int midTarget = Math.Max(2, (int)Math.Round(bossDist * 0.5));
        var mid = path
            .Where(t => !fixedTiles.Contains(t))
            .OrderBy(t => Math.Abs(t.Distance - midTarget))
            .FirstOrDefault();
        if (mid != null)
        {
            mid.Kind = TileKind.Campfire;
            fixedTiles.Add(mid);
        }

        // Pre-boss campfire right before the boss room
        var preBoss = path.Count >= 2 ? path[^2] : null;
        if (preBoss != null && !fixedTiles.Contains(preBoss))
        {
            preBoss.Kind = TileKind.Campfire;
            fixedTiles.Add(preBoss);
        }
    }

    private static void PlaceDeadEnds(DungeonGrid g, HashSet<Tile> fixedTiles, DungeonParams p, Rng rng)
    {
        foreach (var t in g.Rooms.Where(t => t.Degree == 1 && !fixedTiles.Contains(t)).ToList())
        {
            t.Kind = rng.Chance(p.DeadEndTreasureChance)
                ? TileKind.Treasure
                : (rng.Chance(0.5) ? TileKind.Event : TileKind.Elite);
            fixedTiles.Add(t);
        }
    }

    /// <summary>
    /// One merchant on every Depth (session 6: Depth 1 had none, so the shop was never found), preferably off
    /// the critical path and at least 3 rooms in. Its icon shows from 2 rooms away, like campfires.
    /// </summary>
    private static void PlaceMerchant(DungeonGrid g, HashSet<Tile> fixedTiles, DungeonParams p, Rng rng)
    {
        if (!p.Merchant) return;
        var options = g.Rooms
            .Where(t => !fixedTiles.Contains(t) && !g.CriticalPath.Contains(t) && t.Distance >= 3)
            .ToList();
        if (options.Count == 0)
            options = g.Rooms.Where(t => !fixedTiles.Contains(t) && t.Distance >= 2).ToList();
        if (options.Count == 0) return;

        var m = rng.Pick(options);
        m.Kind = TileKind.Merchant;
        fixedTiles.Add(m);
    }

    private static void FillRemaining(DungeonGrid g, HashSet<Tile> fixedTiles, DungeonParams p, Rng rng)
    {
        foreach (var t in g.Rooms.Where(t => !fixedTiles.Contains(t)))
            t.Kind = rng.Weighted(p.RoomTable);
    }

    private static void EnforceElites(DungeonGrid g, HashSet<Tile> fixedTiles, DungeonParams p, Rng rng)
    {
        var elites = g.Rooms.Where(t => t.Kind == TileKind.Elite).ToList();

        // Too many: demote extras to ordinary encounters
        rng.Shuffle(elites);
        foreach (var extra in elites.Skip(p.MaxElites))
            extra.Kind = TileKind.Encounter;

        // Too few: promote encounters/empties off the critical path, away from the entrance
        int missing = p.MinElites - Math.Min(elites.Count, p.MaxElites);
        if (missing <= 0) return;

        var options = g.Rooms
            .Where(t => t.Kind is TileKind.Encounter or TileKind.Empty
                        && !fixedTiles.Contains(t)
                        && t.Distance >= 3)
            .ToList();
        rng.Shuffle(options);
        foreach (var t in options.Take(missing))
            t.Kind = TileKind.Elite;
    }

    private static void ApplySafetyRules(DungeonGrid g)
    {
        // Nothing hostile on or next to the entrance
        foreach (var n in g.LinkedNeighbours(g.Entrance))
            if (n.IsHostile && n.Kind != TileKind.Boss)
                n.Kind = TileKind.Empty;

        // No more than 2 fights in a row along the critical path
        int streak = 0;
        foreach (var t in g.CriticalPath)
        {
            bool fight = t.Kind is TileKind.Encounter or TileKind.Elite;
            streak = fight ? streak + 1 : 0;
            if (streak > 2)
            {
                t.Kind = TileKind.Empty;
                streak = 0;
            }
        }
    }

    private static void ExposeTreasure(DungeonGrid g, DungeonParams p, Rng rng)
    {
        var treasure = g.Rooms.Where(t => t.Kind == TileKind.Treasure).ToList();
        rng.Shuffle(treasure);
        foreach (var t in treasure.Take(p.ExposedTreasure))
            t.Exposed = true;
    }
}
