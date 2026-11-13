using Pixel8.Core.Dungeon;

namespace Pixel8.Tests;

public class DungeonTests
{
    public static IEnumerable<object[]> Seeds() =>
        Enumerable.Range(1, 200).Select(i => new object[] { (ulong)i });

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Every_room_is_reachable_from_the_entrance(ulong seed)
    {
        var g = DungeonGenerator.Generate(seed);
        Assert.All(g.Rooms, t => Assert.True(t.Distance >= 0, $"{t} unreachable"));
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Exactly_one_boss_at_max_distance(ulong seed)
    {
        var g = DungeonGenerator.Generate(seed);
        Assert.Single(g.Rooms, t => t.Kind == TileKind.Boss);
        Assert.Equal(g.Rooms.Max(t => t.Distance), g.Boss.Distance);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Entrance_neighbours_are_safe(ulong seed)
    {
        var g = DungeonGenerator.Generate(seed);
        foreach (var n in g.LinkedNeighbours(g.Entrance))
            Assert.True(!n.IsHostile || n.Kind == TileKind.Boss, $"{n} next to entrance");
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Has_mid_campfire_and_room_count_on_target(ulong seed)
    {
        var p = new DungeonParams();
        var g = DungeonGenerator.Generate(seed, p);
        Assert.Contains(g.Rooms, t => t.Kind == TileKind.Campfire);
        Assert.InRange(g.Rooms.Count(), p.TargetRooms - 2, p.TargetRooms + 2);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void No_more_than_two_fights_in_a_row_on_critical_path(ulong seed)
    {
        var g = DungeonGenerator.Generate(seed);
        int streak = 0;
        foreach (var t in g.CriticalPath)
        {
            streak = t.Kind is TileKind.Encounter or TileKind.Elite ? streak + 1 : 0;
            Assert.True(streak <= 2);
        }
    }

    [Fact]
    public void Same_seed_produces_identical_dungeon()
    {
        var a = DungeonGenerator.Generate(999);
        var b = DungeonGenerator.Generate(999);
        Assert.Equal(
            a.All.Select(t => (t.Kind, t.Links, t.Exposed)),
            b.All.Select(t => (t.Kind, t.Links, t.Exposed)));
    }

    [Fact]
    public void Fog_shows_one_ahead_near_two_ahead_far()
    {
        var g = DungeonGenerator.Generate(5);
        var vis = Visibility.Compute(g);
        var dist = g.Distances(new[] { g.Entrance });

        foreach (var t in g.Rooms)
        {
            var expected = dist[t.X, t.Y] switch
            {
                0 => Vis.Known,
                1 => Vis.Near,
                2 => Vis.Far,
                _ => Vis.Dark
            };
            Assert.Equal(expected, vis[t.X, t.Y]);
        }
    }
}
