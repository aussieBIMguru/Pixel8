using Pixel8.Core.Cards;
using Pixel8.Core.Dice;

namespace Pixel8.Core.Gear;

/// <summary>The four gear slots every hero has (round 10).</summary>
public enum GearSlot { Weapon, Armor, OffHand, Trinket }

/// <summary>How armor adds DEX to AC (5e): cloth/light = all of it, medium = up to +2, heavy = none. Shields add flat AC.</summary>
public enum ArmorKind { None, Cloth, Light, Medium, Heavy, Shield }

/// <summary>
/// A Rare item's passive (round 10). Types:
/// <c>onHit</c> (dice + damageType: extra damage on every weapon hit),
/// <c>onCrit</c> (dice: extra damage on a weapon crit),
/// <c>poisonOnHit</c> (amount, rounds),
/// <c>regen</c> (amount healed at the start of the wearer's turn),
/// <c>bracers</c> (amount AC while wearing no armor or cloth and no shield).
/// </summary>
public sealed record GearPassive(string Type, int Amount = 0, string? Dice = null, string DamageType = "magic", int Rounds = 2);

/// <summary>
/// One piece of gear, from data/gear.json. Names are SRD 5.1 items (CC BY 4.0) or generic descriptions.
/// Weapons: <see cref="Damage"/> die (+ STR, or the better of STR/DEX when <see cref="Finesse"/>), <see cref="Magic"/>
/// adds to hit and damage. Armor: <see cref="BaseAc"/> + DEX by <see cref="Armor"/> kind (+ Magic). Shields: +2 + Magic.
/// Flat bonuses stack from every slot. <see cref="Tags"/> decide which classes may use it (trinkets: anyone).
/// </summary>
public sealed record GearDef(
    string Id,
    string Name,
    GearSlot Slot,
    Rarity Rarity = Rarity.Common,
    string Text = "",
    IReadOnlyList<string>? Tags = null,
    string? Damage = null,
    bool Finesse = false,
    bool TwoHanded = false,
    ArmorKind Armor = ArmorKind.None,
    int BaseAc = 0,
    int Magic = 0,
    int Ac = 0,
    int Attack = 0,
    int DamageBonus = 0,
    int SpellDc = 0,
    int Saves = 0,
    int MaxHp = 0,
    int Initiative = 0,
    int FightLuck = 0,
    int Rearm = 0,
    int StartBlock = 0,
    GearPassive? Passive = null,
    bool Cursed = false,
    int Price = 0,
    bool Loot = true,
    string? Fx = null,
    bool Ranged = false)
{
    public bool IsShield => Armor == ArmorKind.Shield;
}

/// <summary>
/// A piece of gear the party owns. <see cref="Bonus"/> is extra +N from the Smith's Anvil event (mundane → +1 → +2).
/// </summary>
public sealed class GearInstance
{
    private static int _nextUid;

    public GearInstance(GearDef def, int bonus = 0)
    {
        Def = def;
        Bonus = Math.Max(0, bonus);
        Uid = Interlocked.Increment(ref _nextUid);
    }

    public int Uid { get; }
    public GearDef Def { get; }
    public int Bonus { get; set; }

    /// <summary>Total +N: the item's own magic plus any improvement.</summary>
    public int Magic => Def.Magic + Bonus;

    /// <summary>"Longsword", "Longsword +1" (improved), "Shield +1".</summary>
    public string Name
    {
        get
        {
            if (Bonus == 0) return Def.Name;
            string suffix = $" +{Def.Magic}";
            return Def.Magic > 0 && Def.Name.EndsWith(suffix, StringComparison.Ordinal)
                ? Def.Name[..^suffix.Length] + $" +{Magic}"
                : $"{Def.Name} +{Bonus}";
        }
    }

    /// <summary>Improved gear counts a rarity higher per step for prices (capped at Rare).</summary>
    public Rarity EffectiveRarity => (Rarity)Math.Min((int)Rarity.Rare, (int)Def.Rarity + Bonus);

    /// <summary>The weapon/armor/shield can still be improved (max +2 in total; trinkets can't).</summary>
    public bool CanImprove => Def.Slot != GearSlot.Trinket && (Def.Slot != GearSlot.OffHand || Def.IsShield) && Magic < 2;

