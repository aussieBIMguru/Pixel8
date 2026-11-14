using System.Text.Json;
using System.Text.Json.Serialization;
using Pixel8.Core.Cards;
using Pixel8.Core.Dice;
using Pixel8.Core.Effects;
using Pixel8.Core.Gear;

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
    string? WeaponFx = null,
    int ConSave = 0,
    int? Str = null,
    int? Dex = null,
    IReadOnlyList<string>? Proficiencies = null,
    IReadOnlyList<string>? StartingGear = null,
    int WisSave = 0);

public enum IntentKind { Attack, Guard }

/// <summary>
/// One thing an enemy can do on its turn. Picked by weight at the start of each round and shown to the player.
/// <paramref name="Big"/> marks a heavy attack (shown with a bolt): when it hits, the defender gets a timed
/// reaction (Perfect = dodge, Good = half, Ok = −25%, Miss = full).
/// Round 8:
/// <paramref name="Extra"/> is extra damage dice on a hit (a dragon's poison bite), labelled <paramref name="ExtraType"/>.
/// <paramref name="Poison"/> poisons the target on a hit for <paramref name="PoisonRounds"/> turns.
/// <paramref name="Area"/> hits every standing hero; with <paramref name="Save"/> (e.g. "CON") each hero saves vs
/// <paramref name="SaveDc"/> for half instead of the enemy rolling to hit. One damage roll for everyone; a big area
/// attack gets one timing press for the whole party.
/// <paramref name="Recharge"/> (5e "Recharge 5–6" = 5): after use, the intent is unavailable until a d6 rolled at the
/// start of a round meets it. <paramref name="DamageScale"/> scales the published damage at resolution (game-side,
/// like HP scaling), so the data can keep the SRD dice.
/// </summary>
public sealed record IntentDef(
    string Name,
    IntentKind Kind = IntentKind.Attack,
    int AttackBonus = 0,
    string Damage = "0",
    int Block = 0,
    int Weight = 1,
    bool Big = false,
    string? Fx = null,
    string? Extra = null,
    string ExtraType = "extra",
    int Poison = 0,
    int PoisonRounds = 2,
    bool Area = false,
    string? Save = null,
    int SaveDc = 10,
    int Recharge = 0,
    double DamageScale = 1,
    string? Condition = null,
    int ConditionRounds = 1,
    string? ConditionSave = null,
    int ConditionDc = 10,
    bool Drain = false,
    bool PerHead = false,
    bool Legendary = false);

// Round 11 intent fields:
//   Condition / ConditionRounds: a condition put on a hero the attack hits (Paralyzed, Restrained, Charmed…). With
//   ConditionSave ("CON", "DEX", "WIS") + ConditionDc the hero saves to shrug it off.
//   A non-area intent with Save is a single-target save (Basilisk gaze): no attack roll, half damage on a made save.
//   Drain: the attacker heals the damage it deals (Wight, Wraith, Vampire bite).
//   PerHead: one attack per head the enemy has right now (Hydra). PerHead bites skip reactions and timing.
//   Legendary: never picked as the main intent; the enemy uses one of these as an extra turn at the end of each round (Lich).

/// <summary>Round 11: an enemy that calls allies when it drops to <paramref name="AtHpFraction"/> of its HP (once per fight).</summary>
public sealed record SummonDef(IReadOnlyList<string> Enemies, double AtHpFraction = 0.5, string? Text = null);

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
    int Xp = 0,
    double HpScale = 1,
    bool Random = true,
    string? Type = null,
    int MaxDepth = 99,
    int? MaxAdds = null,
    int Regen = 0,
    IReadOnlyList<string>? RegenStoppedBy = null,
    int Heads = 0,
    int SeverAt = 0,
    IReadOnlyList<string>? RegrowStoppedBy = null,
    SummonDef? Summon = null)
{
    // Round 11: Type ("undead"…) for Smite and card conditions; MaxDepth keeps weak normals out of deep Depths;
    // MaxAdds caps the extra normals in a boss or elite fight; Regen heals at the start of its turn unless it took
    // damage of a RegenStoppedBy type since its last turn (Troll: fire, acid); Heads/SeverAt/RegrowStoppedBy (Hydra):
    // one hit of SeverAt+ damage severs a head, and a lost head regrows each turn unless it was hit with a
    // RegrowStoppedBy type; Summon: allies called once at a HP fraction (Lich).
    public bool IsUndead => string.Equals(Type, "undead", StringComparison.OrdinalIgnoreCase);
}

