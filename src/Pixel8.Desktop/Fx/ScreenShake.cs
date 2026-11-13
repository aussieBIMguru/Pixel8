using Microsoft.Xna.Framework;

namespace Pixel8.Desktop.Fx;

/// <summary>Whole-pixel screen shake. Uses System.Random on purpose: visuals must never touch the rules RNG.</summary>
public sealed class ScreenShake
{
    private readonly Random _random = new();
    private float _time;
    private float _duration;
    private int _intensity;

    public Point Offset { get; private set; }

    public void Trigger(int intensityPixels, float seconds)
    {
        if (intensityPixels < _intensity && _time > 0) return;
        _intensity = intensityPixels;
        _duration = seconds;
        _time = seconds;
    }

    public void Update(float dt)
    {
        if (_time <= 0)
        {
            Offset = Point.Zero;
            return;
        }
        _time -= dt;
        int amp = Math.Max(1, (int)Math.Ceiling(_intensity * (_time / _duration)));
        Offset = new Point(_random.Next(-amp, amp + 1), _random.Next(-amp, amp + 1));
    }
}