    public override string ToString() => Name;
}

/// <summary>A hero's four equipped slots. Changing them goes through <see cref="Runs.Run"/>, which recalculates stats.</summary>
public sealed class Loadout
{
    private readonly Dictionary<GearSlot, GearInstance?> _slots = Enum.GetValues<GearSlot>().ToDictionary(s => s, _ => (GearInstance?)null);

    public GearInstance? this[GearSlot slot]
    {
        get => _slots[slot];
        internal set => _slots[slot] = value;
    }

    public IEnumerable<GearInstance> Equipped => _slots.Values.Where(g => g is not null)!;

    public bool IsFull => _slots.Values.All(g => g is not null);

    public IEnumerable<GearPassive> Passives => Equipped.Select(g => g.Def.Passive).Where(p => p is not null)!;

    public int Sum(Func<GearDef, int> stat) => Equipped.Sum(g => stat(g.Def));

    internal void Clear()
    {
        foreach (var s in Enum.GetValues<GearSlot>()) _slots[s] = null;
    }
}

/// <summary>A hero's stats derived from their class and gear (round 10).</summary>
public sealed record GearStats(int AttackMod, DiceExpr Weapon, int Ac, int SpellDc, int Saves, int MaxHpBonus,
    int Initiative, int FightLuck, int Rearm, int StartBlock);

public static class GearRules
{
    /// <summary>5e proficiency bonus at level 1, already inside heroes.json attackMod.</summary>
    public const int BaseProficiency = 2;

    /// <summary>
    /// Works out a hero's combat stats from class STR/DEX and gear:
    /// attack = proficiency + weapon stat (STR, or the better of STR/DEX for finesse) + magic + bonuses;
    /// weapon damage = die + the same stat + magic + damage bonuses (unarmed 1d4 + STR);
    /// AC = armor base + DEX by armor kind (none: 10 + DEX) + magic + shield (2 + magic) + bonuses.
    /// </summary>
    public static GearStats Compute(int str, int dex, int classSpellDc, int classSaves, Loadout gear)
    {
        var weapon = gear[GearSlot.Weapon];
        // Round 11: ranged weapons (bows, crossbows) use DEX; finesse the better of STR/DEX.
        int stat = weapon is null ? str : weapon.Def.Ranged ? dex : weapon.Def.Finesse ? Math.Max(str, dex) : str;
        int weaponMagic = weapon?.Magic ?? 0;
        int attack = BaseProficiency + stat + weaponMagic + gear.Sum(d => d.Attack);
        var die = DiceExpr.Parse(weapon?.Def.Damage ?? "1d4");
        int damageBonus = stat + weaponMagic + gear.Sum(d => d.DamageBonus);
        var weaponDice = new DiceExpr(die.Count, die.Sides, die.Bonus + damageBonus);

        var armor = gear[GearSlot.Armor];
        var shield = gear[GearSlot.OffHand] is { Def.IsShield: true } s ? s : null;
        int ac = armor is null
            ? 10 + dex
            : armor.Def.BaseAc + armor.Magic + armor.Def.Armor switch
            {
                ArmorKind.Medium => Math.Min(dex, 2),
                ArmorKind.Heavy => 0,
                _ => dex
            };
        if (shield is not null) ac += 2 + shield.Magic;
        ac += gear.Sum(d => d.Ac);
        foreach (var p in gear.Passives.Where(p => p.Type.Equals("bracers", StringComparison.OrdinalIgnoreCase)))
            if ((armor is null || armor.Def.Armor == ArmorKind.Cloth) && shield is null) ac += p.Amount;

        return new GearStats(
            attack, weaponDice, ac,
            classSpellDc + gear.Sum(d => d.SpellDc),
            classSaves + gear.Sum(d => d.Saves),
            gear.Sum(d => d.MaxHp),
            gear.Sum(d => d.Initiative),
            gear.Sum(d => d.FightLuck),
            gear.Sum(d => d.Rearm),
            gear.Sum(d => d.StartBlock));
    }
}
