using System.Text.Json;
using Pixel8.Core.Lore;

namespace Pixel8.Desktop.Ui;

/// <summary>
/// The Adventurer's Journal on this machine (round 11): %LocalAppData%/Pixel8/journal.json. A missing or broken file
/// just starts fresh. One tracker for the whole session; <see cref="SaveIfDirty"/> writes it when something new was seen.
/// </summary>
public static class JournalStore
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pixel8", "journal.json");

    private static JournalTracker? _tracker;

    public static JournalTracker Tracker => _tracker ??= new JournalTracker(Load());

    private static JournalState Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<JournalState>(File.ReadAllText(FilePath)) ?? new JournalState();
        }
        catch (Exception)
        {
            // Unreadable: start fresh rather than crash.
        }
        return new JournalState();
    }

    public static void SaveIfDirty()
    {
        if (!Tracker.Dirty) return;
        Tracker.Dirty = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Tracker.State, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // Read-only profile: the journal won't persist this session.
        }
    }
}
