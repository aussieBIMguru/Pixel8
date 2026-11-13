using Pixel8.Core.Cards;
using Pixel8.Core.Combat;
using Pixel8.Core.Content;
using Pixel8.Core.Dice;
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
        Deck = new HeroDeck(deck.Select(d => new CardInstance(d)), rng, maxHand);
    }

    public HeroClassDef Class { get; }
    public int AttackMod => Class.AttackMod;
    public int SpellDc => Class.SpellDc;
    /// <summary>5e spell attack bonus: spell save DC − 8 (proficiency + ability).</summary>
    public int SpellAttackMod => Class.SpellDc - 8;
    public DiceExpr Weapon { get; }
    public HeroDeck Deck { get; }

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
            return hero;
        });
        return new Party(heroes)
        {
            Gold = content.Balance.StartingGold,
            Luck = content.Balance.StartingLuck
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
