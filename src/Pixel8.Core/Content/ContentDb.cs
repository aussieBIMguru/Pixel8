using System.Text.Json;
using System.Text.Json.Serialization;
using Pixel8.Core.Cards;
using Pixel8.Core.Dice;
using Pixel8.Core.Effects;

namespace Pixel8.Core.Content;

/// <summary>A playable class. Lives in data/heroes.json.</summary>
public sealed record HeroClassDef(
    string Id,
    string Name,
    int Hp,
    int Ac,
    int AttackMod,
    string Weapon,
    int InitMod = 0,
    int SpellDc = 10,
    IReadOnlyList<string>? StartingDeck = null,
    string? BasicAction = null,
    string? WeaponFx = null);

public enum IntentKind { Attack, Guard }

/// <summary>
/// One thing an enemy can do on its turn. Picked by weight at the start of each round and shown to the player.
/// <paramref name="Big"/> marks a heavy attack (shown with a bolt): when it hits, the defender gets a timed
/// reaction (Perfect = dodge, Good = half, Ok = −25%, Miss = full).
/// </summary>
public sealed record IntentDef(
    string Name,
    IntentKind Kind = IntentKind.Attack,
    int AttackBonus = 0,
    string Damage = "0",
    int Block = 0,
    int Weight = 1,
    bool Big = false,
    string? Fx = null);

/// <summary>
/// A 5e SRD 5.1 creature (CC BY 4.0). Lives in data/enemies.json. Stats stay as published;
/// game-side scaling (e.g. <see cref="BalanceDef.EnemyHpMultiplier"/>) is applied when spawning.
/// MinDepth keeps tougher normal enemies (Orc) out of Depth 1.
/// </summary>
public sealed record EnemyDef(
    string Id,
    string Name,
    int Hp,
    int Ac,
    IReadOnlyList<IntentDef> Intents,
    int InitMod = 0,
    int SaveMod = 0,
    int Gold = 0,
    int Tier = 1,
    int MinDepth = 1,
    int Xp = 0);

/// <summary>An event option's d20 check: the best hero rolls d20 + mod ("atk", "dex", "spell" or "none") vs DC.</summary>
public sealed record EventCheck(string Mod = "none", int Dc = 10);

/// <summary>
/// One result of an event option. Type: gold | luck | torch | xp (amount, may be negative) · heal (amount = fraction
/// of max HP, e.g. "0.25") · maxHp (amount) · damage (dice; target "random" or "party", never downs a hero) ·
/// item (id) · upgradeRandom · addCard · removeCard (the player chooses).
/// </summary>
public sealed record EventOutcome(string Type, string? Amount = null, string? Dice = null, string Target = "party", string? Id = null);

/// <summary>A choice on an event. Gold/Torch are paid up front; without a Check, Success always applies.</summary>
public sealed record EventOption(
    string Label,
    string Text = "",
    int Gold = 0,
    int Torch = 0,
    EventCheck? Check = null,
    IReadOnlyList<EventOutcome>? Success = null,
    IReadOnlyList<EventOutcome>? Failure = null);

/// <summary>A map event ("?" tile). Lives in data/events.json.</summary>
public sealed record EventDef(string Id, string Title, string Text, IReadOnlyList<EventOption> Options, int MinDepth = 1, int Weight = 1);

/// <summary>A consumable that goes in the party Bag. Lives in data/items.json.</summary>
public sealed record ItemDef(string Id, string Name, string Text, IReadOnlyList<EffectDef> Effects, int Price = 0);

