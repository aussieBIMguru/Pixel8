namespace Pixel8.Desktop.Ui;

/// <summary>
/// Developer debug mode (round 8). Hidden: on the title screen, keep pressing Down on the last menu item
/// (<see cref="UnlockPresses"/> times) to reveal a "DEBUG MODE" toggle. Nothing here is saved: both the unlock
/// and the toggle last only until the game is closed. While it's on, <see cref="Key"/> opens the debug panel
/// on the map or in a fight.
/// </summary>
public static class DebugMode
{
    public const int UnlockPresses = 20;
    public const Microsoft.Xna.Framework.Input.Keys Key = Microsoft.Xna.Framework.Input.Keys.F9;
    public const string KeyName = "F9";

    /// <summary>The toggle has been revealed on the title screen this session.</summary>
    public static bool Unlocked { get; set; }

    /// <summary>Debug commands are available (the title-screen toggle).</summary>
    public static bool Enabled { get; set; }

    /// <summary>Walking onto "?" events and traps just clears them (debug panel toggle).</summary>
    public static bool SkipEvents { get; set; }

    /// <summary>Walking onto normal and elite fights just clears them; bosses still fight (debug panel toggle).</summary>
    public static bool SkipFights { get; set; }
}
