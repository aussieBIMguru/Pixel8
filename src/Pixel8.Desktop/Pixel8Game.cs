using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Pixel8.Desktop.Rendering;
using Pixel8.Desktop.Screens;

namespace Pixel8.Desktop;

public sealed class Pixel8Game : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch = null!;
    private PixelCanvas _canvas = null!;
    private MapScreen _map = null!;
    private KeyboardState _prevKeys;

    public Pixel8Game()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = PixelCanvas.Width * 4,   // 1280
            PreferredBackBufferHeight = PixelCanvas.Height * 4, // 720
            SynchronizeWithVerticalRetrace = true
        };
        Window.Title = "Pixel8";
        Window.AllowUserResizing = true;
        IsMouseVisible = true;
        Content.RootDirectory = "Content";
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _canvas = new PixelCanvas(GraphicsDevice);
        _map = new MapScreen((ulong)Environment.TickCount64);
    }

    protected override void Update(GameTime gameTime)
    {
        var keys = Keyboard.GetState();
        if (keys.IsKeyDown(Keys.Escape)) Exit();

        if (keys.IsKeyDown(Keys.F11) && !_prevKeys.IsKeyDown(Keys.F11))
        {
            _graphics.IsFullScreen = !_graphics.IsFullScreen;
            _graphics.ApplyChanges();
        }
        _prevKeys = keys;

        _map.Update(gameTime, _canvas);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        _canvas.Begin(_spriteBatch, Palette.Black, _map.ShakeOffset);
        _map.Draw(_spriteBatch, _canvas);
        _canvas.End(_spriteBatch);
        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        _canvas.Dispose();
        _spriteBatch.Dispose();
        base.UnloadContent();
    }
}
