using Pixel8.Core.Cards;
using Pixel8.Core.Combat;
using Pixel8.Core.Content;
using Pixel8.Core.Dice;
using Pixel8.Core.Gear;
using Pixel8.Core.Randomness;

namespace Pixel8.Core.Heroes;

/// <summary>
/// A party member. HP, deck and Downed state persist between fights; Block and conditions do not.
/// </summary>
public sealed class Hero : Combatant
{
    public Hero(HeroClassDef cls, string name, IEnumerable<CardDef> deck, Rng rng, int maxHand = 5)
        : base(name, cls.Hp, cls.Ac, cls.InitMod)
    {
        Class = cls;
        Weapon = DiceExpr.Parse(string.IsNullOrWhiteSpace(cls.Weapon) ? "1d4" : cls.Weapon);
        AttackMod = cls.AttackMod;
        SpellDc = cls.SpellDc;
        Deck = new HeroDeck(deck.Select(d => new CardInstance(d)), rng, maxHand);
    }

    public HeroClassDef Class { get; }
    /// <summary>Weapon attack modifier (round 10: from STR/DEX and gear; heroes.json attackMod without gear).</summary>
    public int AttackMod { get; private set; }
    public int SpellDc { get; private set; }
    /// <summary>Bonus to every saving throw from gear.</summary>
    public int SaveBonus { get; private set; }
    /// <summary>CON saving throw modifier (dragon breath and other area attacks), including gear.</summary>
    public int ConSave => Class.ConSave + SaveBonus;
    /// <summary>5e spell attack bonus: spell save DC − 8 (proficiency + ability).</summary>
    public int SpellAttackMod => SpellDc - 8;
    public DiceExpr Weapon { get; private set; }
    public HeroDeck Deck { get; }

    // ------------------------------------------------------------------ gear (round 10)

    /// <summary>The four equipped slots. Change them through Run (Equip / Unequip), which keeps stats in step.</summary>
    public Loadout Loadout { get; } = new();
    /// <summary>Initiative from gear (added to the class InitMod when rolling).</summary>
    public int InitBonus { get; private set; }
    /// <summary>Extra Luck at the start of every fight (Stone of Good Luck).</summary>
    public int FightLuckBonus { get; private set; }
    /// <summary>Extra cards a Rearm may take (Pearl of Power).</summary>
    public int RearmBonus { get; private set; }
    /// <summary>Block at the start of every fight (Crystal Orb).</summary>
    public int StartBlock { get; private set; }
    /// <summary>Max HP currently added (or removed) by gear, already included in MaxHp.</summary>
    public int GearHp { get; private set; }

    /// <summary>Gear maths needs the class's STR (and DEX). Classes without it keep their fixed heroes.json numbers.</summary>
    public bool UsesGear => Class.Str is not null;

    public bool CanUse(GearDef gear) => ContentDb.CanUse(Class, gear);

    /// <summary>
    /// Recomputes attack, weapon dice, AC, spell DC, saves and the other gear bonuses from the class and Loadout.
    /// With <paramref name="adjustHp"/>, a change in gear max HP is applied to MaxHp (gaining also heals that much;
    /// losing never drops a standing hero below 1 HP). Loading a save passes false: its MaxHp already includes gear.
    /// </summary>
    public void RecalculateGear(bool adjustHp = true)
    {
        if (!UsesGear)
        {
            Weapon = DiceExpr.Parse(string.IsNullOrWhiteSpace(Class.Weapon) ? "1d4" : Class.Weapon);
            AttackMod = Class.AttackMod;
            SpellDc = Class.SpellDc;
            Ac = Class.Ac;
            return;
        }
        var st = GearRules.Compute(Class.Str!.Value, Class.Dex ?? Class.InitMod, Class.SpellDc, 0, Loadout);
        AttackMod = st.AttackMod;
        Weapon = st.Weapon;
        Ac = st.Ac;
        SpellDc = st.SpellDc;
        SaveBonus = st.Saves;
        InitBonus = st.Initiative;
        FightLuckBonus = st.FightLuck;
        RearmBonus = st.Rearm;
        StartBlock = st.StartBlock;

        int delta = st.MaxHpBonus - GearHp;
        GearHp = st.MaxHpBonus;
        if (!adjustHp || delta == 0) return;
        MaxHp = Math.Max(1, MaxHp + delta);
        if (IsDead) return;
        if (delta > 0 && !IsDowned) Hp += delta;
        Hp = Math.Clamp(Hp, IsDowned ? 0 : 1, MaxHp);
    }

    /// <summary>Loading a save: the gear max HP that MaxHp already includes.</summary>
    internal void RestoreGearHp(int gearHp) => GearHp = gearHp;