/// <summary>An event option's d20 check: the best hero rolls d20 + mod ("atk", "dex", "spell" or "none") vs DC.</summary>
public sealed record EventCheck(string Mod = "none", int Dc = 10);

/// <summary>
/// One result of an event option. Type: gold | luck | torch | xp (amount, may be negative) · heal (amount = fraction
/// of max HP, e.g. "0.25") · maxHp (amount) · damage (dice; target "random" or "party", never downs a hero) ·
/// item (id) · upgradeRandom · addCard (optional rarity) · removeCard (the player chooses) ·
/// fight (kind "normal" or "elite"; surprise = heroes have advantage in round 1; optional enemies = exact enemy ids,
/// otherwise a random group for the Depth). A fight starts when the event screen closes.
/// </summary>
public sealed record EventOutcome(
    string Type,
    string? Amount = null,
    string? Dice = null,
    string Target = "party",
    string? Id = null,
    string? Kind = null,
    bool Surprise = false,
    IReadOnlyList<string>? Enemies = null,
    Rarity? Rarity = null);

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
    /// <summary>Added to every hero attack roll, weapon or spell (round 8: +1 on Easy and Normal).</summary>
    public int HeroAttackBonus { get; set; }
    /// <summary>Luck every fight starts with, spent before run Luck (round 8: Easy 2, Normal 1, Hard 0).</summary>
    public int FightLuck { get; set; } = 1;
    /// <summary>Rearm (basic Action): how many cards a hero picks back from their discard (Easy 3, Normal 2, Hard 1).</summary>
    public int RearmCards { get; set; } = 2;
    /// <summary>Added to normal-fight group size (still 1–4).</summary>
    public int GroupSizeBonus { get; set; }
    /// <summary>After a win, each standing hero heals this fraction of max HP.</summary>
    public double VictoryHealFraction { get; set; }
    /// <summary>
    /// Timed reactions: width of the Good zone as a fraction of the bar (Perfect is the middle third of it,
    /// Ok reaches twice as far). Easy 0.30, Normal 0.20, Hard 0.14.
    /// </summary>
    public double TimingWindow { get; set; } = 0.20;
    /// <summary>
    /// Round 12: per-Depth enemy multipliers for this difficulty only (index 0 = Depth 1), on top of the base
    /// <see cref="BalanceDef.EnemyHpByDepth"/> / <see cref="BalanceDef.EnemyDamageByDepth"/>. Lets a preset reshape
    /// its own curve (Hard: sharper early, gentler late). Empty or missing = ×1.
    /// </summary>
    public List<double> EnemyHpByDepth { get; set; } = new();
    public List<double> EnemyDamageByDepth { get; set; } = new();
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

/// <summary>One loot source (round 10): the chance to drop gear, how many to choose from, and rarity weights per Depth.</summary>
public sealed class LootTableDef
{
    public double Chance { get; set; } = 1;
    public int Choices { get; set; } = 1;
    /// <summary>Index 0 = Depth 1. Keys are rarity names (Common / Uncommon / Rare).</summary>
    public List<Dictionary<string, int>> RarityByDepth { get; set; } = new();

    public Dictionary<string, int> RarityAt(int depth) =>
        RarityByDepth.Count == 0 ? new Dictionary<string, int> { ["Common"] = 1 } : RarityByDepth[Math.Clamp(depth, 1, RarityByDepth.Count) - 1];

    public static LootTableDef Of(double chance, int choices, params int[][] byDepth) => new()
    {
        Chance = chance,
        Choices = choices,
        RarityByDepth = byDepth.Select(w => new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            { ["Common"] = w[0], ["Uncommon"] = w[1], ["Rare"] = w[2] }).ToList()
    };
}

