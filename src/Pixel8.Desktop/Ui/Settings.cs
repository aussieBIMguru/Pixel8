using System.Text.Json;

namespace Pixel8.Desktop.Ui;

/// <summary>
/// Player options chosen on the title screen, kept between sessions in
/// %LocalAppData%/Pixel8/settings.json. A missing or broken file just means defaults.
/// </summary>
public sealed class Settings
{
    public bool Sound { get; set; } = true;
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
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception)
        {
            // Unreadable settings are not worth crashing over.
        }
        return new Settings();
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
