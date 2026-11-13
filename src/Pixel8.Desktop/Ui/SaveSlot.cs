using System.Text.Json;
using Pixel8.Core.Content;
using Pixel8.Core.Runs;

namespace Pixel8.Desktop.Ui;

/// <summary>
/// The single save slot: %LocalAppData%/Pixel8/save.json. Written on the map between moves, deleted when a run ends.
/// A broken or out-of-date file is treated as no save.
/// </summary>
public static class SaveSlot
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pixel8", "save.json");

    public static bool Exists => File.Exists(FilePath);

    public static RunSave? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var save = JsonSerializer.Deserialize<RunSave>(File.ReadAllText(FilePath), ContentDb.JsonOptions);
            return save is { Version: RunSave.CurrentVersion } ? save : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void Write(RunSave save)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(save, ContentDb.JsonOptions));
            File.Move(tmp, FilePath, overwrite: true);   // never leave a half-written save
        }
        catch (Exception)
        {
            // A save that can't be written just means no Continue next time.
        }
    }

    public static void Delete()
    {
        try
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }
        catch (Exception)
        {
            // ignore
        }
    }
}
