using Microsoft.Xna.Framework.Audio;

namespace Pixel8.Desktop.Fx;

/// <summary>
/// Tiny chiptune sound effects, synthesised at startup (square/noise waves), so there are no audio files yet.
/// Turned on and off by the Sound setting. If the machine has no audio device, sound quietly stays off.
/// Uses System.Random for noise: visuals and audio never touch the rules RNG.
/// </summary>
public static class Sfx
{
    private static readonly Dictionary<string, SoundEffect> Sounds = new();
    private static bool _available;

    public static bool Enabled { get; set; } = true;

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
        if (!Enabled || !_available || !Sounds.TryGetValue(name, out var s)) return;
        try
        {
            s.Play(Math.Clamp(volume * 0.6f, 0f, 1f), Math.Clamp(pitch, -1f, 1f), 0f);
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

    private static void Add(string name, short[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        Sounds[name] = new SoundEffect(bytes, Rate, AudioChannels.Mono);
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
