using System.Text.Json;

namespace Pixel8.Desktop.Ui;

/// <summary>
/// Player options chosen on the title screen, kept between sessions in
/// %LocalAppData%/Pixel8/settings.json. A missing or broken file just means defaults.
/// </summary>
public sealed class Settings
{
    /// <summary>Before round 9: one on/off switch. Kept so old settings files still load; volumes replace it.</summary>
    public bool Sound { get; set; } = true;
    /// <summary>Music volume, 0 (off) to 10 (round 9).</summary>
    public int MusicVolume { get; set; } = 5;
    /// <summary>Sound effects volume, 0 (off) to 10 (round 9).</summary>
    public int SfxVolume { get; set; } = 6;
    /// <summary>Timed reactions on big attacks. Off = every big hit is graded Ok (−25%).</summary>
    public bool ReactEvents { get; set; } = true;
    public string Difficulty { get; set; } = "normal";
    /// <summary>Where the merchant is: "OnMap" (a tile on each Depth), "AtCampfires" or "Anytime" (M on the map).</summary>
    public string Shop { get; set; } = "OnMap";
    public List<string>? LastParty { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pixel8", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
                // A pre-round-9 file with Sound off: start with both volumes off.
                if (!settings.Sound)
                {
                    settings.MusicVolume = settings.SfxVolume = 0;
                    settings.Sound = true;
                }
                settings.MusicVolume = Math.Clamp(settings.MusicVolume, 0, 10);
                settings.SfxVolume = Math.Clamp(settings.SfxVolume, 0, 10);
                return settings;
            }
        }
        catch (Exception)
        {
            // Unreadable settings are not worth crashing over.
        }
        return new Settings();
    }

    /// <summary>Pushes the volume settings into the audio systems.</summary>
    public void ApplyAudio()
    {
        Pixel8.Desktop.Fx.Sfx.Volume = SfxVolume / 10f;
        Pixel8.Desktop.Fx.Sfx.Enabled = SfxVolume > 0;
        Pixel8.Desktop.Fx.Music.Volume = MusicVolume / 10f;
        Pixel8.Desktop.Fx.Music.Enabled = MusicVolume > 0;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // Read-only profile etc.: settings simply won't persist.
        }
    }
}
