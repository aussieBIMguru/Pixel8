using Pixel8.Core.Cards;
using Pixel8.Core.Content;
using Pixel8.Core.Gear;
using Pixel8.Core.Heroes;

namespace Pixel8.Core.Runs;

/// <summary>Where a piece of loot came from (round 10). Each has a table in balance.json gear.loot.</summary>
public enum LootSource { Treasure, Normal, Elite, Boss, Mimic, Merchant, Event }

/// <summary>Gear to pick from after a fight, a treasure or an event. Pick one (or the only one); <see cref="Gold"/> is already paid.</summary>
public sealed record LootOffer(LootSource Source, IReadOnlyList<GearInstance> Choices, int Gold);

/// <summary>Round 10: gear, the party pack, loot rolls, the merchant's gear and the run flags achievements use.</summary>
public sealed partial class Run
{
    /// <summary>The party is standing at a campfire: cursed gear can come off here (set by the map).</summary>
    public bool AtCampfire { get; set; }

    /// <summary>Debug mode was on during this run: achievements don't count (saved).</summary>
    public bool Debugged { get; set; }

    /// <summary>Times the party fled a fight this run (No Retreat).</summary>
    public int Flees { get; set; }

    public GearInstance NewGear(string id, int bonus = 0) => new(Content.GearItem(id), bonus);

    // ------------------------------------------------------------------ loot

    /// <summary>Rolls a source's loot: null when its chance fails. Mimics also pay gold (added now).</summary>
    public LootOffer? RollLoot(LootSource source)
    {
        var table = Content.Balance.Gear.Table(source.ToString());
        if (!LootRng.Chance(table.Chance)) return null;
        var offer = RollLootOffer(source, table.Choices);
        if (source == LootSource.Mimic)
        {
            int gold = Content.Balance.Gear.MimicGoldBase + Content.Balance.Gear.MimicGoldPerDepth * Depth;
            Party.Gold += gold;
            offer = offer with { Gold = gold };
        }
        return offer.Choices.Count == 0 ? null : offer;
    }

    /// <summary>
    /// <paramref name="count"/> different pieces of gear from the source's table (or a fixed rarity). When there's a
    /// choice, at least one is usable by someone in the party (if any such gear exists).
    /// </summary>
    public LootOffer RollLootOffer(LootSource source, int count, Rarity? rarity = null)
    {
        var table = Content.Balance.Gear.Table(source.ToString());
        var picks = new List<GearInstance>();
        for (int i = 0; i < Math.Max(1, count); i++)
        {
            var r = rarity ?? RollRarity(table);
            var gear = RollGear(r, picks.Select(p => p.Def.Id).ToHashSet());
            if (gear is not null) picks.Add(gear);
        }
        if (picks.Count > 1 && !picks.Any(p => IsUsableByParty(p.Def)))
        {
            var usable = RollGear(rarity ?? RollRarity(table), picks.Select(p => p.Def.Id).ToHashSet(), usableOnly: true);
            if (usable is not null) picks[^1] = usable;
        }
        foreach (var p in picks) Notice("gear", p.Def.Id);
        return new LootOffer(source, picks, 0);
    }

    public Rarity RollRarity(LootTableDef table)
    {
        var weights = table.RarityAt(Depth)
            .Select(kv => (Enum.TryParse<Rarity>(kv.Key, true, out var r) ? r : Rarity.Common, kv.Value))
            .Where(x => x.Value > 0)
            .OrderBy(x => x.Item1)
            .ToList();
        return weights.Count == 0 ? Rarity.Common : LootRng.Weighted(weights);
    }

    /// <summary>
    /// A random lootable piece of gear of <paramref name="rarity"/> (falling back a tier if none). Three times in four
    /// it's one somebody in the party can use, so loot feels relevant without being guaranteed.
    /// </summary>
    public GearInstance? RollGear(Rarity rarity, ISet<string>? exclude = null, bool usableOnly = false)
    {
        for (var r = rarity; r >= Rarity.Common; r--)
        {
            var pool = Content.Gear.Values
                .Where(g => g.Loot && g.Rarity == r && (exclude is null || !exclude.Contains(g.Id)))
                .OrderBy(g => g.Id, StringComparer.Ordinal)
                .ToList();
            var usable = pool.Where(IsUsableByParty).ToList();
            if (usableOnly) pool = usable;
            if (pool.Count == 0) continue;
            var from = !usableOnly && usable.Count > 0 && LootRng.Chance(0.75) ? usable : pool;
            return new GearInstance(LootRng.Pick(from));
        }
        return null;
    }

    public bool IsUsableByParty(GearDef def) => Party.Heroes.Any(h => !h.IsDead && h.CanUse(def));

    /// <summary>Adds loot to the pack. False when the pack is full (the screen offers to drop something first).</summary>
    public bool TakeLoot(GearInstance gear)
    {
        if (Party.PackFull) return false;
        Party.Pack.Add(gear);
        return true;
    }

    /// <summary>Throws a piece of gear from the pack away for good.</summary>
    public bool Drop(GearInstance gear) => Party.Pack.Remove(gear);

    // ------------------------------------------------------------------ equipping

