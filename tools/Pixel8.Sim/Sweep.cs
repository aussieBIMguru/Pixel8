using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pixel8.Sim;

/// <summary>
/// One data override: <c>path=value</c>. The path is dotted; the first segment may name a data file
/// (balance, enemies, cards, heroes, events, items), otherwise balance.json is meant. In a list of objects a
/// segment matches an "id" (enemies.young_green_dragon.hpScale); in a list of values it is an index
/// (acCapByDepth.2). Values are numbers, true/false, a list written with slashes (14/15/16), or text.
/// </summary>
public sealed record DataPatch(string File, string[] Path, string Raw)
{
    private static readonly string[] Files = { "balance", "enemies", "cards", "heroes", "events", "items" };

    public string Label => $"{(File == "balance" ? "" : File + ".")}{string.Join('.', Path)}={Raw}";

    public static DataPatch Parse(string path, string value)
    {
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) throw new ArgumentException($"Empty path in '{path}'.");
        string file = "balance";
        if (parts.Length > 1 && Files.Contains(parts[0], StringComparer.OrdinalIgnoreCase))
        {
            file = parts[0].ToLowerInvariant();
            parts = parts[1..];
        }
        return new DataPatch(file, parts, value);
    }

    public JsonNode Value()
    {
        if (Raw.Contains('/'))
            return new JsonArray(Raw.Split('/').Select(v => (JsonNode)Scalar(v)!).ToArray());
        return Scalar(Raw)!;
    }

    private static JsonNode? Scalar(string v)
    {
        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
            return d == Math.Floor(d) && !v.Contains('.') ? JsonValue.Create((long)d) : JsonValue.Create(d);
        if (bool.TryParse(v, out bool b)) return JsonValue.Create(b);
        return JsonValue.Create(v);
    }

    /// <summary>Applies the patch to a parsed data file. Throws with a readable message when the path doesn't exist.</summary>
    public void ApplyTo(JsonNode root)
    {
        JsonNode node = root;
        // enemies.json etc. wrap their list: { "enemies": [...] }. Step into it when the path starts with an id.
        if (File != "balance" && node is JsonObject wrapper && wrapper[File] is JsonArray && wrapper[Path[0]] is null)
            node = wrapper[File]!;

        for (int i = 0; i < Path.Length; i++)
        {
            string seg = Path[i];
            bool last = i == Path.Length - 1;
            switch (node)
            {
                case JsonObject obj:
                {
                    string key = obj.Select(kv => kv.Key).FirstOrDefault(k => k.Equals(seg, StringComparison.OrdinalIgnoreCase)) ?? seg;
                    if (last) { obj[key] = Value(); return; }
                    node = obj[key] ?? (obj[key] = new JsonObject());
                    break;
                }
                case JsonArray arr when int.TryParse(seg, out int index):
                    if (index < 0 || index >= arr.Count) throw new ArgumentException($"{Label}: index {index} is out of range.");
                    if (last) { arr[index] = Value(); return; }
                    node = arr[index]!;
                    break;
                case JsonArray arr:
                {
                    var match = arr.OfType<JsonObject>().FirstOrDefault(o =>
                        string.Equals(o["id"]?.GetValue<string>(), seg, StringComparison.OrdinalIgnoreCase));
                    node = match ?? throw new ArgumentException($"{Label}: no item with id '{seg}'.");
                    if (last) throw new ArgumentException($"{Label}: the path ends on an item, not a value.");
                    break;
                }
                default:
                    throw new ArgumentException($"{Label}: '{seg}' isn't inside an object or list.");
            }
        }
    }
}

/// <summary>Builds a patched copy of the data folder in a temp directory for one sweep combination.</summary>
public static class DataFolder
{
    private static readonly JsonSerializerOptions Write = new() { WriteIndented = true };

    public static string Patched(string source, IReadOnlyList<DataPatch> patches)
    {
        string dir = Path.Combine(Path.GetTempPath(), "pixel8-sim", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        foreach (var file in Directory.GetFiles(source, "*.json"))
            File.Copy(file, Path.Combine(dir, Path.GetFileName(file)));

        foreach (var group in patches.GroupBy(p => p.File))
        {
            string path = Path.Combine(dir, group.Key + ".json");
            var root = File.Exists(path)
                ? JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!
                : new JsonObject();
            foreach (var patch in group) patch.ApplyTo(root);
            File.WriteAllText(path, root.ToJsonString(Write));
        }
        return dir;
    }

    public static void Delete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