/// <summary>Gear numbers (round 10). Lives under "gear" in balance.json.</summary>
public sealed class GearBalanceDef
{
    /// <summary>Shared party pack for unequipped gear.</summary>
    public int PackSize { get; set; } = 8;
    /// <summary>Merchant buy prices by rarity (an item's own "price" wins).</summary>
    public Dictionary<string, int> Prices { get; set; } = new(StringComparer.OrdinalIgnoreCase) { ["Common"] = 30, ["Uncommon"] = 70, ["Rare"] = 140 };
    /// <summary>Selling gear to a merchant pays this share of its price.</summary>
    public double SellFraction { get; set; } = 0.5;
    public int MerchantStock { get; set; } = 3;
    /// <summary>At least this many of the merchant's gear can be used by someone in the party.</summary>
    public int MerchantUsable { get; set; } = 2;
    /// <summary>Chance per Depth that a treasure first seen in fog (not exposed) is a mimic.</summary>
    public List<double> MimicChance { get; set; } = new() { 0.15, 0.20, 0.25, 0.25, 0.30 };
    public int MimicGoldBase { get; set; } = 30;
    public int MimicGoldPerDepth { get; set; } = 10;
    /// <summary>Loot tables keyed by source: treasure, normal, elite, boss, mimic, merchant, event.</summary>
    public Dictionary<string, LootTableDef> Loot { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["treasure"] = LootTableDef.Of(0.4, 1, new[] { 70, 27, 3 }, new[] { 45, 45, 10 }, new[] { 25, 55, 20 }),
        ["normal"] = LootTableDef.Of(0.1, 1, new[] { 100, 0, 0 }),
        ["elite"] = LootTableDef.Of(0.5, 1, new[] { 40, 50, 10 }, new[] { 25, 55, 20 }, new[] { 10, 55, 35 }),
        ["boss"] = LootTableDef.Of(1, 2, new[] { 0, 70, 30 }, new[] { 0, 50, 50 }, new[] { 0, 40, 60 }),
        ["mimic"] = LootTableDef.Of(1, 3, new[] { 0, 60, 40 }, new[] { 0, 50, 50 }, new[] { 0, 40, 60 }),
        ["merchant"] = LootTableDef.Of(1, 3, new[] { 60, 35, 5 }, new[] { 40, 45, 15 }, new[] { 20, 55, 25 }),
        ["event"] = LootTableDef.Of(1, 1, new[] { 65, 30, 5 }, new[] { 45, 45, 10 }, new[] { 25, 55, 20 }),
    };

    public int MimicChanceAt(int depth, out double chance)
    {
        chance = MimicChance.Count == 0 ? 0 : MimicChance[Math.Clamp(depth, 1, MimicChance.Count) - 1];
        return (int)Math.Round(chance * 100);
    }

    // JSON replaces these dictionaries with case-sensitive ones, so look keys up ignoring case.
    public int PriceOf(GearDef def, Rarity effective) =>
        def.Price > 0 ? def.Price
        : Prices.FirstOrDefault(kv => kv.Key.Equals(effective.ToString(), StringComparison.OrdinalIgnoreCase)) is { Key: not null } p ? p.Value : 50;

    public LootTableDef Table(string source) =>
        Loot.FirstOrDefault(kv => kv.Key.Equals(source, StringComparison.OrdinalIgnoreCase)).Value ?? LootTableDef.Of(1, 1, new[] { 70, 25, 5 });
}

/// <summary>Tunable numbers. Lives in data/balance.json; every value has a default so the file is optional.</summary>
public sealed class BalanceDef
{
    public int MaxHand { get; set; } = 5;
    public int SecondWindDraw { get; set; } = 1;
    public int StartingGold { get; set; } = 0;
    /// <summary>Run Luck at the start (round 8: 0; events add it). Each fight also gets its difficulty's fightLuck.</summary>
    public int StartingLuck { get; set; } = 0;
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

    /// <summary>
    /// Round 11: normal group size per Depth as [min, max] (index 0 = Depth 1), before the difficulty bonus.
    /// Empty = the old rule (NormalGroupMin..Max, +1 per Depth, max 4).
    /// </summary>
    public List<List<int>> NormalGroupByDepth { get; set; } = new();

    /// <summary>Extra normals beside an elite (index 0 = Depth 1), capped by the elite's maxAdds. Empty = Depth.</summary>
    public List<int> EliteAddsByDepth { get; set; } = new();

    /// <summary>Extra normals beside a boss (index 0 = Depth 1), capped by the boss's maxAdds. Empty = Depth − 1.</summary>
    public List<int> BossAddsByDepth { get; set; } = new();

    /// <summary>Starting Torch = critical path × this, per Depth (index 0 = Depth 1). Empty = 1.6 everywhere.</summary>
    public List<double> TorchMultiplierByDepth { get; set; } = new();

    public double TorchMultiplierAt(int depth) =>
        TorchMultiplierByDepth.Count == 0 ? 1.6 : TorchMultiplierByDepth[Math.Clamp(depth, 1, TorchMultiplierByDepth.Count) - 1];

