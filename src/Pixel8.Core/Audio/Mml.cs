using System.Globalization;
using System.Text;

namespace Pixel8.Core.Audio;

/// <summary>One note (or drum hit) on a chip channel. Times are in beats (quarter notes).</summary>
/// <param name="Pitch">MIDI note number (60 = middle C, o4 c); 0 for drums.</param>
/// <param name="Drum">'k' kick, 's' snare, 'h' hi-hat, 'x' crash; '\0' for pitched notes.</param>
/// <param name="Volume">0–15.</param>
/// <param name="Duty">Pulse duty: 0 = 12.5%, 1 = 25%, 2 = 50%, 3 = 75%.</param>
/// <param name="Gate">How much of the note sounds, in eighths (q7 = 7/8, legato q8).</param>
public sealed record ChipNote(double Start, double Length, int Pitch, char Drum, int Volume, int Duty, int Gate);

/// <summary>A parsed track: four NES-style channels (A, B = pulse, T = triangle, N = noise drums).</summary>
public sealed class ChipSong
{
    public const string Channels = "ABTN";

    public string Name { get; init; } = "";
    public string Title { get; init; } = "";
    public double Tempo { get; init; } = 120;
    public bool Loop { get; init; } = true;
    public IReadOnlyDictionary<char, IReadOnlyList<ChipNote>> Notes { get; init; } = new Dictionary<char, IReadOnlyList<ChipNote>>();
    /// <summary>Length of each channel in beats.</summary>
    public IReadOnlyDictionary<char, double> Lengths { get; init; } = new Dictionary<char, double>();
    /// <summary>Timing problems: channels of different lengths in a loop, bar lines (|) that don't fall on a 4/4 bar.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public double Beats => Lengths.Count == 0 ? 0 : Lengths.Values.Max();
    public double Seconds => Beats * 60.0 / Tempo;
}

/// <summary>
/// Parses Pixel8's music text format (round 9), a small MML dialect. See data/music/README.md.
/// <code>
/// title: Torchlit Halls      header lines: title, tempo (quarter-note BPM), loop (yes/no)
/// A: @1 v10 l8 o5 a4 e a b4 >c4&lt; | ...     channel lines (A, B pulse; T triangle; N noise); lines for a channel join up
/// </code>
/// Notes c d e f g a b, + sharp / - flat, then a length (1 2 4 8 16 32, default from l) and dots. r = rest,
/// ^len = extend the last note. o octave, &gt; &lt; octave up/down, l default length, v volume 0–15, @ duty 0–3,
/// q gate 1–8. [ ... ]n repeats (default 2, nestable). | is a bar line, checked against 4/4. # starts a comment.
/// On N, notes are drums: k kick, s snare, h hat, x crash.
/// </summary>
public static class Mml
{
    private static readonly Dictionary<char, int> NoteOffsets = new()
    {
        ['c'] = 0, ['d'] = 2, ['e'] = 4, ['f'] = 5, ['g'] = 7, ['a'] = 9, ['b'] = 11
    };

    private const string Drums = "kshx";

    public static ChipSong Parse(string text, string name = "")
    {
        var header = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sources = ChipSong.Channels.ToDictionary(c => c, _ => new StringBuilder());

        int lineNo = 0;
        foreach (var rawLine in text.Split('\n'))
        {
            lineNo++;
            string line = rawLine;
            int hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash];
            line = line.Trim();
            if (line.Length == 0) continue;

            int colon = line.IndexOf(':');
            if (colon <= 0) throw new FormatException($"{name} line {lineNo}: expected 'key: value' or 'A: notes'.");
            string key = line[..colon].Trim();
            string value = line[(colon + 1)..];
            if (key.Length == 1 && ChipSong.Channels.Contains(char.ToUpperInvariant(key[0])))
                sources[char.ToUpperInvariant(key[0])].Append(' ').Append(value);
            else
                header[key] = value.Trim();
        }

