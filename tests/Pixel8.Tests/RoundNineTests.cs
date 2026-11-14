using Pixel8.Core.Audio;
using Pixel8.Core.Content;
using Pixel8.Core.Runs;

namespace Pixel8.Tests;

/// <summary>Round 9: the music text format and chip synth.</summary>
public class MusicTests
{
    private static string MusicDir => Path.Combine(ContentDb.FindDataDirectory(), "music");

    [Fact]
    public void Every_shipped_track_parses_with_clean_timing()
    {
        var files = Directory.GetFiles(MusicDir, "*.mml");
        foreach (var name in new[] { "title", "map1", "map2", "map3", "map4", "map5", "combat", "boss", "victory", "defeat" })
            Assert.Contains(files, f => Path.GetFileNameWithoutExtension(f) == name);
        foreach (var file in files)
        {
            var song = Mml.Parse(File.ReadAllText(file), Path.GetFileNameWithoutExtension(file));
            Assert.True(song.Warnings.Count == 0, $"{song.Name}: {string.Join("; ", song.Warnings)}");
            Assert.True(song.Beats > 0, song.Name);
        }
    }

    [Fact]
    public void Notes_octaves_lengths_and_dots()
    {
        var song = Mml.Parse("tempo: 120\nA: o4 c4 d+8 e-8. > c2 < b16 r4 c4^8");
        var a = song.Notes['A'];
        Assert.Equal(60, a[0].Pitch);
        Assert.Equal(1.0, a[0].Length, 6);
        Assert.Equal(63, a[1].Pitch);
        Assert.Equal(0.5, a[1].Length, 6);
        Assert.Equal(63, a[2].Pitch);           // e flat
        Assert.Equal(0.75, a[2].Length, 6);     // dotted eighth
        Assert.Equal(72, a[3].Pitch);
        Assert.Equal(71, a[4].Pitch);
        Assert.Equal(1.5, a[^1].Length, 6);     // c4 extended by ^8
        Assert.Equal(1 + 0.5 + 0.75 + 2 + 0.25 + 1 + 1.5, song.Lengths['A'], 6);
    }

    [Fact]
    public void Repeats_nest_and_default_to_two()
    {
        var song = Mml.Parse("A: l8 [c [d e]3 ]2 [f]");
        // (c + d e ×3) ×2 = 14 notes, then f ×2.
        Assert.Equal(16, song.Notes['A'].Count);
        Assert.Equal(8.0, song.Lengths['A'], 6);
    }

    [Fact]
    public void Drums_live_on_the_noise_channel()
    {
        var song = Mml.Parse("N: l8 k h s x");
        Assert.Equal("khsx", new string(song.Notes['N'].Select(n => n.Drum).ToArray()));
        Assert.Throws<FormatException>(() => Mml.Parse("A: k"));
    }

    [Fact]
    public void Timing_problems_are_reported()
    {
        var song = Mml.Parse("A: c1 c1\nB: c1 | c2 |");
        Assert.Contains(song.Warnings, w => w.Contains("lengths differ"));
        Assert.Contains(song.Warnings, w => w.Contains("bar line"));
        Assert.Empty(Mml.Parse("loop: no\nA: c1 c1\nB: c1").Warnings.Where(w => w.Contains("lengths differ")));
    }

    [Fact]
    public void Synth_is_deterministic_bounded_and_the_right_length()
    {
        var song = Mml.Parse(File.ReadAllText(Path.Combine(MusicDir, "victory.mml")), "victory");
        var a = ChipSynth.Render(song);
        var b = ChipSynth.Render(song);
        Assert.Equal(a, b);
        Assert.Equal((int)Math.Round(song.Seconds * ChipSynth.SampleRate), a.Length);
        Assert.Contains(a, s => Math.Abs((int)s) > 1000);   // it isn't silent
        Assert.All(ChipSynth.RenderFloat(song), v => Assert.InRange(v, -1.0, 1.0));
    }

    [Fact]
    public void Middle_a_is_440()
    {
        Assert.Equal(440.0, ChipSynth.Frequency(69), 6);
        Assert.Equal(261.63, ChipSynth.Frequency(60), 2);
    }
}

/// <summary>Round 9: treasure and traps moved from the map screen into Run (shared with the Sim).</summary>
public class TreasureTrapTests
{
    [Fact]
    public void Treasure_gold_scales_with_depth()
    {
        var run = new Run(TestContent.Db, 5, new[] { "fighter", "rogue", "wizard" }) { Depth = 2 };
        for (int i = 0; i < 50; i++)
        {
            int before = run.Party.Gold;
            int gold = run.OpenTreasure();
            Assert.InRange(gold, 25, 35);
            Assert.Equal(before + gold, run.Party.Gold);
        }
    }

    [Fact]
    public void Traps_hurt_but_never_down_a_hero()
    {
        var run = new Run(TestContent.Db, 6, new[] { "fighter", "rogue", "wizard" }) { Depth = 3 };
        foreach (var h in run.Party.Heroes) h.Hp = 2;
        int hurt = 0;
        for (int i = 0; i < 200; i++)
        {
            var trap = run.SpringTrap()!;
            if (!trap.Spotted)
            {
                hurt++;
                Assert.InRange(trap.Damage, 3, 8);
            }
            Assert.All(run.Party.Heroes, h => Assert.True(h.Hp >= 1 && !h.IsDowned));
        }
        Assert.True(hurt > 0);
    }
}