    /// <summary>Round 11: Paladin Lay on Hands pool per fight: base + per party level after 1.</summary>
    public int LayOnHandsBase { get; set; } = 5;
    /// <summary>Round 12: enemy regeneration and Hydra regrowth stop after this round (0 = never). Prevents endless fights.</summary>
    public int RegenTiresAfterRound { get; set; } = 10;
    public int LayOnHandsPerLevel { get; set; } = 1;

    public static int AtDepth(List<int> list, int depth, int fallback) =>
        list.Count == 0 ? fallback : list[Math.Clamp(depth, 1, list.Count) - 1];

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

    /// <summary>
    /// Proficiency growth: at each of these party levels every hero gets +1 to attack rolls (weapon and spell)
    /// and +1 to their spell save DC. Round 8: a 5e-style curve.
    /// </summary>
    public List<int> ProficiencyLevels { get; set; } = new() { 3, 5, 7, 9 };

    /// <summary>The proficiency bonus at a party level (0 at level 1).</summary>
    public int ProficiencyAt(int level) => ProficiencyLevels.Count(l => level >= l);

    /// <summary>At each of these party levels, hero weapon and spell damage rolls get +1.</summary>
    public List<int> DamageLevels { get; set; } = new() { 2, 4, 6, 8 };

    /// <summary>The hero damage bonus at a party level (0 at level 1).</summary>
    public int DamageBonusAt(int level) => DamageLevels.Count(l => level >= l);

    /// <summary>A missed hero attack (not a natural 1) still grazes the target for the hero's damage bonus.</summary>
    public bool GrazeOnMiss { get; set; } = true;

    /// <summary>Each miss in a row adds this much to that hero's next attack roll ("FOCUS"), until they hit.</summary>
    public int FocusPerMiss { get; set; } = 2;

    /// <summary>
    /// Enemy AC in play is capped by Depth (index 0 = Depth 1). Published AC stays in enemies.json;
    /// like HP scaling, the cap is applied at spawn. Empty = no cap.
    /// </summary>
    public List<int> AcCapByDepth { get; set; } = new() { 14, 15, 16, 16, 17 };

    /// <summary>
    /// Round 12: per-Depth enemy multipliers (index 0 = Depth 1), on top of enemyHpMultiplier, hpScale/damageScale
    /// and the difficulty. They shape the difficulty curve across the five Depths. Empty or missing = ×1.
    /// </summary>
    public List<double> EnemyHpByDepth { get; set; } = new();
    public List<double> EnemyDamageByDepth { get; set; } = new();

    public double EnemyHpAt(int depth) => ByDepth(EnemyHpByDepth, depth);
    public double EnemyDamageAt(int depth) => ByDepth(EnemyDamageByDepth, depth);

    /// <summary>The base per-Depth multiplier × the difficulty's own per-Depth multiplier.</summary>
    public double EnemyHpAt(int depth, DifficultyDef? difficulty) => EnemyHpAt(depth) * ByDepth(difficulty?.EnemyHpByDepth, depth);
    public double EnemyDamageAt(int depth, DifficultyDef? difficulty) => EnemyDamageAt(depth) * ByDepth(difficulty?.EnemyDamageByDepth, depth);

    internal static double ByDepth(List<double>? list, int depth) =>
        depth <= 0 || list is null || list.Count == 0 ? 1 : list[Math.Min(depth, list.Count) - 1];

    public int AcCap(int depth) =>
        depth <= 0 || AcCapByDepth.Count == 0 ? int.MaxValue : AcCapByDepth[Math.Min(depth, AcCapByDepth.Count) - 1];

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

    /// <summary>Round 10: gear loot tables, pack size, prices, mimics.</summary>
    public GearBalanceDef Gear { get; set; } = new();

    public string DefaultDifficulty { get; set; } = "normal";

