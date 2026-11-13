using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Pixel8.Desktop.Rendering;

/// <summary>
/// Everything is drawn into a fixed 320×180 render target, then blown up by the largest
/// whole-number scale that fits the window (×4 at 720p, ×6 at 1080p, ×8 at 1440p) with
/// point sampling, so pixels stay perfectly square and crisp. Any remainder is letterboxed.
/// </summary>
public sealed class PixelCanvas : IDisposable
{
    public const int Width = 320;
    public const int Height = 180;

    private readonly GraphicsDevice _device;
    private readonly RenderTarget2D _target;

    public PixelCanvas(GraphicsDevice device)
    {
        _device = device;
        _target = new RenderTarget2D(device, Width, Height);
        Pixel = new Texture2D(device, 1, 1);
        Pixel.SetData(new[] { Color.White });
    }

    /// <summary>1×1 white texture for rectangles and the tiny font.</summary>
    public Texture2D Pixel { get; }

    /// <summary>Where the canvas landed on the back buffer last frame.</summary>
    public Rectangle Destination { get; private set; }

    public void Begin(SpriteBatch batch, Color clear, Point shake = default)
    {
        _device.SetRenderTarget(_target);
        _device.Clear(clear);
        batch.Begin(
            samplerState: SamplerState.PointClamp,
            transformMatrix: Matrix.CreateTranslation(shake.X, shake.Y, 0));
    }

    public void End(SpriteBatch batch)
    {
        batch.End();
        _device.SetRenderTarget(null);
        _device.Clear(Color.Black);

        var pp = _device.PresentationParameters;
        int scale = Math.Max(1, Math.Min(pp.BackBufferWidth / Width, pp.BackBufferHeight / Height));
        int w = Width * scale, h = Height * scale;
        Destination = new Rectangle((pp.BackBufferWidth - w) / 2, (pp.BackBufferHeight - h) / 2, w, h);

        batch.Begin(samplerState: SamplerState.PointClamp);
        batch.Draw(_target, Destination, Color.White);
        batch.End();
    }

    /// <summary>Window/mouse coordinates → canvas pixels.</summary>
    public Point ToCanvas(Point screen)
    {
        if (Destination.Width == 0) return new Point(-1, -1);
        return new Point(
            (screen.X - Destination.X) * Width / Destination.Width,
            (screen.Y - Destination.Y) * Height / Destination.Height);
    }

    public void Fill(SpriteBatch batch, Rectangle r, Color c) => batch.Draw(Pixel, r, c);

    public void Fill(SpriteBatch batch, int x, int y, int w, int h, Color c) =>
        batch.Draw(Pixel, new Rectangle(x, y, w, h), c);

    public void Outline(SpriteBatch batch, Rectangle r, Color c)
    {
        Fill(batch, r.X, r.Y, r.Width, 1, c);
        Fill(batch, r.X, r.Bottom - 1, r.Width, 1, c);
        Fill(batch, r.X, r.Y, 1, r.Height, c);
        Fill(batch, r.Right - 1, r.Y, 1, r.Height, c);
    }

    public void Dispose()
    {
        _target.Dispose();
        Pixel.Dispose();
    }
}
