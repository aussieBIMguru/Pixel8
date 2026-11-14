using Pixel8.Core.Cards;
using Pixel8.Core.Combat;
using Pixel8.Core.Dice;
using Pixel8.Core.Heroes;

namespace Pixel8.Core.Effects;

/// <summary>Target keys an effect can name in its "target" arg.</summary>
public static class TargetKey
{
    /// <summary>The target the player picked (or, for a checked card, each creature the check was rolled against).</summary>
    public const string Target = "target";
    public const string Self = "self";
    /// <summary>All living allies of the source, Downed heroes included.</summary>
    public const string Party = "party";
    public const string AllEnemies = "allEnemies";
    /// <summary>No formation in the MVP: a random other enemy.</summary>
    public const string Adjacent = "adjacent";
    /// <summary>Two different random living enemies (one if only one is left). Round 8 multi-target cards.</summary>
    public const string TwoRandom = "random2";
    /// <summary>The effect doesn't touch a creature (FixNextDie, ModifyIncomingRoll).</summary>
    public const string None = "none";
}

/// <summary>Everything an effect needs to resolve against one target.</summary>
public sealed class EffectContext
{
    public required CombatState Combat { get; init; }
    public required Combatant Source { get; init; }
    public required Combatant Target { get; init; }
    public required EffectDef Def { get; init; }
    public CardInstance? Card { get; init; }
    public CheckResult? Check { get; init; }
    /// <summary>The check was a critical: dice in damage effects are doubled.</summary>
    public bool Critical { get; init; }

    public string Arg(string key, string fallback = "") =>
        Def.Args is not null && Def.Args.TryGetValue(key, out var v) ? v : fallback;

    public int IntArg(string key, int fallback = 0) =>
        int.TryParse(Arg(key), out int v) ? v : fallback;

    public bool BoolArg(string key) =>
        bool.TryParse(Arg(key), out bool v) && v;

    public DiceExpr DiceArg(string key, string fallback = "0") => DiceExpr.Parse(Arg(key, fallback));
}

/// <summary>One building block of a card. Registered by name in <see cref="EffectRegistry"/> and referenced by "type" in JSON.</summary>
public interface IEffect
{
    /// <summary>Target key used when the effect's args don't name one.</summary>
    string DefaultTarget { get; }

    /// <summary>When the target key is "target", the picked target must be a hero (heals, buffs) rather than an enemy.</summary>
    bool TargetsAllies { get; }

    void Apply(EffectContext ctx);
}

public sealed class EffectRegistry
{
    private readonly Dictionary<string, IEffect> _effects = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> Names => _effects.Keys;

    public EffectRegistry Register(string name, IEffect effect)
    {
        _effects[name] = effect;
        return this;
    }

    public bool Contains(string name) => _effects.ContainsKey(name);

    public IEffect Get(string name) =>
        _effects.TryGetValue(name, out var e) ? e : throw new KeyNotFoundException($"Unknown effect type '{name}'.");

    public static EffectRegistry CreateDefault() => new EffectRegistry()
        .Register("WeaponDamage", new WeaponDamageEffect())
        .Register("Damage", new DamageEffect())
        .Register("BonusDamage", new BonusDamageEffect())
        .Register("Block", new BlockEffect())
        .Register("Heal", new HealEffect())
        .Register("Apply", new ApplyEffect())
        .Register("FixNextDie", new FixNextDieEffect())
        .Register("ModifyIncomingRoll", new ModifyIncomingRollEffect())
        .Register("Stabilize", new StabilizeEffect())
        .Register("Poison", new PoisonEffect())
        .Register("Draw", new DrawEffect())
        // Round 11: new classes
        .Register("Inspire", new InspireEffect())
        .Register("Smite", new SmiteEffect())
        .Register("WildShape", new WildShapeEffect())
        .Register("HuntersMark", new HuntersMarkEffect())
        .Register("LayOnHands", new LayOnHandsEffect())
        .Register("TempHp", new TempHpEffect());
}

// ---------------------------------------------------------------------- built-in effects

/// <summary>The source's weapon dice (enemies use args.dice), plus args.bonus (upgrades). A crit doubles the dice.</summary>
public sealed class WeaponDamageEffect : IEffect
{
    public string DefaultTarget => TargetKey.Target;
    public bool TargetsAllies => false;