    /// <summary>Keyed by id (easy / normal / hard), in display order.</summary>
    public Dictionary<string, DifficultyDef> Difficulties { get; set; } = new()
    {
        ["easy"] = new() { Name = "Easy", Description = "Tougher heroes, softer foes, heal after each win.",
            HeroHpMultiplier = 1.15, EnemyHpMultiplier = 0.9, EnemyDamageMultiplier = 0.85, VictoryHealFraction = 0.1, TimingWindow = 0.30,
            HeroAttackBonus = 1, FightLuck = 2, RearmCards = 3 },
        ["normal"] = new() { Name = "Normal", Description = "The intended challenge.", HeroAttackBonus = 1, FightLuck = 1, RearmCards = 2 },
        ["hard"] = new() { Name = "Hard", Description = "Foes are tougher, hit harder and more often.",
            EnemyHpMultiplier = 1.1, EnemyDamageMultiplier = 1.1, EnemyAttackBonus = 1, TimingWindow = 0.14, FightLuck = 0, RearmCards = 1 },
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
        { "slash", "smash", "stab", "bite", "arrow", "bolt", "fire", "missile", "radiant", "heal", "shield", "poof", "crit", "poison",
          "frost", "necrotic", "song", "vine" };

    private static readonly string[] OutcomeTypes =
        { "gold", "luck", "torch", "xp", "heal", "maxHp", "damage", "item", "upgradeRandom", "addCard", "removeCard", "fight",
          "gear", "improveGear", "cursedGear" };

    private static readonly string[] PassiveTypes = { "onHit", "onCrit", "poisonOnHit", "regen", "bracers" };

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
        IEnumerable<EventDef>? events = null,
        IEnumerable<GearDef>? gear = null)
    {
        Events = Index(events ?? Array.Empty<EventDef>(), e => e.Id, "event");
        Gear = Index(gear ?? Array.Empty<GearDef>(), g => g.Id, "gear");
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
    /// <summary>Weapons, armor, off-hands and trinkets (round 10, data/gear.json).</summary>
    public IReadOnlyDictionary<string, GearDef> Gear { get; }
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
        var gear = Read<GearFile>(Path.Combine(dataDirectory, "gear.json"))?.Gear ?? new();

        var db = new ContentDb(cards, heroes, enemies, items, balance, effects, events, gear);
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
            if (card.Uses < 0) errors.Add($"card '{card.Id}': uses can't be negative.");
            CheckFx(errors, $"card '{card.Id}'", card.Fx);
            // Round 8: decks never reshuffle, so card draw is a rare-only power.
            bool draws = card.Effects.Concat(card.Upgrade?.Effects ?? Array.Empty<EffectDef>())
                .Any(e => e.Type.Equals("Draw", StringComparison.OrdinalIgnoreCase));
            if (draws && card.Rarity < Rarity.Rare)
                errors.Add($"card '{card.Id}' draws cards but is {card.Rarity}; Draw is for Rare cards only.");
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
                    if (o.Type.Equals("cursedGear", StringComparison.OrdinalIgnoreCase) && (o.Id is null || !Gear.ContainsKey(o.Id)))
                        errors.Add($"{owner}: unknown gear '{o.Id}'.");
                    if (o.Type.Equals("gear", StringComparison.OrdinalIgnoreCase) && o.Id is not null && !Gear.ContainsKey(o.Id))
                        errors.Add($"{owner}: unknown gear '{o.Id}'.");
                    if (o.Type.Equals("fight", StringComparison.OrdinalIgnoreCase))
                    {
                        if (o.Kind is not null && !o.Kind.Equals("normal", StringComparison.OrdinalIgnoreCase)
                                               && !o.Kind.Equals("elite", StringComparison.OrdinalIgnoreCase))
                            errors.Add($"{owner}: fight kind must be 'normal' or 'elite', not '{o.Kind}'.");
                        foreach (var id in o.Enemies ?? Array.Empty<string>())
                            if (!Enemies.ContainsKey(id)) errors.Add($"{owner}: unknown enemy '{id}'.");
                        if (o.Enemies is { Count: > 4 }) errors.Add($"{owner}: a fight can have at most 4 enemies.");
                    }
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
            var slots = new HashSet<GearSlot>();
            foreach (var gearId in hero.StartingGear ?? Array.Empty<string>())
            {
                if (!Gear.TryGetValue(gearId, out var g)) { errors.Add($"hero '{hero.Id}' starting gear references unknown gear '{gearId}'."); continue; }
                if (!slots.Add(g.Slot)) errors.Add($"hero '{hero.Id}' starting gear has two {g.Slot} items.");
                if (!CanUse(hero, g)) errors.Add($"hero '{hero.Id}' can't use its starting gear '{gearId}'.");
            }
            if (hero.StartingGear is { Count: > 0 } && hero.Str is null)
                errors.Add($"hero '{hero.Id}' has starting gear but no str (gear maths needs str and dex).");
        }

