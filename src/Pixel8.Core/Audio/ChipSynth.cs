namespace Pixel8.Core.Audio;

/// <summary>
/// Renders a <see cref="ChipSong"/> to 16-bit mono PCM, NES style (round 9): two band-limited pulse channels with
/// four duty cycles, a 4-bit stepped triangle and a 15-bit LFSR noise channel for drums. Pure and deterministic:
/// the same song always renders the same samples. Nothing in the rules uses this; it lives in Core so it can be tested.
/// The Python preview script (tools/music/chip.py) mirrors it.
/// </summary>
public static class ChipSynth
{
    public const int SampleRate = 32000;

    /// <summary>Corner of the softening low-pass filter on the final mix.</summary>
    public const double LowPassHz = 6000;

    private static readonly double[] DutyCycles = { 0.125, 0.25, 0.5, 0.75 };

    /// <summary>Per-channel mix levels.</summary>
    private static readonly Dictionary<char, double> Gains = new() { ['A'] = 0.22, ['B'] = 0.22, ['T'] = 0.30, ['N'] = 0.22 };

    public static short[] Render(ChipSong song, double gain = 0.9)
    {
        var mix = RenderFloat(song);
        var pcm = new short[mix.Length];
        for (int i = 0; i < mix.Length; i++)
            pcm[i] = (short)Math.Round(Math.Clamp(mix[i] * gain, -1, 1) * short.MaxValue);
        return pcm;
    }

    /// <summary>The final mix in −1..1 (DC-blocked, soft-clipped).</summary>
    public static double[] RenderFloat(ChipSong song)
    {
        double secondsPerBeat = 60.0 / song.Tempo;
        int total = (int)Math.Round(song.Beats * secondsPerBeat * SampleRate);
        var mix = new double[total];
        var lfsr = new Lfsr(0x4A1);

        foreach (var (ch, notes) in song.Notes)
        {
            double channelGain = Gains.GetValueOrDefault(ch, 0.2);
            foreach (var note in notes)
            {
                int s0 = (int)Math.Round(note.Start * secondsPerBeat * SampleRate);
                int s1 = Math.Min(total, (int)Math.Round((note.Start + note.Length) * secondsPerBeat * SampleRate));
                if (s1 <= s0 || note.Volume == 0) continue;
                double amp = note.Volume / 15.0 * channelGain;
                int count = s1 - s0;
                switch (ch)
                {
                    case 'N': Drum(mix, s0, count, note.Drum, amp, lfsr); break;
                    case 'T': Triangle(mix, s0, count, note, amp); break;
                    default: Pulse(mix, s0, count, note, amp); break;
                }
            }
        }

        // DC block (one-pole high-pass, ~40 Hz), a gentle one-pole low-pass (~6 kHz) to take the fizz off the
        // pulse waves (the "tinny" edge), then a soft clip.
        double r = 1 - 2 * Math.PI * 40 / SampleRate, prevX = 0, prevY = 0;
        double lp = 1 - Math.Exp(-2 * Math.PI * LowPassHz / SampleRate), low = 0;
        for (int i = 0; i < total; i++)
        {
            double y = mix[i] - prevX + r * prevY;
            prevX = mix[i];
            prevY = y;
            low += lp * (y - low);
            mix[i] = Math.Tanh(low * 1.1);
        }
        return mix;
    }

    public static double Frequency(int midi) => 440.0 * Math.Pow(2, (midi - 69) / 12.0);

    // ------------------------------------------------------------------ voices

    private static void Pulse(double[] mix, int s0, int count, ChipNote note, double amp)
    {
        double f = Frequency(note.Pitch), dt = f / SampleRate, duty = DutyCycles[Math.Clamp(note.Duty, 0, 3)];
        int on = Math.Max(1, count * note.Gate / 8);
        int attack = (int)(0.003 * SampleRate), decay = (int)(0.12 * SampleRate), release = (int)(0.02 * SampleRate);
        double last = 0;
        for (int k = 0; k < count; k++)
        {
            double env;
            if (k < on)
            {
                env = Math.Min(1, k / (double)Math.Max(1, attack)) * (0.6 + 0.4 * Math.Clamp(1 - (k - attack) / (double)decay, 0, 1));
                last = env;
            }
            else if (k < on + release) env = last * (1 - (k - on) / (double)release);
            else break;

            double ph = k * dt % 1.0;
            double w = ph < duty ? 1 : -1;
            w += PolyBlep(ph, dt) - PolyBlep((ph - duty + 1) % 1.0, dt);
            mix[s0 + k] += w * env * amp;
        }
    }