    /// <summary>Puts gear straight into a slot (start of run, loading a save). Run.Equip is the checked way.</summary>
    internal void SetSlot(GearSlot slot, GearInstance? gear) => Loadout[slot] = gear;

    /// <summary>At 0 HP: can't act, rolls a death save at the start of each of their turns.</summary>
    public bool IsDowned { get; set; }
    /// <summary>Downed with 3 successful death saves: stops rolling, wakes at 1 HP after the fight.</summary>
    public bool IsStable { get; set; }
    public bool IsDead { get; set; }
    public int DeathSaveSuccesses { get; set; }
    public int DeathSaveFailures { get; set; }

    public override bool IsHero => true;
    public override bool IsAlive => !IsDead;
    public override bool CanAct => !IsDead && !IsDowned;

    /// <summary>Clears Downed state and sets HP (healing a Downed hero, a nat-20 death save, or the end of a fight).</summary>
    public void Revive(int hp)
    {
        if (IsDead) return;
        IsDowned = false;
        IsStable = false;
        DeathSaveSuccesses = 0;
        DeathSaveFailures = 0;
        Hp = Math.Clamp(hp, 1, MaxHp);
    }
}

/// <summary>The three heroes plus shared resources.</summary>
public sealed class Party
{
    private readonly List<Hero> _heroes = new();

    public Party(IEnumerable<Hero> heroes) => _heroes.AddRange(heroes);

    public IReadOnlyList<Hero> Heroes => _heroes;

    public int Gold { get; set; }
    /// <summary>Luck pips: spent to reroll a die.</summary>
    public int Luck { get; set; }
    /// <summary>Torch meter: 1 per move on the map; also paid to flee a fight.</summary>
    public int Torch { get; set; }
    /// <summary>Item ids (see items.json).</summary>
    public List<string> Bag { get; } = new();

    /// <summary>Round 10: unequipped gear, shared by the party (balance gear.packSize slots).</summary>
    public List<GearInstance> Pack { get; } = new();
    public int PackSize { get; set; } = 8;
    public bool PackFull => Pack.Count >= PackSize;

    // Round 10 achievement counters for this session of the run (not saved: counted as they happen).
    public int TreasuresOpened { get; set; }
    public int UpgradesMade { get; set; }
    public int GoldSpent { get; set; }

    /// <summary>Every piece of gear the party has: equipped by living heroes, then the pack.</summary>
    public IEnumerable<GearInstance> AllGear =>
        _heroes.Where(h => !h.IsDead).SelectMany(h => h.Loadout.Equipped).Concat(Pack);

    /// <summary>Spends gold (merchant, events, fleeing). Counts toward Big Spender.</summary>
    public void Spend(int amount)
    {
        if (amount <= 0) return;
        int paid = Math.Min(amount, Gold);
        Gold -= paid;
        GoldSpent += paid;
    }

    /// <summary>Shared party experience (from fights) and level. Thresholds live in balance.json levelXp.</summary>
    public int Xp { get; set; }
    public int Level { get; set; } = 1;

    public bool IsWiped => _heroes.All(h => !h.CanAct);

    /// <summary>Builds a party from class ids using each class's starting deck from heroes.json.</summary>
    public static Party Create(ContentDb content, IEnumerable<string> classIds, Rng rng, DifficultyDef? difficulty = null)
    {
        double hpScale = difficulty?.HeroHpMultiplier ?? 1;
        var heroes = classIds.Select(id =>
        {
            var cls = content.HeroClass(id);
            var deck = (cls.StartingDeck ?? Array.Empty<string>()).Select(content.Card);
            var hero = new Hero(cls, cls.Name, deck, rng, content.Balance.MaxHand);
            hero.MaxHp = hero.Hp = Math.Max(1, (int)Math.Round(cls.Hp * hpScale));
            // Round 10: starting gear (it reproduces the class's old fixed numbers).
            foreach (var sid in cls.StartingGear ?? Array.Empty<string>())
                if (content.Gear.TryGetValue(sid, out var g))
                    hero.SetSlot(g.Slot, new GearInstance(g));
            hero.RecalculateGear();
            return hero;
        });
        return new Party(heroes)
        {
            Gold = content.Balance.StartingGold,
            Luck = content.Balance.StartingLuck,
            PackSize = Math.Max(1, content.Balance.Gear.PackSize)
        };
    }

    /// <summary>Start of a Depth: every hero draws to a full hand and the Torch is set from the dungeon.</summary>
    public void StartDepth(int torch)
    {
        Torch = torch;
        foreach (var h in _heroes.Where(h => !h.IsDead))
            h.Deck.DrawToFull();
    }
}
