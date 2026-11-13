using System.Numerics;

namespace Pixel8.Core.Dungeon;

[Flags]
public enum Dir : byte { None = 0, N = 1, E = 2, S = 4, W = 8 }

public static class Dirs
{
    public static readonly Dir[] All = { Dir.N, Dir.E, Dir.S, Dir.W };

    public static (int Dx, int Dy) Delta(this Dir d) => d switch
    {
        Dir.N => (0, -1),
        Dir.E => (1, 0),
        Dir.S => (0, 1),
        Dir.W => (-1, 0),
        _ => (0, 0)
    };

    public static Dir Opposite(this Dir d) => d switch
    {
        Dir.N => Dir.S,
        Dir.E => Dir.W,
        Dir.S => Dir.N,
        Dir.W => Dir.E,
        _ => Dir.None
    };
}

public enum TileKind
{
    Rock,       // not a room
    Entrance,
    Empty,
    Treasure,
    Encounter,
    Elite,
    Trap,
    Event,
    Campfire,
    Merchant,
    Boss
}

public sealed class Tile
{
    public Tile(int x, int y)
    {
        X = x;
        Y = y;
    }

    public int X { get; }
    public int Y { get; }
    public TileKind Kind { get; set; } = TileKind.Rock;
    public Dir Links { get; set; }

    /// <summary>The party has stood on this tile.</summary>
    public bool Visited { get; set; }

    /// <summary>Revealed from the start of the Depth regardless of fog (Munchkin-style goals).</summary>
    public bool Exposed { get; set; }

    /// <summary>Graph distance from the entrance (-1 = unreachable / rock).</summary>
    public int Distance { get; set; } = -1;

    public bool IsRoom => Kind != TileKind.Rock;
    public int Degree => BitOperations.PopCount((uint)Links);
    public bool Has(Dir d) => (Links & d) != 0;
    public bool IsHostile => Kind is TileKind.Encounter or TileKind.Elite or TileKind.Trap or TileKind.Boss;

    public override string ToString() => $"({X},{Y}) {Kind}";
}

public sealed class DungeonGrid
{
    private readonly Tile[] _tiles;

    public DungeonGrid(int width, int height)
    {
        Width = width;
        Height = height;
        _tiles = new Tile[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                _tiles[y * width + x] = new Tile(x, y);
    }

    public int Width { get; }
    public int Height { get; }
    public int Depth { get; init; } = 1;
    public ulong Seed { get; init; }

    public Tile Entrance { get; internal set; } = null!;
    public Tile Boss { get; internal set; } = null!;

    /// <summary>Starting Torch for this Depth (critical path × multiplier).</summary>
    public int Torch { get; internal set; }

    /// <summary>Tiles from entrance to boss inclusive.</summary>
    public IReadOnlyList<Tile> CriticalPath { get; internal set; } = Array.Empty<Tile>();

    public Tile this[int x, int y] => _tiles[y * Width + x];

    public IEnumerable<Tile> All => _tiles;
    public IEnumerable<Tile> Rooms => _tiles.Where(t => t.IsRoom);

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public Tile? Neighbour(Tile t, Dir d)
    {
        var (dx, dy) = d.Delta();
        int nx = t.X + dx, ny = t.Y + dy;
        return InBounds(nx, ny) ? this[nx, ny] : null;
    }

    public IEnumerable<Tile> LinkedNeighbours(Tile t)
    {
        foreach (var d in Dirs.All)
            if (t.Has(d) && Neighbour(t, d) is { } n)
                yield return n;
    }

    public void Link(Tile a, Dir d)
    {
        var b = Neighbour(a, d) ?? throw new InvalidOperationException("Link out of bounds");
        a.Links |= d;
        b.Links |= d.Opposite();
    }

    public void Unlink(Tile a, Dir d)
    {
        var b = Neighbour(a, d);
        a.Links &= ~d;
        if (b != null) b.Links &= ~d.Opposite();
    }

    /// <summary>Breadth-first distances along links from one or more source tiles. -1 = unreachable.</summary>
    public int[,] Distances(IEnumerable<Tile> sources)
    {
        var dist = new int[Width, Height];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                dist[x, y] = -1;

        var queue = new Queue<Tile>();
        foreach (var s in sources)
        {
            if (dist[s.X, s.Y] == 0) continue;
            dist[s.X, s.Y] = 0;
            queue.Enqueue(s);
        }

        while (queue.Count > 0)
        {
            var t = queue.Dequeue();
            foreach (var n in LinkedNeighbours(t))
            {
                if (dist[n.X, n.Y] >= 0) continue;
                dist[n.X, n.Y] = dist[t.X, t.Y] + 1;
                queue.Enqueue(n);
            }
        }
        return dist;
    }
}