    public void Apply(EffectContext ctx)
    {
        // Round 11: a Druid in Wild Shape uses the beast's attack instead of their weapon.
        var dice = (ctx.Source is Hero h ? ctx.Combat.WeaponOf(h).WithBonus(ctx.Combat.HeroDamageBonus) : ctx.DiceArg("dice", "1d4")).WithBonus(ctx.IntArg("bonus"));
        int amount = ctx.Combat.RollDamage(ctx.Source, ctx.Target, dice, ctx.Critical, type: "weapon");
        ctx.Combat.DealDamage(ctx.Source, ctx.Target, amount, "weapon");
        if (ctx.Source is Hero hero && ctx.Target is not null && ctx.Combat.FormOf(hero) is null)
            ctx.Combat.ApplyWeaponPassives(hero, ctx.Target, ctx.Critical);
    }
}

/// <summary>args: dice, type, half ("true" halves the roll, e.g. a made save).</summary>
public sealed class DamageEffect : IEffect
{
    public string DefaultTarget => TargetKey.Target;
    public bool TargetsAllies => false;

    public void Apply(EffectContext ctx)
    {
        var dice = ctx.DiceArg("dice");
        if (ctx.Source is Hero) dice = dice.WithBonus(ctx.Combat.HeroDamageBonus);   // level scaling (round 8)
        int amount = ctx.Combat.RollDamage(ctx.Source, ctx.Target, dice, ctx.Critical, ctx.BoolArg("half"), ctx.Arg("type", "damage"));
        ctx.Combat.DealDamage(ctx.Source, ctx.Target, amount, ctx.Arg("type", "damage"));
    }
}

/// <summary>args: dice, type, if ("target.Marked|target.Prone|allyHitThisRound", any one is enough).</summary>
public sealed class BonusDamageEffect : IEffect
{
    public string DefaultTarget => TargetKey.Target;
    public bool TargetsAllies => false;

    public void Apply(EffectContext ctx)
    {
        string condition = ctx.Arg("if");
        if (condition.Length > 0 && !ctx.Combat.EvaluateCondition(condition, ctx.Source, ctx.Target))
            return;
        int amount = ctx.Combat.RollDamage(ctx.Source, ctx.Target, ctx.DiceArg("dice"), ctx.Critical, type: ctx.Arg("type", "bonus"));
        ctx.Combat.DealDamage(ctx.Source, ctx.Target, amount, ctx.Arg("type", "bonus"));
    }
}

/// <summary>args: amount (dice or flat). Targets self unless told otherwise.</summary>
public sealed class BlockEffect : IEffect
{
    public string DefaultTarget => TargetKey.Self;
    public bool TargetsAllies => true;

    public void Apply(EffectContext ctx) =>
        ctx.Combat.GainBlock(ctx.Target, ctx.DiceArg("amount").Roll(ctx.Combat.Rng));
}

/// <summary>args: dice. Healing a Downed hero brings them back up.</summary>
public sealed class HealEffect : IEffect
{
    public string DefaultTarget => TargetKey.Self;
    public bool TargetsAllies => true;

    public void Apply(EffectContext ctx) =>
        ctx.Combat.Heal(ctx.Target, ctx.DiceArg("dice").Roll(ctx.Combat.Rng), ctx.Source);
}

/// <summary>args: cond, rounds.</summary>
public sealed class ApplyEffect : IEffect
{
    public string DefaultTarget => TargetKey.Target;
    public bool TargetsAllies => false;

    public void Apply(EffectContext ctx) =>
        ctx.Combat.ApplyCondition(ctx.Target, ctx.Arg("cond"), ctx.IntArg("rounds", 1));
}

/// <summary>args: value. The next d20 any hero rolls uses this natural value.</summary>
public sealed class FixNextDieEffect : IEffect
{
    public string DefaultTarget => TargetKey.None;
    public bool TargetsAllies => false;

    public void Apply(EffectContext ctx) => ctx.Combat.FixedNextDie = Math.Clamp(ctx.IntArg("value", 10), 1, 20);
}

/// <summary>Spare the Dying: a Downed ally gets back up at 1 HP; anyone else gains args.block (default 3) Block.</summary>
public sealed class StabilizeEffect : IEffect
{
    public string DefaultTarget => TargetKey.Target;
    public bool TargetsAllies => true;

    public void Apply(EffectContext ctx)
    {
        if (ctx.Target is Hero { IsDowned: true })
            ctx.Combat.Heal(ctx.Target, 1, ctx.Source);
        else
            ctx.Combat.GainBlock(ctx.Target, ctx.IntArg("block", 3));
    }
}

/// <summary>
/// args: amount (damage at the start of each of the bearer's turns), rounds (how many turns). The target is Poisoned;
/// re-applying keeps the longer duration and the bigger amount. Round 8.
/// </summary>
public sealed class PoisonEffect : IEffect
{
    public string DefaultTarget => TargetKey.Target;
    public bool TargetsAllies => false;

