using Microsoft.Xna.Framework.Audio;
using Pixel8.Core.Audio;

namespace Pixel8.Desktop.Fx;

/// <summary>
/// Tiny chiptune sound effects, synthesised at startup (square/noise waves, and short jingles written in the
/// music format and rendered by <see cref="ChipSynth"/>), so there are no audio files. Volume comes from the title
/// screen's SFX setting (0 = off). If the machine has no audio device, sound quietly stays off.
/// Uses System.Random for noise: visuals and audio never touch the rules RNG.
/// </summary>
public static class Sfx
{
    private static readonly Dictionary<string, SoundEffect> Sounds = new();
    private static bool _available;

    public static bool Enabled { get; set; } = true;

    /// <summary>SFX volume 0..1 (title-screen setting).</summary>
    public static float Volume { get; set; } = 0.6f;

    /// <summary>Builds every sound. Call once after the graphics device exists.</summary>
    public static void Load()
    {
        try
        {
            Add("tick", Tone(1200, 0.018, Wave.Square, 0.25));
            Add("land", Tone(520, 0.06, Wave.Square, 0.35, endFreq: 380));
            Add("part", Tone(880, 0.03, Wave.Square, 0.2));
            Add("hit", Tone(180, 0.12, Wave.Noise, 0.45, endFreq: 90));
            Add("hurt", Tone(240, 0.18, Wave.Square, 0.4, endFreq: 70));
            Add("miss", Tone(300, 0.12, Wave.Triangle, 0.35, endFreq: 180));
            Add("crit", Concat(Tone(660, 0.05, Wave.Square, 0.35), Tone(990, 0.05, Wave.Square, 0.35), Tone(1320, 0.08, Wave.Square, 0.35)));
            Add("heal", Concat(Tone(520, 0.05, Wave.Triangle, 0.35), Tone(780, 0.08, Wave.Triangle, 0.35)));
            Add("block", Tone(140, 0.08, Wave.Square, 0.3));
            Add("select", Tone(700, 0.03, Wave.Square, 0.25));
            Add("confirm", Concat(Tone(600, 0.04, Wave.Square, 0.3), Tone(900, 0.06, Wave.Square, 0.3)));
            Add("deny", Tone(160, 0.1, Wave.Square, 0.3));
            Add("coin", Concat(Tone(988, 0.04, Wave.Square, 0.3), Tone(1319, 0.1, Wave.Square, 0.3)));
            Add("warn", Concat(Tone(440, 0.07, Wave.Square, 0.35), Tone(330, 0.07, Wave.Square, 0.35), Tone(440, 0.07, Wave.Square, 0.35)));
            Add("perfect", Concat(Tone(784, 0.04, Wave.Square, 0.35), Tone(1046, 0.04, Wave.Square, 0.35), Tone(1568, 0.1, Wave.Square, 0.35)));
            Add("good", Concat(Tone(660, 0.04, Wave.Square, 0.3), Tone(880, 0.08, Wave.Square, 0.3)));
            Add("down", Tone(400, 0.35, Wave.Square, 0.4, endFreq: 60));
            Add("die", Tone(300, 0.2, Wave.Noise, 0.4, endFreq: 60));

            // ---- round 9: dice and cards
            Add("fumble", Concat(Tone(330, 0.06, Wave.Square, 0.35), Tone(220, 0.06, Wave.Square, 0.35), Tone(110, 0.14, Wave.Square, 0.35, endFreq: 70)));
            Add("draw", Tone(900, 0.03, Wave.Noise, 0.25, endFreq: 2400));
            Add("card", Concat(Tone(500, 0.025, Wave.Square, 0.25), Tone(1000, 0.04, Wave.Noise, 0.2, endFreq: 3000)));
            Jingle("rearm", "tempo: 240\nA: @1 v11 l32 o5 c e g >c< g >c e");
            Jingle("luck", "tempo: 200\nA: @2 v11 l32 o6 e g+ b >e< b >e g+ b");
            // ---- map and world
            Add("step", Tone(90, 0.04, Wave.Noise, 0.18, endFreq: 60));
            Add("reveal", Tone(600, 0.05, Wave.Triangle, 0.22, endFreq: 900));
            Jingle("stairs", "tempo: 180\nA: @1 v11 l16 o6 c o5 g e c o4 g e c4\nT: l16 o3 c o2 g e c o1 g e c4");
            Add("torch_low", Concat(Tone(392, 0.08, Wave.Triangle, 0.35), Tone(262, 0.14, Wave.Triangle, 0.35)));
            Add("fire", Concat(Tone(2000, 0.02, Wave.Noise, 0.2), Tone(700, 0.05, Wave.Noise, 0.12), Tone(3000, 0.015, Wave.Noise, 0.2), Tone(500, 0.12, Wave.Noise, 0.1, endFreq: 200)));
            Add("trap_spot", Concat(Tone(880, 0.03, Wave.Square, 0.25), Tone(660, 0.05, Wave.Square, 0.25)));
            // ---- rewards and UI
            Add("coins", Concat(Tone(988, 0.035, Wave.Square, 0.28), Tone(1319, 0.035, Wave.Square, 0.28), Tone(988, 0.035, Wave.Square, 0.28), Tone(1568, 0.09, Wave.Square, 0.28)));
            Jingle("item", "tempo: 200\nA: @1 v11 l16 o5 g >c e g4");
            Jingle("levelup", "tempo: 160\nA: @1 v12 l16 o5 c e g >c< g >c e g4\nB: @0 v8 l16 o4 g >c e g e g >c e4\nT: l8 o3 c g >c4");
            Jingle("buy", "tempo: 220\nA: @1 v11 l32 o6 c e g e >c8");
            Add("back", Concat(Tone(700, 0.03, Wave.Square, 0.25), Tone(450, 0.05, Wave.Square, 0.25)));
            // ---- combat extras
            Add("graze", Tone(2200, 0.04, Wave.Triangle, 0.25, endFreq: 1600));
            Add("focus", Tone(400, 0.12, Wave.Square, 0.2, endFreq: 1200));
            Add("poison", Concat(Tone(260, 0.04, Wave.Square, 0.25, endFreq: 240), Tone(200, 0.06, Wave.Square, 0.25, endFreq: 150)));
            Add("roar", Mix(Tone(120, 0.6, Wave.Noise, 0.45, endFreq: 40), Tone(90, 0.6, Wave.Square, 0.25, endFreq: 50)));
            Add("ok", Tone(520, 0.06, Wave.Square, 0.28));
            Add("timing_miss", Tone(200, 0.1, Wave.Triangle, 0.3, endFreq: 140));
            Jingle("achievement", "tempo: 200\nA: @1 v12 l16 o5 g >c e g e g >c4\nB: @2 v7 l16 o5 e g >c e c e g4\nT: l8 o3 c g >c4");
            Add("recharge", Tone(200, 0.3, Wave.Square, 0.22, endFreq: 800));
            _available = true;
        }
        catch (Exception)
        {
            // NoAudioHardwareException and friends: play without sound.
            _available = false;
        }
    }