/// <summary>
/// A difficulty preset, applied on top of the balance numbers when a run starts.
/// Normal is all 1s/0s. Presets live in balance.json under "difficulties".
/// </summary>
public sealed class DifficultyDef
{
    public string Name { get; set; } = "Normal";
    public string Description { get; set; } = "";
    /// <summary>Hero max HP × this.</summary>
    public double HeroHpMultiplier { get; set; } = 1;
    /// <summary>Enemy max HP × this (on top of <see cref="BalanceDef.EnemyHpMultiplier"/>).</summary>
    public double EnemyHpMultiplier { get; set; } = 1;
    /// <summary>Damage from enemy attacks × this (rounded, minimum 1).</summary>
    public double EnemyDamageMultiplier { get; set; } = 1;
    /// <summary>Added to every enemy attack roll (and to the odds shown on intents).</summary>
    public int EnemyAttackBonus { get; set; }
    /// <summary>Added to normal-fight group size (still 1–4).</summary>
    public int GroupSizeBonus { get; set; }
    /// <summary>After a win, each standing hero heals this fraction of max HP.</summary>
    public double VictoryHealFraction { get; set; }
    /// <summary>
    /// Timed reactions: width of the Good zone as a fraction of the bar (Perfect is the middle third of it,
    /// Ok reaches twice as far). Easy 0.30, Normal 0.20, Hard 0.14.
    /// </summary>
    public double TimingWindow { get; set; } = 0.20;
}

/// <summary>Merchant prices (balance.json "prices"). Card prices are keyed by <see cref="Rarity"/> name.</summary>
public sealed class PriceDef
{
    public Dictionary<string, int> Cards { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Common"] = 25, ["Uncommon"] = 40, ["Rare"] = 65, ["Legendary"] = 100
    };
    /// <summary>Remove one card from a hero's deck (once per merchant visit).</summary>
    public int RemoveCard { get; set; } = 35;
    /// <summary>Upgrade one card (once per merchant visit).</summary>
    public int UpgradeCard { get; set; } = 40;
    /// <summary>How many of each item the merchant stocks.</summary>
    public int ItemStock { get; set; } = 2;

    public int CardPrice(Rarity rarity) => Cards.TryGetValue(rarity.ToString(), out int p) ? p : 50;
}

/// <summary>Tunable numbers. Lives in data/balance.json; every value has a default so the file is optional.</summary>
public sealed class BalanceDef
{
    public int MaxHand { get; set; } = 5;
    public int SecondWindDraw { get; set; } = 1;
    public int StartingGold { get; set; } = 0;
    public int StartingLuck { get; set; } = 3;
    /// <summary>How many Luck rerolls the party may spend in one fight (the rest wait for the next fight).</summary>
    public int LuckRerollsPerFight { get; set; } = 1;
    public int FleeTorchCost { get; set; } = 2;
    public int FleeGoldCost { get; set; } = 10;
    public int SkipDraftGold { get; set; } = 15;

    /// <summary>All heroes act before any enemy each round (initiative only orders within a side),
    /// so the party always sees enemy intents before they land.</summary>
    public bool HeroesActFirst { get; set; } = true;

    /// <summary>Enemy max HP = SRD HP × this, rounded. Heroes are sturdier than 5e level 1, so enemies are too.</summary>
    public double EnemyHpMultiplier { get; set; } = 1.5;

    /// <summary>Normal fights roll NormalGroupMin..NormalGroupMax enemies, +1 per Depth after the first (max 4).</summary>
    public int NormalGroupMin { get; set; } = 2;
    public int NormalGroupMax { get; set; } = 3;

    /// <summary>Draft weights keyed by <see cref="Rarity"/> name.</summary>
    public Dictionary<string, int> RarityWeights { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Common"] = 70, ["Uncommon"] = 25, ["Rare"] = 5, ["Legendary"] = 0
    };

    /// <summary>Party XP per enemy by tier (index = tier; tier 0 unused) when an enemy has no "xp" of its own.</summary>
    public List<int> XpByTier { get; set; } = new() { 0, 4, 12, 30 };

    /// <summary>Total party XP needed for each level: index 0 = level 1 (0 XP), index 1 = level 2, …</summary>
    public List<int> LevelXp { get; set; } = new() { 0, 35, 85, 155, 245, 355, 490, 650 };

    /// <summary>Level-up reward "+HP": max HP (and current HP) gained.</summary>
    public int LevelUpHp { get; set; } = 4;

    /// <summary>A level-up or event can't remove a card from a deck this small.</summary>
    public int MinDeckSize { get; set; } = 4;

    public int XpOf(EnemyDef def) =>
        def.Xp > 0 ? def.Xp : XpByTier.Count == 0 ? 0 : XpByTier[Math.Clamp(def.Tier, 0, XpByTier.Count - 1)];