    public void Apply(EffectContext ctx) =>
        ctx.Combat.ApplyPoison(ctx.Target, Math.Max(1, ctx.IntArg("amount", 2)), Math.Max(1, ctx.IntArg("rounds", 3)));
}

/// <summary>args: count (default 1). The hero draws cards (hand limit applies). Rare cards only (decks never reshuffle).</summary>
public sealed class DrawEffect : IEffect
{
    public string DefaultTarget => TargetKey.Self;
    public bool TargetsAllies => true;

    public void Apply(EffectContext ctx)
    {
        if (ctx.Target is Hero h) ctx.Combat.DrawCards(h, Math.Max(1, ctx.IntArg("count", 1)));
    }
}

/// <summary>Reaction only. args: targetDelta (raises the number the incoming attack needed to hit).</summary>
public sealed class ModifyIncomingRollEffect : IEffect
{
    public string DefaultTarget => TargetKey.None;
    public bool TargetsAllies => false;

    public void Apply(EffectContext ctx) => ctx.Combat.AdjustIncomingTarget(ctx.IntArg("targetDelta"));
}

// ---------------------------------------------------------------------- round 11: new classes

/// <summary>
/// Bard. args: dice (default 1d6), count (default 1). The target holds that many Inspiration dice; each is spent
/// automatically when it could turn one of their failed d20 rolls (attack or save) into a success.
/// </summary>
public sealed class InspireEffect : IEffect
{
    public string DefaultTarget => TargetKey.Target;
    public bool TargetsAllies => true;

    public void Apply(EffectContext ctx)
    {
        if (ctx.Target is Hero h)
            ctx.Combat.Inspire(h, ctx.DiceArg("dice", "1d6"), Math.Max(1, ctx.IntArg("count", 1)));
    }
}

/// <summary>Paladin. args: dice (default 2d8), undead (extra vs undead, default 1d8). The hero's next hit this fight smites.</summary>
public sealed class SmiteEffect : IEffect
{
    public string DefaultTarget => TargetKey.Self;
    public bool TargetsAllies => true;

    public void Apply(EffectContext ctx)
    {
        if (ctx.Target is Hero h)
            ctx.Combat.PrimeSmite(h, ctx.DiceArg("dice", "2d8"), ctx.DiceArg("undead", "1d8"));
    }
}

/// <summary>
/// Druid. args: form (sprite id, e.g. "wolf"), name, tempHp, dice (the beast's attack). The hero gains the temp HP and
/// their Strike and weapon attacks become the beast's (with their spell attack bonus) until the temp HP is gone.
/// </summary>
public sealed class WildShapeEffect : IEffect
{
    public string DefaultTarget => TargetKey.Self;
    public bool TargetsAllies => true;

    public void Apply(EffectContext ctx)
    {
        if (ctx.Target is Hero h)
            ctx.Combat.WildShape(h, new WildForm(ctx.Arg("form", "wolf"), ctx.Arg("name", "Wolf"),
                ctx.DiceArg("dice", "2d4+2"), Math.Max(1, ctx.IntArg("tempHp", 10))));
    }
}

/// <summary>Ranger. args: dice (default 1d6). The target is Hunted: the source's hits on it deal the extra dice. The mark moves when it dies.</summary>
public sealed class HuntersMarkEffect : IEffect
{
    public string DefaultTarget => TargetKey.Target;
    public bool TargetsAllies => false;

    public void Apply(EffectContext ctx)
    {
        if (ctx.Source is Hero h)
            ctx.Combat.Hunt(h, ctx.Target, ctx.DiceArg("dice", "1d6"));
    }
}

/// <summary>Paladin. Heals the target from the hero's Lay on Hands pool (balance layOnHandsBase + per level); revives a Downed ally.</summary>
public sealed class LayOnHandsEffect : IEffect
{
    public string DefaultTarget => TargetKey.Target;
    public bool TargetsAllies => true;

    public void Apply(EffectContext ctx)
    {
        if (ctx.Source is Hero h) ctx.Combat.LayOnHands(h, ctx.Target);
    }
}

/// <summary>args: amount (dice or flat). Temporary hit points (they don't stack: the bigger value stays).</summary>
public sealed class TempHpEffect : IEffect
{
    public string DefaultTarget => TargetKey.Self;
    public bool TargetsAllies => true;

    public void Apply(EffectContext ctx) =>
        ctx.Combat.GainTempHp(ctx.Target, ctx.DiceArg("amount", "5").Roll(ctx.Combat.Rng));
}
