using Pixel8.Core.Randomness;

namespace Pixel8.Core.Dice;

public enum RollMode { Normal, Advantage, Disadvantage }

/// <summary>
/// What the player sees before committing: "HIT 11-20 · 50%".
/// Produced by <see cref="D20.Odds"/>, which is the single source of truth for both UI and AI/sim.
/// </summary>
public readonly record struct CheckOdds(int Needed, RollMode Mode, double Chance)
{
    public string RangeText => Needed >= 20 ? "20" : $"{Needed}-20";

    public int Percent => (int)Math.Round(Chance * 100);

    public override string ToString()
    {
        string mode = Mode switch
        {
            RollMode.Advantage => " ADV",
            RollMode.Disadvantage => " DIS",
            _ => ""
        };
        return $"{RangeText}{mode} {Percent}%";
    }
}

public readonly record struct CheckResult(
    int Die1, int Die2, int Kept, int Modifier, int Target, int CritMin,
    bool Success, bool Critical, bool Fumble)
{
    public int Total => Kept + Modifier;

    /// <summary>After-roll nudge (Bless that lands late, Cutting Words, Flash of Genius): keeps the die, changes the total.</summary>
    public CheckResult WithExtraModifier(int extra) =>
        D20.Evaluate(Die1, Die2, Kept, Modifier + extra, Target, CritMin);
}

/// <summary>
/// d20 + modifier ≥ target. Natural 1 always fails; a natural roll ≥ critMin (normally 20) always
/// succeeds and is a critical. Effects that widen the crit range (Champion, Marked) lower critMin.
/// </summary>
public static class D20
{
    /// <summary>Lowest natural roll that succeeds.</summary>
    public static int Needed(int modifier, int target, int critMin = 20) =>
        Math.Min(Math.Max(target - modifier, 2), critMin);

    public static CheckOdds Odds(int modifier, int target, RollMode mode = RollMode.Normal, int critMin = 20)
    {
        int needed = Needed(modifier, target, critMin);
        double p = (21 - needed) / 20.0;
        double chance = mode switch
        {
            RollMode.Advantage => 1 - (1 - p) * (1 - p),
            RollMode.Disadvantage => p * p,
            _ => p
        };
        return new CheckOdds(needed, mode, chance);
    }

    /// <summary>Advantage and disadvantage cancel out, as in 5e.</summary>
    public static RollMode Combine(bool advantage, bool disadvantage) =>
        advantage == disadvantage ? RollMode.Normal
        : advantage ? RollMode.Advantage
        : RollMode.Disadvantage;

    /// <param name="fixedDie">Set by "Loaded Die" style effects: the natural roll is forced.</param>
    public static CheckResult Roll(Rng rng, int modifier, int target,
        RollMode mode = RollMode.Normal, int critMin = 20, int? fixedDie = null)
    {
        int a = fixedDie ?? rng.Roll(20);
        int b = (mode == RollMode.Normal || fixedDie.HasValue) ? a : rng.Roll(20);
        int kept = mode switch
        {
            RollMode.Advantage => Math.Max(a, b),
            RollMode.Disadvantage => Math.Min(a, b),
            _ => a
        };
        return Evaluate(a, b, kept, modifier, target, critMin);
    }

    public static CheckResult Evaluate(int die1, int die2, int kept, int modifier, int target, int critMin)
    {
        bool fumble = kept == 1;
        bool critical = !fumble && kept >= critMin;
        bool success = !fumble && (critical || kept + modifier >= target);
        return new CheckResult(die1, die2, kept, modifier, target, critMin, success, critical, fumble);
    }

    /// <summary>Re-roll the natural die (Luck pip, Lucky Coin) keeping the same modifier, target and mode.</summary>
    public static CheckResult Reroll(Rng rng, CheckResult previous, RollMode mode = RollMode.Normal) =>
        Roll(rng, previous.Modifier, previous.Target, mode, previous.CritMin);
}