    /// <summary>Total XP needed to reach <paramref name="level"/>; past the table, each level costs the last step again.</summary>
    public int XpForLevel(int level)
    {
        if (level <= 1 || LevelXp.Count == 0) return 0;
        if (level - 1 < LevelXp.Count) return LevelXp[level - 1];
        int last = LevelXp[^1];
        int step = LevelXp.Count >= 2 ? Math.Max(1, LevelXp[^1] - LevelXp[^2]) : 100;
        return last + step * (level - LevelXp.Count);
    }

    /// <summary>Campfires: Torch added when the party stops at a fire (whether they rest or upgrade).</summary>
    public int CampfireTorch { get; set; } = 6;

    public PriceDef Prices { get; set; } = new();

    public string DefaultDifficulty { get; set; } = "normal";

    /// <summary>Keyed by id (easy / normal / hard), in display order.</summary>
    public Dictionary<string, DifficultyDef> Difficulties { get; set; } = new()
    {
        ["easy"] = new() { Name = "Easy", Description = "Tougher heroes, softer foes, heal after each win.",
            HeroHpMultiplier = 1.15, EnemyHpMultiplier = 0.9, EnemyDamageMultiplier = 0.85, VictoryHealFraction = 0.1, TimingWindow = 0.30 },
        ["normal"] = new() { Name = "Normal", Description = "The intended challenge." },
        ["hard"] = new() { Name = "Hard", Description = "Foes are tougher, hit harder and more often.",
            EnemyHpMultiplier = 1.1, EnemyDamageMultiplier = 1.1, EnemyAttackBonus = 1, TimingWindow = 0.14 },
    };

    /// <summary>The preset for <paramref name="id"/>, falling back to the default, then to neutral Normal.</summary>
    public DifficultyDef DifficultyOf(string? id) =>
        id is not null && Difficulties.TryGetValue(id, out var d) ? d
        : Difficulties.TryGetValue(DefaultDifficulty, out var n) ? n
        : new DifficultyDef();

    public int WeightOf(Rarity rarity) => RarityWeights.TryGetValue(rarity.ToString(), out int w) ? w : 0;
}

/// <summary>
/// All static game data, loaded once from the data folder with System.Text.Json.
/// <see cref="Load"/> validates everything (unknown effect types, missing card ids, bad dice) and throws
/// with the full list of problems, so a typo in JSON fails at startup rather than mid-fight.
/// </summary>
public sealed class ContentDb
{
    public const string BasicStrikeId = "basic.strike";
    public const string BasicGuardId = "basic.guard";

    private static readonly string[] ValidWhen = { "always", "onHit", "onCrit", "onMiss" };

    /// <summary>Hit/spell effects the desktop knows how to draw ("fx" on cards, intents and hero weapons).</summary>
    public static readonly string[] KnownFx =
        { "slash", "smash", "stab", "bite", "arrow", "bolt", "fire", "missile", "radiant", "heal", "shield", "poof", "crit" };

    private static readonly string[] OutcomeTypes =
        { "gold", "luck", "torch", "xp", "heal", "maxHp", "damage", "item", "upgradeRandom", "addCard", "removeCard" };

    private readonly List<string> _loadErrors = new();

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ContentDb(
        IEnumerable<CardDef> cards,
        IEnumerable<HeroClassDef> heroes,
        IEnumerable<EnemyDef> enemies,
        IEnumerable<ItemDef>? items = null,
        BalanceDef? balance = null,
        EffectRegistry? effects = null,
        IEnumerable<EventDef>? events = null)
    {
        Events = Index(events ?? Array.Empty<EventDef>(), e => e.Id, "event");
        Cards = Index(cards, c => c.Id, "card");
        Heroes = Index(heroes, h => h.Id, "hero class");
        Enemies = Index(enemies, e => e.Id, "enemy");
        Items = Index(items ?? Array.Empty<ItemDef>(), i => i.Id, "item");
        Balance = balance ?? new BalanceDef();
        Effects = effects ?? EffectRegistry.CreateDefault();
    }

    public IReadOnlyDictionary<string, CardDef> Cards { get; }
    public IReadOnlyDictionary<string, HeroClassDef> Heroes { get; }
    public IReadOnlyDictionary<string, EnemyDef> Enemies { get; }
    public IReadOnlyDictionary<string, ItemDef> Items { get; }
    public IReadOnlyDictionary<string, EventDef> Events { get; }
    public BalanceDef Balance { get; }
    public EffectRegistry Effects { get; }