    public bool CanEquip(Hero hero, GearInstance gear, out string reason)
    {
        var slot = gear.Def.Slot;
        var current = hero.Loadout[slot];
        var weapon = hero.Loadout[GearSlot.Weapon];
        var offHand = hero.Loadout[GearSlot.OffHand];
        int room = Party.PackSize - Party.Pack.Count + (Party.Pack.Contains(gear) ? 1 : 0);
        int needed = (current is null ? 0 : 1) + (gear.Def.TwoHanded && offHand is not null ? 1 : 0);

        reason = hero.IsDead ? $"{hero.Name} is dead."
            : !hero.CanUse(gear.Def) ? $"{hero.Name} can't use {gear.Name}."
            : current is { Def.Cursed: true } && !AtCampfire ? $"{current.Name} is cursed: it only comes off at a campfire."
            : slot == GearSlot.OffHand && weapon is { Def.TwoHanded: true } ? $"{weapon.Name} needs both hands."
            : gear.Def.TwoHanded && offHand is { Def.Cursed: true } && !AtCampfire ? $"{offHand.Name} is cursed."
            : needed > room ? "The pack is full."
            : "";
        return reason.Length == 0;
    }

    /// <summary>Equips gear from the pack (or loot not yet in it). What it replaces goes into the pack.</summary>
    public void Equip(Hero hero, GearInstance gear)
    {
        if (!CanEquip(hero, gear, out string reason)) throw new InvalidOperationException(reason);
        Party.Pack.Remove(gear);
        var slot = gear.Def.Slot;
        if (hero.Loadout[slot] is { } old) Party.Pack.Add(old);
        if (gear.Def.TwoHanded && hero.Loadout[GearSlot.OffHand] is { } off)
        {
            Party.Pack.Add(off);
            hero.SetSlot(GearSlot.OffHand, null);
        }
        hero.SetSlot(slot, gear);
        hero.RecalculateGear();
    }

    public bool CanUnequip(Hero hero, GearSlot slot, out string reason)
    {
        var current = hero.Loadout[slot];
        reason = current is null ? "Nothing there."
            : current.Def.Cursed && !AtCampfire ? $"{current.Name} is cursed: it only comes off at a campfire."
            : Party.PackFull ? "The pack is full."
            : "";
        return reason.Length == 0;
    }

    public void Unequip(Hero hero, GearSlot slot)
    {
        if (!CanUnequip(hero, slot, out string reason)) throw new InvalidOperationException(reason);
        Party.Pack.Add(hero.Loadout[slot]!);
        hero.SetSlot(slot, null);
        hero.RecalculateGear();
    }

    /// <summary>A hero's stats now (null for classes without gear maths).</summary>
    public GearStats? CurrentStats(Hero hero) =>
        hero.UsesGear ? GearRules.Compute(hero.Class.Str!.Value, hero.Class.Dex ?? hero.Class.InitMod, hero.Class.SpellDc, 0, hero.Loadout) : null;

    /// <summary>What a hero's stats would be with <paramref name="gear"/> equipped (for the gear screen's preview).</summary>
    public GearStats? PreviewEquip(Hero hero, GearInstance gear)
    {
        if (!hero.UsesGear) return null;
        var temp = new Loadout();
        foreach (var s in Enum.GetValues<GearSlot>()) temp[s] = hero.Loadout[s];
        temp[gear.Def.Slot] = gear;
        if (gear.Def.TwoHanded) temp[GearSlot.OffHand] = null;
        if (gear.Def.Slot == GearSlot.OffHand && temp[GearSlot.Weapon] is { Def.TwoHanded: true }) temp[GearSlot.Weapon] = null;
        return GearRules.Compute(hero.Class.Str!.Value, hero.Class.Dex ?? hero.Class.InitMod, hero.Class.SpellDc, 0, temp);
    }

    /// <summary>Every living hero has all four slots filled (Armed to the Teeth).</summary>
    public bool PartyFullyArmed => Party.Heroes.Where(h => !h.IsDead).All(h => h.Loadout.IsFull) && Party.Heroes.Any(h => !h.IsDead);

    // ------------------------------------------------------------------ merchant and the smith

    public int BuyPrice(GearInstance gear) => Content.Balance.Gear.PriceOf(gear.Def, gear.EffectiveRarity);

    public int SellPrice(GearInstance gear) =>
        Math.Max(1, (int)Math.Round(BuyPrice(gear) * Content.Balance.Gear.SellFraction));

    /// <summary>The merchant's gear: gear.merchantStock pieces, at least gear.merchantUsable of them usable by the party.</summary>
    public List<GearInstance> RollMerchantGear()
    {
        var g = Content.Balance.Gear;
        var table = g.Table(LootSource.Merchant.ToString());
        var stock = new List<GearInstance>();
        for (int i = 0; i < Math.Max(0, g.MerchantStock); i++)
        {
            bool mustUse = stock.Count(s => IsUsableByParty(s.Def)) < g.MerchantUsable && g.MerchantStock - i <= g.MerchantUsable;
            var item = RollGear(RollRarity(table), stock.Select(s => s.Def.Id).ToHashSet(), usableOnly: mustUse)
                       ?? RollGear(RollRarity(table), stock.Select(s => s.Def.Id).ToHashSet());
            if (item is not null) stock.Add(item);
        }
        return stock;
    }

    /// <summary>
    /// The Smith's Anvil: improves one equipped weapon, armor or shield by +1 (max +2 in total). Picks the living
    /// heroes' weapons first, then armor, then shields, lowest bonus first. Returns the line to show.
    /// </summary>
    public string ImproveBestGear(out GearInstance? improved)
    {
        var best = Party.Heroes
            .Where(h => !h.IsDead)
            .SelectMany(h => h.Loadout.Equipped.Select(g => (Hero: h, Gear: g)))
            .Where(x => x.Gear.CanImprove)
            .OrderBy(x => x.Gear.Magic)
            .ThenBy(x => x.Gear.Def.Slot)
            .FirstOrDefault();

        improved = best.Gear;

        if (improved is null)
            return "Nothing here can be improved.";

        improved.Bonus++;
        best.Hero.RecalculateGear();

        return $"{best.Hero.Name}'s {improved.Def.Name} is now +{improved.Magic}!";
    }
}
