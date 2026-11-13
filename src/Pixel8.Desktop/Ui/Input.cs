using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Pixel8.Desktop.Rendering;

namespace Pixel8.Desktop.Ui;

/// <summary>
/// Keyboard and mouse state for one frame, with edge detection, in canvas pixels.
/// Updated once per frame by the game so a click can never be seen by two screens.
/// </summary>
public sealed class Input
{
    private KeyboardState _keys, _prevKeys;
    private MouseState _mouse, _prevMouse;

    /// <summary>Mouse position on the 256×144 canvas.</summary>
    public Point Pointer { get; private set; }

    public void Update(PixelCanvas canvas, bool active)
    {
        _prevKeys = _keys;
        _prevMouse = _mouse;
        _keys = active ? Keyboard.GetState() : default;
        _mouse = active ? Microsoft.Xna.Framework.Input.Mouse.GetState() : _prevMouse;
        Pointer = canvas.ToCanvas(_mouse.Position);
    }

    public bool Pressed(Keys key) => _keys.IsKeyDown(key) && !_prevKeys.IsKeyDown(key);

    public bool PressedAny(params Keys[] keys) => keys.Any(Pressed);

    public bool Held(Keys key) => _keys.IsKeyDown(key);

    public bool LeftHeld => _mouse.LeftButton == ButtonState.Pressed;

    public bool LeftClick => _mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;

    public bool RightClick => _mouse.RightButton == ButtonState.Pressed && _prevMouse.RightButton == ButtonState.Released;

    public bool Over(Rectangle r) => r.Contains(Pointer);

    public bool Clicked(Rectangle r) => LeftClick && Over(r);

    /// <summary>Number keys 1–9 (top row or numpad) → 0-based index, or -1.</summary>
    public int PressedDigit()
    {
        for (int i = 0; i < 9; i++)
            if (Pressed(Keys.D1 + i) || Pressed(Keys.NumPad1 + i)) return i;
        return -1;
    }
}