    public CardDef Card(string id) =>
        Cards.TryGetValue(id, out var c) ? c : throw new KeyNotFoundException($"Unknown card '{id}'.");

    public EnemyDef Enemy(string id) =>
        Enemies.TryGetValue(id, out var e) ? e : throw new KeyNotFoundException($"Unknown enemy '{id}'.");

    public HeroClassDef HeroClass(string id) =>
        Heroes.TryGetValue(id, out var h) ? h : throw new KeyNotFoundException($"Unknown hero class '{id}'.");

    // ------------------------------------------------------------------ loading

    /// <summary>Loads cards.json (required), heroes.json, enemies.json, items.json and balance.json (optional) and validates them.</summary>
    public static ContentDb Load(string dataDirectory, EffectRegistry? effects = null)
    {
        var cards = Read<CardFile>(Path.Combine(dataDirectory, "cards.json"), required: true)!.Cards;
        var heroes = Read<HeroFile>(Path.Combine(dataDirectory, "heroes.json"))?.Heroes ?? new();
        var enemies = Read<EnemyFile>(Path.Combine(dataDirectory, "enemies.json"))?.Enemies ?? new();
        var items = Read<ItemFile>(Path.Combine(dataDirectory, "items.json"))?.Items ?? new();
        var balance = Read<BalanceDef>(Path.Combine(dataDirectory, "balance.json"));
        var events = Read<EventFile>(Path.Combine(dataDirectory, "events.json"))?.Events ?? new();

        var db = new ContentDb(cards, heroes, enemies, items, balance, effects, events);
        var errors = db.Validate();
        if (errors.Count > 0)
            throw new InvalidDataException("Content errors in " + dataDirectory + ":\n  " + string.Join("\n  ", errors));
        return db;
    }