        double tempo = header.TryGetValue("tempo", out var t) && double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var bpm) && bpm > 0 ? bpm : 120;
        bool loop = !header.TryGetValue("loop", out var l) || l.Trim().ToLowerInvariant() is "yes" or "true" or "1";

        var notes = new Dictionary<char, IReadOnlyList<ChipNote>>();
        var lengths = new Dictionary<char, double>();
        var warnings = new List<string>();
        foreach (var (ch, sb) in sources)
        {
            var (list, beats, bars) = ParseChannel(ExpandRepeats(sb.ToString(), name, ch), ch, name);
            notes[ch] = list;
            lengths[ch] = beats;
            foreach (double bar in bars)
                if (Math.Abs(bar / 4 - Math.Round(bar / 4)) > 1e-9)
                    warnings.Add($"{ch}: bar line at beat {bar:0.##} isn't on a 4/4 bar.");
        }

        var used = lengths.Where(kv => kv.Value > 0).Select(kv => kv.Value).Distinct().ToList();
        if (loop && used.Count > 1)
            warnings.Add("Channel lengths differ: " + string.Join(", ", lengths.Select(kv => $"{kv.Key}={kv.Value:0.##}")));
        if (loop && used.Count > 0 && Math.Abs(used.Max() % 4) > 1e-9)
            warnings.Add($"The loop is {used.Max():0.##} beats, not whole 4/4 bars.");

        return new ChipSong
        {
            Name = name,
            Title = header.GetValueOrDefault("title", name),
            Tempo = tempo,
            Loop = loop,
            Notes = notes,
            Lengths = lengths,
            Warnings = warnings
        };
    }

    /// <summary>Expands [ ... ]n (innermost first). n defaults to 2.</summary>
    private static string ExpandRepeats(string src, string name, char ch)
    {
        while (true)
        {
            int close = src.IndexOf(']');
            if (close < 0)
            {
                if (src.Contains('[')) throw new FormatException($"{name} {ch}: '[' without ']'.");
                return src;
            }
            int open = src.LastIndexOf('[', close);
            if (open < 0) throw new FormatException($"{name} {ch}: ']' without '['.");
            int j = close + 1;
            while (j < src.Length && char.IsWhiteSpace(src[j])) j++;
            int k = j;
            while (k < src.Length && char.IsDigit(src[k])) k++;
            int count = 2, tail = close + 1;
            if (k > j)
            {
                count = int.Parse(src[j..k], CultureInfo.InvariantCulture);
                tail = k;
            }
            string body = src[(open + 1)..close];
            string expanded = string.Join(" ", Enumerable.Repeat(body, Math.Max(0, count)));
            src = src[..open] + " " + expanded + " " + src[tail..];
        }
    }

    private static (List<ChipNote> Notes, double Beats, List<double> Bars) ParseChannel(string src, char ch, string name)
    {
        int i = 0, n = src.Length;
        int octave = 4, length = 4, volume = 15, duty = 2, gate = 7;
        double time = 0;
        var notes = new List<ChipNote>();
        var bars = new List<double>();

        int? Number()
        {
            int start = i;
            while (i < n && char.IsDigit(src[i])) i++;
            return i > start ? int.Parse(src[start..i], CultureInfo.InvariantCulture) : null;
        }

        int Require(char command) =>
            Number() ?? throw new FormatException($"{name} {ch}: '{command}' needs a number (at {i}).");

        double Duration()
        {
            int len = Number() ?? length;
            if (len <= 0) throw new FormatException($"{name} {ch}: length must be positive (at {i}).");
            double beats = 4.0 / len, add = beats;
            while (i < n && src[i] == '.')
            {
                add /= 2;
                beats += add;
                i++;
            }
            return beats;
        }

        while (i < n)
        {
            char c = char.ToLowerInvariant(src[i++]);
            if (char.IsWhiteSpace(c)) continue;
            if (c == '|') { bars.Add(time); continue; }

            if (ch == 'N' && Drums.Contains(c))
            {
                double d = Duration();
                notes.Add(new ChipNote(time, d, 0, c, volume, duty, gate));
                time += d;
                continue;
            }
            if (ch != 'N' && NoteOffsets.TryGetValue(c, out int offset))
            {
                int pitch = offset;
                while (i < n && src[i] is '+' or '-') pitch += src[i++] == '+' ? 1 : -1;
                double d = Duration();
                notes.Add(new ChipNote(time, d, 12 * (octave + 1) + pitch, '\0', volume, duty, gate));
                time += d;
                continue;
            }

            switch (c)
            {
                case 'r': time += Duration(); break;
                case '^':
                {
                    double d = Duration();
                    if (notes.Count > 0) notes[^1] = notes[^1] with { Length = notes[^1].Length + d };
                    time += d;
                    break;
                }
                case 'o': octave = Require(c); break;
                case '>': octave++; break;
                case '<': octave--; break;
                case 'l': length = Math.Max(1, Require(c)); break;
                case 'v': volume = Math.Clamp(Require(c), 0, 15); break;
                case '@': duty = Math.Clamp(Require(c), 0, 3); break;
                case 'q': gate = Math.Clamp(Require(c), 1, 8); break;
                default: throw new FormatException($"{name} {ch}: unexpected '{c}' (at {i}).");
            }
        }
        return (notes, time, bars);
    }
}
