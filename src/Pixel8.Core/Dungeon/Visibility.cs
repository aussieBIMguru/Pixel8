namespace Pixel8.Core.Dungeon;

/// <summary>How much the party knows about a tile.</summary>
public enum Vis
{
    Dark,   // nothing (except Exposed treasure)
    Far,    // edge of lookahead: links shown (dead ends visible) + treasure/campfire/boss icons
    Near,   // within lookahead: links + category icon (encounters show danger, not monsters)
    Known   // visited
}

/// <summary>
/// Fog of war. Distances are measured along corridors from every visited tile,
/// so the party remembers what it has already seen.
/// </summary>
public static class Visibility
{
    /// <param name="lookahead">2 by default; Lantern trinket / Scout card raise it.</param>
    public static Vis[,] Compute(DungeonGrid g, int lookahead = 2)
    {
        var vis = new Vis[g.Width, g.Height];
        var dist = g.Distances(g.Rooms.Where(t => t.Visited));

        foreach (var t in g.All)
        {
            int d = dist[t.X, t.Y];
            vis[t.X, t.Y] =
                t.Visited ? Vis.Known
                : d < 0 ? Vis.Dark
                : d <= lookahead - 1 ? Vis.Near
                : d <= lookahead ? Vis.Far
                : Vis.Dark;
        }
        return vis;
    }

    /// <summary>The icon the player may see for this tile, or null when it should show as unknown.</summary>
    public static TileKind? VisibleKind(Tile t, Vis v) => v switch
    {
        Vis.Known or Vis.Near => t.Kind,
        Vis.Far when t.Kind is TileKind.Treasure or TileKind.Campfire or TileKind.Merchant or TileKind.Boss => t.Kind,
        _ when t.Exposed => t.Kind,
        _ => null
    };

    public static bool ShowsLinks(Vis v) => v != Vis.Dark;
}