        foreach (var g in Gear.Values)
        {
            string owner = $"gear '{g.Id}'";
            if (g.Slot == GearSlot.Weapon) CheckDice(errors, owner, g.Damage ?? "");
            if (g.Slot == GearSlot.Armor && g.Armor is ArmorKind.None or ArmorKind.Shield)
                errors.Add($"{owner}: armor needs armor kind Cloth, Light, Medium or Heavy.");
            if (g.IsShield && g.Slot != GearSlot.OffHand) errors.Add($"{owner}: shields go in the OffHand slot.");
            if (g.TwoHanded && g.Slot != GearSlot.Weapon) errors.Add($"{owner}: only weapons can be two-handed.");
            if (g.Slot != GearSlot.Trinket && (g.Tags is null || g.Tags.Count == 0)) errors.Add($"{owner}: needs tags (who can use it).");
            if (g.Passive is { } p)
            {
                if (!PassiveTypes.Contains(p.Type, StringComparer.OrdinalIgnoreCase)) errors.Add($"{owner}: unknown passive '{p.Type}'.");
                if (p.Dice is not null) CheckDice(errors, $"{owner} passive", p.Dice);
            }
            CheckFx(errors, owner, g.Fx);
        }

        foreach (var enemy in Enemies.Values)
        {
            if (enemy.Intents is null || enemy.Intents.Count == 0)
            {
                errors.Add($"enemy '{enemy.Id}' has no intents.");
                continue;
            }
            if (enemy.Intents.All(i => i.Recharge > 0))
                errors.Add($"enemy '{enemy.Id}' needs at least one intent without a recharge.");
            if (enemy.HpScale <= 0) errors.Add($"enemy '{enemy.Id}': hpScale must be > 0.");
            if (enemy.Intents.All(i => i.Legendary)) errors.Add($"enemy '{enemy.Id}' needs at least one intent that isn't legendary.");
            if (enemy.Intents.Any(i => i.PerHead) && enemy.Heads <= 0) errors.Add($"enemy '{enemy.Id}' has a perHead intent but no heads.");
            if (enemy.MaxDepth < enemy.MinDepth) errors.Add($"enemy '{enemy.Id}': maxDepth is below minDepth.");
            foreach (var id in enemy.Summon?.Enemies ?? Array.Empty<string>())
                if (!Enemies.ContainsKey(id)) errors.Add($"enemy '{enemy.Id}' summons unknown enemy '{id}'.");
            foreach (var intent in enemy.Intents)
            {
                if (intent.Weight <= 0) errors.Add($"enemy '{enemy.Id}' intent '{intent.Name}' needs a weight > 0.");
                if (intent.Kind == IntentKind.Attack) CheckDice(errors, $"enemy '{enemy.Id}' intent '{intent.Name}'", intent.Damage);
                if (intent.Extra is not null) CheckDice(errors, $"enemy '{enemy.Id}' intent '{intent.Name}' extra", intent.Extra);
                if (intent.Recharge is < 0 or > 6) errors.Add($"enemy '{enemy.Id}' intent '{intent.Name}': recharge must be 1-6.");
                if (intent.DamageScale <= 0) errors.Add($"enemy '{enemy.Id}' intent '{intent.Name}': damageScale must be > 0.");
                CheckFx(errors, $"enemy '{enemy.Id}' intent '{intent.Name}'", intent.Fx);
                foreach (var ability in new[] { intent.Save, intent.ConditionSave })
                    if (ability is not null && !SaveAbilities.Contains(ability, StringComparer.OrdinalIgnoreCase))
                        errors.Add($"enemy '{enemy.Id}' intent '{intent.Name}': unknown save '{ability}' (CON, DEX or WIS).");
            }
        }

        return errors;
    }

    private static readonly string[] SaveAbilities = { "CON", "DEX", "WIS" };

    private static readonly string[] TargetKeys = { "target", "self", "party", "allEnemies", "adjacent", "random2", "none" };

    private void CheckEffect(List<string> errors, string owner, EffectDef e)
    {
        if (e.Args is not null && e.Args.TryGetValue("target", out var key) && !TargetKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
            errors.Add($"{owner}: effect '{e.Type}' has unknown target '{key}'.");
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
    internal sealed class GearFile { public List<GearDef> Gear { get; set; } = new(); }

    /// <summary>Round 10: a class may use gear whose tags meet its proficiencies. Trinkets (and untagged gear) suit anyone.</summary>
    public static bool CanUse(HeroClassDef cls, GearDef gear) =>
        gear.Slot == GearSlot.Trinket || gear.Tags is null || gear.Tags.Count == 0 || cls.Proficiencies is null
        || gear.Tags.Any(t => cls.Proficiencies.Contains(t, StringComparer.OrdinalIgnoreCase));

    public GearDef GearItem(string id) =>
        Gear.TryGetValue(id, out var g) ? g : throw new KeyNotFoundException($"Unknown gear '{id}'.");
}
