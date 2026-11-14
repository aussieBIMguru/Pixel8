using Pixel8.Core.Content;

namespace Pixel8.Core.Combat;

/// <summary>Well-known condition names used by the rules. Cards can apply any name; these ones have built-in meaning.</summary>
public static class Cond
{
    /// <summary>Enemies must target this hero.</summary>
    public const string Taunting = "Taunting";
    /// <summary>Attacks against this creature crit on 19–20.</summary>
    public const string Marked = "Marked";
    /// <summary>+1d4 to attack rolls.</summary>
    public const string Blessed = "Blessed";
    /// <summary>Attacks against this creature have advantage.</summary>
    public const string Prone = "Prone";
    /// <summary>Rogue's Cunning: this hero's next attack has advantage (used up by the attack).</summary>
    public const string Cunning = "Cunning";
    /// <summary>Takes poison damage at the start of each of its turns (amount kept by CombatState).</summary>
    public const string Poisoned = "Poisoned";

    // Round 11
    /// <summary>Loses their next turn (Lich's Paralyzing Touch). Attacks against them have advantage.</summary>
    public const string Paralyzed = "Paralyzed";
    /// <summary>Loses their next turn (Vampire's charm).</summary>
    public const string Charmed = "Charmed";
    /// <summary>Attacks against it have advantage; its own attacks have disadvantage (Basilisk gaze, Ensnaring Strike, Entangle).</summary>
    public const string Restrained = "Restrained";
    /// <summary>Faerie Fire / Vicious Mockery: attacks against it have advantage.</summary>
    public const string Outlined = "Outlined";
    /// <summary>Hunter's Mark: the Ranger's hits on it deal extra damage. The mark moves when it dies.</summary>
    public const string Hunted = "Hunted";
    /// <summary>Holds Bardic Inspiration dice, spent automatically to turn a failed d20 into a success.</summary>
    public const string Inspired = "Inspired";
    /// <summary>Divine Smite is primed: the next hit deals extra radiant damage.</summary>
    public const string Smiting = "Smiting";
    /// <summary>Wild Shape: a Druid in beast form (temp HP; Strike uses the beast's attack).</summary>
    public const string Wild = "Wild";

    /// <summary>Conditions that make the bearer lose their turn.</summary>
    public static bool SkipsTurn(ConditionSet c) => c.Has(Paralyzed) || c.Has(Charmed);
}

/// <summary>
/// Conditions with a duration in rounds. Durations tick down at the start of the bearer's own turn,
/// so "1 round" means "until the start of your next turn" (the 5e convention).
/// </summary>
public sealed class ConditionSet
{
    private readonly Dictionary<string, int> _rounds = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> Active => _rounds.Keys;

    public bool Has(string name) => _rounds.ContainsKey(name);

    public int RoundsLeft(string name) => _rounds.TryGetValue(name, out int r) ? r : 0;

    /// <summary>Re-applying keeps the longer duration.</summary>
    public void Add(string name, int rounds) => _rounds[name] = Math.Max(Math.Max(rounds, 1), RoundsLeft(name));

    public bool Remove(string name) => _rounds.Remove(name);

    public void Clear() => _rounds.Clear();

    /// <summary>Called at the start of the bearer's turn. Returns the conditions that ran out.</summary>
    public List<string> TickStartOfTurn()
    {
        var expired = new List<string>();
        foreach (string key in _rounds.Keys.ToList())
        {
            int left = _rounds[key] - 1;
            if (left <= 0)
            {
                _rounds.Remove(key);
                expired.Add(key);
            }
            else
            {
                _rounds[key] = left;
            }
        }
        return expired;
    }
}

/// <summary>Anything that takes part in a fight: a hero or an enemy.</summary>
public abstract class Combatant
{
    private static int _nextId;

    protected Combatant(string name, int maxHp, int ac, int initMod)
    {
        Id = Interlocked.Increment(ref _nextId);
        Name = name;
        MaxHp = maxHp;
        Hp = maxHp;
        Ac = ac;
        InitMod = initMod;
    }

    /// <summary>Process-unique id. Not stable across runs, so never log it or use it for ordering.</summary>
    public int Id { get; }
    public string Name { get; }
    public int MaxHp { get; set; }
    public int Hp { get; set; }
    public int Ac { get; set; }
    public int InitMod { get; }

    /// <summary>Absorbs damage before HP. Cleared at the start of the bearer's turn and at the end of a fight.</summary>
    public int Block { get; set; }

    /// <summary>Round 11: temporary hit points (Wild Shape). Lost after Block, before HP; gone at the end of a fight.</summary>
    public int TempHp { get; set; }

    public ConditionSet Conditions { get; } = new();

    public abstract bool IsHero { get; }

    /// <summary>Still part of the fight (a Downed hero is alive; a dead one isn't).</summary>
    public abstract bool IsAlive { get; }

    /// <summary>Can take turns and be chosen as an attack target.</summary>
    public abstract bool CanAct { get; }

    public override string ToString() => Name;
}

public sealed class Enemy : Combatant
{
    /// <param name="maxHp">Overrides the SRD HP (balance scaling). Use <c>Encounters.Spawn</c> rather than calling this directly.</param>
    public Enemy(EnemyDef def, string? name = null, int? maxHp = null)
        : base(name ?? def.Name, Math.Max(1, maxHp ?? def.Hp), def.Ac, def.InitMod)
    {
        Def = def;
    }

    public EnemyDef Def { get; }
    public int SaveMod => Def.SaveMod;

    public override bool IsHero => false;
    public override bool IsAlive => Hp > 0;
    public override bool CanAct => Hp > 0;
}
