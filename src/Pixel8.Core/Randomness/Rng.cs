namespace Pixel8.Core.Randomness;

/// <summary>
/// Deterministic xoshiro128** generator seeded via SplitMix64.
/// Every random decision in the rules goes through one of these so a seed reproduces a whole run.
/// Use <see cref="Fork"/> to give each system (dungeon, loot, combat) its own independent stream,
/// so adding a roll in combat doesn't change the dungeon layout for the same seed.
/// </summary>
public sealed class Rng
{
    private uint _s0, _s1, _s2, _s3;

    public ulong Seed { get; }

    public Rng(ulong seed)
    {
        Seed = seed;
        ulong x = seed;
        ulong a = SplitMix(ref x);
        ulong b = SplitMix(ref x);
        _s0 = (uint)a;
        _s1 = (uint)(a >> 32);
        _s2 = (uint)b;
        _s3 = (uint)(b >> 32);
        if ((_s0 | _s1 | _s2 | _s3) == 0) _s0 = 1;
    }

    /// <summary>Creates an independent sub-stream keyed by name (FNV-1a mixed with the seed).</summary>
    public Rng Fork(string stream)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in stream)
        {
            h ^= c;
            h *= 1099511628211UL;
        }
        return new Rng(Seed ^ h);
    }

    public uint NextUInt()
    {
        uint result = RotL(_s1 * 5, 7) * 9;
        uint t = _s1 << 9;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = RotL(_s3, 11);
        return result;
    }

    /// <summary>Uniform integer in [0, maxExclusive).</summary>
    public int Next(int maxExclusive)
    {
        if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        return (int)(((ulong)NextUInt() * (ulong)maxExclusive) >> 32);
    }

    /// <summary>Uniform integer in [min, maxExclusive).</summary>
    public int Range(int min, int maxExclusive) => min + Next(maxExclusive - min);

    /// <summary>Rolls a single die with the given number of sides (1..sides).</summary>
    public int Roll(int sides) => 1 + Next(sides);

    public double NextDouble() => NextUInt() * (1.0 / 4294967296.0);

    public bool Chance(double probability) => NextDouble() < probability;

    public T Pick<T>(IReadOnlyList<T> list) => list[Next(list.Count)];

    public void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    public T Weighted<T>(IReadOnlyList<(T Item, int Weight)> table)
    {
        int total = 0;
        foreach (var entry in table) total += entry.Weight;
        int roll = Next(total);
        foreach (var entry in table)
        {
            if (roll < entry.Weight) return entry.Item;
            roll -= entry.Weight;
        }
        return table[^1].Item;
    }

    private static uint RotL(uint x, int k) => (x << k) | (x >> (32 - k));

    private static ulong SplitMix(ref ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        ulong z = x;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
