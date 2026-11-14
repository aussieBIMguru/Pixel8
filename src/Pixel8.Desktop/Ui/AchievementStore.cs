using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Pixel8.Core.Achievements;
using Pixel8.Desktop.Fx;
using Pixel8.Desktop.Rendering;

namespace Pixel8.Desktop.Ui;

/// <summary>
/// Achievements on this machine (round 10): %LocalAppData%/Pixel8/achievements.json. A missing or broken file
/// just starts fresh. One tracker for the whole session.
/// </summary>
public static class AchievementStore
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pixel8", "achievements.json");

    private static AchievementTracker? _tracker;

    public static AchievementTracker Tracker => _tracker ??= new AchievementTracker(Load());

    private static AchievementState Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AchievementState>(File.ReadAllText(FilePath)) ?? new AchievementState();
        }
        catch (Exception)
        {
            // Unreadable: start fresh rather than crash.
        }
        return new AchievementState();
    }

    /// <summary>Saves if anything changed, and shows a toast for each new unlock.</summary>
    public static void Report(IEnumerable<AchievementDef> unlocked)
    {
        foreach (var a in unlocked) Toasts.Show(a);
        if (!Tracker.Dirty) return;
        Tracker.Dirty = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Tracker.State, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // Read-only profile: achievements won't persist this session.
        }
    }
}

/// <summary>Achievement toasts: slide down from the top, hold, slide away. One at a time, queued. Drawn over every screen.</summary>
public static class Toasts
{
    private const float In = 0.25f, Hold = 2.6f, Out = 0.3f;
    private static readonly Queue<AchievementDef> Pending = new();
    private static AchievementDef? _current;
    private static float _t;

    public static void Show(AchievementDef a) => Pending.Enqueue(a);

    public static void Update(float dt)
    {
        if (_current is null)
        {
            if (Pending.Count == 0) return;
            _current = Pending.Dequeue();
            _t = 0;
            Sfx.Play("achievement");
        }
        _t += dt;
        if (_t > In + Hold + Out) _current = null;
    }

    public static void Draw(SpriteBatch b, PixelCanvas c, SpriteSheet s)
    {
        if (_current is null) return;
        float slide = _t < In ? _t / In : _t > In + Hold ? 1 - (_t - In - Hold) / Out : 1;
        var r = new Rectangle(40, -20 + (int)(22 * Math.Clamp(slide, 0, 1)), 176, 19);
        Gui.Panel(b, c, r, Palette.DarkBlue, Palette.Yellow);
        // Round 11: the achievement's icon on the left (and right) instead of stars.
        Pixel8.Desktop.Screens.AchievementsScreen.DrawIcon(b, s, _current, r.X + 4, r.Y + 5, true);
        Pixel8.Desktop.Screens.AchievementsScreen.DrawIcon(b, s, _current, r.Right - 12, r.Y + 5, true);
        Gui.TextCenter(b, c, "ACHIEVEMENT: " + Gui.Clip(_current.Name.ToUpperInvariant(), 24), r.Center.X, r.Y + 3, Palette.Yellow);
        Gui.TextCenter(b, c, Gui.Clip(_current.Description.ToUpperInvariant(), 35), r.Center.X, r.Y + 11, Palette.White);
    }
}