    /// <summary>Walks up from <paramref name="startDirectory"/> (default: the exe folder) to find the repo's data folder.</summary>
    public static string FindDataDirectory(string? startDirectory = null)
    {
        var dir = new DirectoryInfo(startDirectory ?? AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "data");
            if (File.Exists(Path.Combine(candidate, "cards.json")))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            $"No data/cards.json found in {startDirectory ?? AppContext.BaseDirectory} or any parent folder.");
    }

    private static T? Read<T>(string path, bool required = false) where T : class
    {
        if (!File.Exists(path))
        {
            if (required) throw new FileNotFoundException("Required content file is missing.", path);
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
                   ?? throw new InvalidDataException($"{path} is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{Path.GetFileName(path)}: {ex.Message}", ex);
        }
    }

    // ------------------------------------------------------------------ validation

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>(_loadErrors);

        foreach (var card in Cards.Values)
        {
            if (card.Effects is null || card.Effects.Count == 0)
            {
                errors.Add($"card '{card.Id}' has no effects.");
                continue;
            }
            foreach (var e in card.Effects)
                CheckEffect(errors, $"card '{card.Id}'", e);
            CheckFx(errors, $"card '{card.Id}'", card.Fx);
            if (card.Upgrade?.Effects is { } upEffects)
            {
                if (upEffects.Count == 0) errors.Add($"card '{card.Id}' upgrade has an empty effect list.");
                foreach (var e in upEffects)
                    CheckEffect(errors, $"card '{card.Id}' upgrade", e);
            }
        }

        foreach (var item in Items.Values)
            foreach (var e in item.Effects ?? Array.Empty<EffectDef>())
                CheckEffect(errors, $"item '{item.Id}'", e);

        foreach (var id in new[] { BasicStrikeId, BasicGuardId })
            if (!Cards.ContainsKey(id))
                errors.Add($"basic card '{id}' is missing from cards.json.");

        foreach (var ev in Events.Values)
        {
            if (ev.Options is null || ev.Options.Count == 0)
            {
                errors.Add($"event '{ev.Id}' has no options.");
                continue;
            }
            foreach (var option in ev.Options)
                foreach (var o in (option.Success ?? Array.Empty<EventOutcome>()).Concat(option.Failure ?? Array.Empty<EventOutcome>()))
                {
                    string owner = $"event '{ev.Id}' option '{option.Label}'";
                    if (!OutcomeTypes.Contains(o.Type, StringComparer.OrdinalIgnoreCase))
                        errors.Add($"{owner}: unknown outcome type '{o.Type}'.");
                    if (o.Dice is not null) CheckDice(errors, owner, o.Dice);
                    if (o.Type.Equals("item", StringComparison.OrdinalIgnoreCase) && (o.Id is null || !Items.ContainsKey(o.Id)))
                        errors.Add($"{owner}: unknown item '{o.Id}'.");
                }
        }

        foreach (var hero in Heroes.Values)
        {
            CheckDice(errors, $"hero '{hero.Id}' weapon", hero.Weapon);
            CheckFx(errors, $"hero '{hero.Id}' weaponFx", hero.WeaponFx);
            if (hero.BasicAction is { Length: > 0 } basic)
            {
                if (!Cards.TryGetValue(basic, out var b))
                    errors.Add($"hero '{hero.Id}' basicAction references unknown card '{basic}'.");
                else if (!b.ClassId.Equals("basic", StringComparison.OrdinalIgnoreCase))
                    errors.Add($"hero '{hero.Id}' basicAction '{basic}' must have classId 'basic'.");
            }
            foreach (var cardId in hero.StartingDeck ?? Array.Empty<string>())
                if (!Cards.ContainsKey(cardId))
                    errors.Add($"hero '{hero.Id}' starting deck references unknown card '{cardId}'.");
        }

        foreach (var enemy in Enemies.Values)
        {
            if (enemy.Intents is null || enemy.Intents.Count == 0)
            {
                errors.Add($"enemy '{enemy.Id}' has no intents.");
                continue;
            }
            foreach (var intent in enemy.Intents)
            {
                if (intent.Weight <= 0) errors.Add($"enemy '{enemy.Id}' intent '{intent.Name}' needs a weight > 0.");
                if (intent.Kind == IntentKind.Attack) CheckDice(errors, $"enemy '{enemy.Id}' intent '{intent.Name}'", intent.Damage);
                CheckFx(errors, $"enemy '{enemy.Id}' intent '{intent.Name}'", intent.Fx);
            }
        }

        return errors;
    }

    private void CheckEffect(List<string> errors, string owner, EffectDef e)
    {
        if (!Effects.Contains(e.Type))
            errors.Add($"{owner}: unknown effect type '{e.Type}'.");
        if (!ValidWhen.Contains(e.When, StringComparer.OrdinalIgnoreCase))
            errors.Add($"{owner}: effect '{e.Type}' has invalid when '{e.When}'.");
        if (e.Args is not null && e.Args.TryGetValue("dice", out var dice))
            CheckDice(errors, $"{owner} effect '{e.Type}'", dice);
    }

    private static void CheckFx(List<string> errors, string owner, string? fx)
    {
        if (fx is not null && !KnownFx.Contains(fx, StringComparer.OrdinalIgnoreCase))
            errors.Add($"{owner}: unknown fx '{fx}' (known: {string.Join(", ", KnownFx)}).");
    }

    private static void CheckDice(List<string> errors, string owner, string? text)
    {
        try
        {
            DiceExpr.Parse(text ?? "");
        }
        catch (Exception)
        {
            errors.Add($"{owner}: '{text}' is not a dice expression.");
        }
    }

    private Dictionary<string, T> Index<T>(IEnumerable<T> items, Func<T, string> key, string what)
    {
        var dict = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            string id = key(item);
            if (string.IsNullOrWhiteSpace(id)) _loadErrors.Add($"a {what} has no id.");
            else if (!dict.TryAdd(id, item)) _loadErrors.Add($"duplicate {what} id '{id}'.");
        }
        return dict;
    }

    // JSON file wrappers
    internal sealed class CardFile { public List<CardDef> Cards { get; set; } = new(); }
    internal sealed class HeroFile { public List<HeroClassDef> Heroes { get; set; } = new(); }
    internal sealed class EnemyFile { public List<EnemyDef> Enemies { get; set; } = new(); }
    internal sealed class ItemFile { public List<ItemDef> Items { get; set; } = new(); }
    internal sealed class EventFile { public List<EventDef> Events { get; set; } = new(); }
}
