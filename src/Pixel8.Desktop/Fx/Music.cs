using Microsoft.Xna.Framework.Audio;
using Pixel8.Core.Audio;

namespace Pixel8.Desktop.Fx;

/// <summary>
/// Background music (round 9): the tracks in data/music/*.mml are rendered by <see cref="ChipSynth"/> on a background
/// thread at startup, then played as looping sound effects with a short crossfade. Stings (victory, defeat) play
/// once over a ducked loop. Screens just say which track they want with <see cref="Play"/>; asking for the track
/// that's already playing does nothing, so it's safe to call every time a screen opens.
/// </summary>
public static class Music
{
    public const float FadeSeconds = 0.6f;

    private static readonly Dictionary<string, short[]> Rendered = new();
    private static readonly Dictionary<string, SoundEffect> Tracks = new();
    private static readonly HashSet<string> Loops = new();
    private static readonly object Gate = new();
    private static Task? _loading;
    private static bool _available = true;

    private static SoundEffectInstance? _current, _fading, _sting;
    private static string? _currentName, _wanted;
    private static float _currentVol, _fadingVol;

    /// <summary>Master switch (the title screen's Sound setting).</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>Music volume 0..1 (title-screen setting).</summary>
    public static float Volume { get; set; } = 0.5f;

    /// <summary>The track that should be playing (even while it's still rendering).</summary>
    public static string? Wanted => _wanted;

    /// <summary>Starts rendering every .mml track in <paramref name="musicDirectory"/> in the background.</summary>
    public static void Load(string musicDirectory)
    {
        if (!Directory.Exists(musicDirectory)) return;
        var files = Directory.GetFiles(musicDirectory, "*.mml").OrderBy(f => f, StringComparer.Ordinal).ToList();
        _loading = Task.Run(() =>
        {
            foreach (var file in files)
            {
                try
                {
                    var song = Mml.Parse(File.ReadAllText(file), Path.GetFileNameWithoutExtension(file));
                    var pcm = ChipSynth.Render(song);
                    lock (Gate)
                    {
                        Rendered[song.Name] = pcm;
                        if (song.Loop) Loops.Add(song.Name);
                    }
                }
                catch (Exception ex) when (ex is FormatException or IOException)
                {
                    System.Diagnostics.Debug.WriteLine($"Music: {Path.GetFileName(file)}: {ex.Message}");
                }
            }
        });
    }

    /// <summary>Plays a looping track (crossfading from the current one). Null fades to silence.</summary>
    public static void Play(string? name) => _wanted = name;

    /// <summary>Plays a one-shot sting (victory, defeat) over the music, which ducks while it plays.</summary>
    public static void Sting(string name)
    {
        if (!Enabled || !_available) return;
        var effect = Effect(name);
        if (effect is null) return;
        try
        {
            _sting?.Stop();
            _sting = effect.CreateInstance();
            _sting.Volume = Math.Clamp(Volume, 0, 1);
            _sting.Play();
        }
        catch (Exception)
        {
            _sting = null;
        }
    }

    /// <summary>Call once per frame: builds rendered tracks into sound effects and runs the crossfade.</summary>
    public static void Update(float dt)
    {
        if (!_available) return;
        try
        {
            string? target = Enabled && Volume > 0 ? _wanted : null;
            if (target != _currentName)
            {
                var effect = target is null ? null : Effect(target);
                if (target is null || effect is not null)
                {
                    // Start the crossfade: the current loop becomes the fading one.
                    _fading?.Stop();
                    _fading = _current;
                    _fadingVol = _currentVol;
                    _current = null;
                    _currentVol = 0;
                    _currentName = target;
                    if (effect is not null)
                    {
                        _current = effect.CreateInstance();
                        _current.IsLooped = Loops.Contains(target!);
                        _current.Volume = 0;
                        _current.Play();
                    }
                }
            }

            float step = dt / FadeSeconds;
            bool stingPlaying = _sting is { State: SoundState.Playing };
            float level = Math.Clamp(Volume, 0, 1) * (stingPlaying ? 0.25f : 1f);
            if (_current is not null)
            {
                _currentVol = Math.Min(1, _currentVol + step);
                _current.Volume = Math.Clamp(_currentVol * level, 0, 1);
            }
            if (_fading is not null)
            {
                _fadingVol -= step;
                if (_fadingVol <= 0)
                {
                    _fading.Stop();
                    _fading.Dispose();
                    _fading = null;
                }
                else _fading.Volume = Math.Clamp(_fadingVol * level, 0, 1);
            }
            if (_sting is not null)
            {
                if (!Enabled) _sting.Stop();
                if (_sting.State == SoundState.Stopped)
                {
                    _sting.Dispose();
                    _sting = null;
                }
            }
        }
        catch (Exception)
        {
            // NoAudioHardwareException and friends: no music this session.
            _available = false;
        }
    }

    /// <summary>The sound effect for a track, built on this (the game) thread once its samples are rendered.</summary>
    private static SoundEffect? Effect(string name)
    {
        if (Tracks.TryGetValue(name, out var effect)) return effect;
        short[]? pcm;
        lock (Gate) Rendered.TryGetValue(name, out pcm);
        if (pcm is null) return null;
        var bytes = new byte[pcm.Length * 2];
        Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
        effect = new SoundEffect(bytes, ChipSynth.SampleRate, AudioChannels.Mono);
        Tracks[name] = effect;
        lock (Gate) Rendered.Remove(name);   // the SoundEffect keeps its own copy
        return effect;
    }

    public static void Unload()
    {
        try { _loading?.Wait(2000); } catch (AggregateException) { }
        foreach (var i in new[] { _current, _fading, _sting }) i?.Dispose();
        _current = _fading = _sting = null;
        foreach (var t in Tracks.Values) t.Dispose();
        Tracks.Clear();
    }
}
