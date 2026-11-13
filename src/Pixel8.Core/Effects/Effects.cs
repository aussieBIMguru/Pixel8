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
        .Register("Stabilize", new StabilizeEffect());
}

// ---------------------------------------------------------------------- built-in effects

/// <summary>The source's weapon dice (enemies use args.dice), plus args.bonus (upgrades). A crit doubles the dice.</summary>
public sealed class WeaponDamageEffect : IEffect
{
    public string DefaultTarget => TargetKey.Target;
    public bool TargetsAllies => false;

    public void Apply(EffectContext ctx)
    {
        var dice = (ctx.Source is Hero h ? h.Weapon : ctx.DiceArg("dice", "1d4")).WithBonus(ctx.IntArg("bonus"));
        int amount = ctx.Combat.RollDamage(ctx.Source, ctx.Target, dice, ctx.Critical, type: "weapon");
        ctx.Combat.DealDamage(ctx.Source, ctx.Target, amount, "weapon");
    }
}

/// <summary>args: dice, type, half ("true" halves the roll, e.g. a made save).</summary>
public sealed class DamageEffect : IEffect
{
    public string DefaultTarget => TargetKey.Target;
    public bool TargetsAllies => false;

    public void Apply(EffectContext ctx)
    {
        int amount = ctx.Combat.RollDamage(ctx.Source, ctx.Target, ctx.DiceArg("dice"), ctx.Critical, ctx.BoolArg("half"), ctx.Arg("type", "damage"));
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
        ctx.Combat.Heal(ctx.Target, ctx.DiceArg("dice").Roll(ctx.Combat.Rng));
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
            ctx.Combat.Heal(ctx.Target, 1);
        else
            ctx.Combat.GainBlock(ctx.Target, ctx.IntArg("block", 3));
    }
}

/// <summary>Reaction only. args: targetDelta (raises the number the incoming attack needed to hit).</summary>
public sealed class ModifyIncomingRollEffect : IEffect
{
    public string DefaultTarget => TargetKey.None;
    public bool TargetsAllies => false;

    public void Apply(EffectContext ctx) => ctx.Combat.AdjustIncomingTarget(ctx.IntArg("targetDelta"));
}