    public static void Play(string name, float volume = 1f, float pitch = 0f)
    {
        if (!Enabled || Volume <= 0 || !_available || !Sounds.TryGetValue(name, out var s)) return;
        try
        {
            s.Play(Math.Clamp(volume * 0.6f * Volume, 0f, 1f), Math.Clamp(pitch, -1f, 1f), 0f);
        }
        catch (Exception)
        {
            // Too many voices etc.: drop the sound.
        }
    }

    public static void Unload()
    {
        foreach (var s in Sounds.Values) s.Dispose();
        Sounds.Clear();
        _available = false;
    }

    // ------------------------------------------------------------------ synthesis

    private const int Rate = 22050;
    private static readonly Random Noise = new(8);

    private enum Wave { Square, Triangle, Noise }

    private static void Add(string name, short[] samples, int rate = Rate)
    {
        var bytes = new byte[samples.Length * 2];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        Sounds[name] = new SoundEffect(bytes, rate, AudioChannels.Mono);
    }

    /// <summary>A short jingle in the music format (loop: no is implied), rendered by the chip synth.</summary>
    private static void Jingle(string name, string mml) =>
        Add(name, ChipSynth.Render(Mml.Parse("loop: no\n" + mml, name), gain: 1.4), ChipSynth.SampleRate);

    /// <summary>Plays two sounds at once (same start), padded to the longer one.</summary>
    private static short[] Mix(short[] a, short[] b)
    {
        var data = new short[Math.Max(a.Length, b.Length)];
        for (int i = 0; i < data.Length; i++)
            data[i] = (short)Math.Clamp((i < a.Length ? a[i] : 0) + (i < b.Length ? b[i] : 0), short.MinValue, short.MaxValue);
        return data;
    }

    private static short[] Tone(double freq, double seconds, Wave wave, double volume, double? endFreq = null)
    {
        int n = Math.Max(1, (int)(Rate * seconds));
        var data = new short[n];
        double phase = 0;
        double noiseValue = 0;
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)n;
            double f = endFreq is double e ? freq + (e - freq) * t : freq;
            double prev = phase;
            phase = (phase + f / Rate) % 1.0;
            double v = wave switch
            {
                Wave.Square => phase < 0.5 ? 1 : -1,
                Wave.Triangle => phase < 0.5 ? phase * 4 - 1 : 3 - phase * 4,
                _ => phase < prev ? (noiseValue = Noise.NextDouble() * 2 - 1) : noiseValue
            };
            // Quick attack, linear release, so clicks don't pop.
            double env = Math.Min(1, i / (Rate * 0.002)) * (1 - t);
            data[i] = (short)(v * env * volume * short.MaxValue);
        }
        return data;
    }

    private static short[] Concat(params short[][] parts) => parts.SelectMany(p => p).ToArray();
}