    private static void Triangle(double[] mix, int s0, int count, ChipNote note, double amp)
    {
        double dt = Frequency(note.Pitch) / SampleRate;
        int on = Math.Max(1, count * note.Gate / 8);
        int release = (int)(0.005 * SampleRate);
        for (int k = 0; k < count && k < on + release; k++)
        {
            double env = k < on ? 1 : 1 - (k - on) / (double)release;
            double ph = k * dt % 1.0;
            int step = (int)Math.Floor(ph * 32);
            double tri = step < 16 ? step : 31 - step;      // 4-bit stepped, like the NES
            mix[s0 + k] += (tri / 7.5 - 1) * env * amp;
        }
    }

    private static void Drum(double[] mix, int s0, int count, char kind, double amp, Lfsr lfsr)
    {
        switch (kind)
        {
            case 'k':
            {
                double ph = 0;
                var noise = lfsr.Burst(count, 8000, 0.01);
                for (int k = 0; k < count; k++)
                {
                    double t = k / (double)SampleRate;
                    double f = 150 * Math.Exp(-t / 0.05) + 45;
                    ph = (ph + f / SampleRate) % 1.0;
                    double tri = 4 * Math.Abs(ph - 0.5) - 1;
                    double v = (t < 0.2 ? tri * Math.Exp(-t / 0.09) : 0) + noise[k] * 0.3 * Math.Exp(-t / 0.004);
                    mix[s0 + k] += v * amp;
                }
                break;
            }
            case 's':
            {
                double ph = 0;
                var noise = lfsr.Burst(count, 6000, 0.2);
                for (int k = 0; k < count; k++)
                {
                    double t = k / (double)SampleRate;
                    double f = 220 * Math.Exp(-t / 0.03) + 160;
                    ph = (ph + f / SampleRate) % 1.0;
                    double tri = 4 * Math.Abs(ph - 0.5) - 1;
                    mix[s0 + k] += (noise[k] * 0.8 * Math.Exp(-t / 0.06) + tri * 0.4 * Math.Exp(-t / 0.04)) * amp;
                }
                break;
            }
            case 'h':
            {
                var noise = lfsr.Burst(count, 16000, 0.06);
                for (int k = 0; k < count; k++)
                    mix[s0 + k] += noise[k] * 0.5 * Math.Exp(-k / (double)SampleRate / 0.015) * amp;
                break;
            }
            case 'x':
            {
                var noise = lfsr.Burst(count, 12000, 0.6);
                for (int k = 0; k < count; k++)
                    mix[s0 + k] += noise[k] * 0.6 * Math.Exp(-k / (double)SampleRate / 0.18) * amp;
                break;
            }
        }
    }

    private static double PolyBlep(double t, double dt)
    {
        if (t < dt)
        {
            double x = t / dt;
            return x + x - x * x - 1;
        }
        if (t > 1 - dt)
        {
            double x = (t - 1) / dt;
            return x * x + x + x + 1;
        }
        return 0;
    }

    /// <summary>The NES noise channel's 15-bit LFSR ("long" mode), held for a number of samples per step.</summary>
    public sealed class Lfsr
    {
        private int _state;

        public Lfsr(int seed) => _state = seed == 0 ? 1 : seed & 0x7FFF;

        public double Next()
        {
            int bit = (_state ^ (_state >> 1)) & 1;
            _state = (_state >> 1) | (bit << 14);
            return (_state & 1) != 0 ? 1 : -1;
        }

        /// <summary>A burst of noise: <paramref name="rate"/> new values per second, for at most <paramref name="seconds"/>, then silence.</summary>
        public double[] Burst(int count, double rate, double seconds)
        {
            var v = new double[count];
            int length = Math.Min(count, (int)(seconds * SampleRate));
            int hold = Math.Max(1, (int)(SampleRate / rate));
            double current = 0;
            for (int i = 0; i < length; i++)
            {
                if (i % hold == 0) current = Next();
                v[i] = current;
            }
            return v;
        }
    }
}
