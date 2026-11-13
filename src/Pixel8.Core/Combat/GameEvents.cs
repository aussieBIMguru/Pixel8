using Pixel8.Core.Cards;
using Pixel8.Core.Content;
using Pixel8.Core.Dice;
using Pixel8.Core.Heroes;

namespace Pixel8.Core.Combat;

/// <summary>
/// Everything that happens in a fight, in order. The rules never touch the UI: the combat screen replays
/// this log to animate dice, damage numbers and screen shake, and the Sim prints it.
/// </summary>
public abstract record GameEvent;

public sealed record CombatStarted(IReadOnlyList<Combatant> TurnOrder) : GameEvent;
public sealed record RoundStarted(int Round) : GameEvent;
public sealed record TurnStarted(Combatant Actor) : GameEvent;
public sealed record CardsDrawn(Hero Hero, int Count) : GameEvent;
public sealed record IntentChosen(Enemy Enemy, IntentDef Intent, Hero? Target, CheckOdds? Odds) : GameEvent;
public sealed record CardPlayed(Combatant Actor, CardDef Card, Combatant? Target) : GameEvent;
/// <summary>
/// One labelled piece of a roll for the roll strip: "+5 ATK", "+2 BLESS", or a tag with no number ("ADV PRONE", "MARKED 19-20").
/// </summary>
public sealed record RollPart(string Label, int Value = 0, bool Tag = false)
{
    public string Text => Tag ? Label : (Value >= 0 ? $"+{Value} {Label}" : $"{Value} {Label}");
}

/// <param name="Parts">How the modifier is made up (numeric parts sum to <c>Result.Modifier</c>) plus tags such as ADV.</param>
/// <param name="Versus">"AC" for attacks, "DC" for saves.</param>
/// <param name="IsReroll">A Luck reroll of the previous check.</param>
public sealed record CheckRolled(Combatant Roller, Combatant Against, CheckResult Result, string Label,
    IReadOnlyList<RollPart>? Parts = null, string Versus = "AC", bool IsReroll = false) : GameEvent
{
    /// <summary>Parts, or a single "+n MOD" when the roller didn't supply any.</summary>
    public IReadOnlyList<RollPart> Breakdown => Parts ?? (Result.Modifier == 0
        ? Array.Empty<RollPart>()
        : new[] { new RollPart("MOD", Result.Modifier) });
}

public sealed record RollModified(Combatant By, CheckResult Result) : GameEvent;

/// <summary>
/// Damage dice before Block, for the roll strip: "2d8+4 → 3 7 +4 = 14", then any multipliers
/// ("EASY x0.85", "GOOD x0.5", "HALF") in <paramref name="Notes"/>. <paramref name="Total"/> is the final amount.
/// </summary>
public sealed record DamageRolled(Combatant Source, Combatant Target, DiceExpr Dice, IReadOnlyList<int> Faces,
    bool Critical, int Total, IReadOnlyList<string> Notes, string Type = "") : GameEvent
{
    public int Raw => Math.Max(0, Faces.Sum() + Dice.Bonus);
}

/// <summary>A hero's d20 failed and the party has Luck: <see cref="CombatState.PendingLuck"/> waits for Y/N.</summary>
public sealed record LuckOffered(Hero Hero, CheckResult Result, int LuckLeft) : GameEvent;
public sealed record LuckSpent(Hero Hero, int LuckLeft) : GameEvent;

/// <summary>A big attack hit: <see cref="CombatState.PendingTiming"/> waits for the timing mini-game.</summary>
public sealed record TimingOffered(Enemy Attacker, Hero Defender, IntentDef Intent) : GameEvent;
public sealed record TimingResolved(Hero Defender, TimingGrade Grade) : GameEvent;
public sealed record DamageDealt(Combatant Source, Combatant Target, int Amount, int Blocked, string Type) : GameEvent;
public sealed record Healed(Combatant Target, int Amount) : GameEvent;
public sealed record BlockGained(Combatant Target, int Amount) : GameEvent;
public sealed record ConditionApplied(Combatant Target, string Condition, int Rounds) : GameEvent;
public sealed record ConditionExpired(Combatant Target, string Condition) : GameEvent;
public sealed record DieFixed(int Value) : GameEvent;
public sealed record HeroDowned(Hero Hero) : GameEvent;
public sealed record DeathSaveRolled(Hero Hero, int Roll, int Successes, int Failures) : GameEvent;
public sealed record CombatantDied(Combatant Combatant) : GameEvent;
public sealed record ReactionOffered(Hero Hero, IReadOnlyList<CardInstance> Options) : GameEvent;
public sealed record PartyFled(int TorchPaid, int GoldPaid) : GameEvent;
/// <summary>A hero used a Bag item (Quick action) on <paramref name="Target"/> (themselves if null).</summary>
public sealed record ItemUsed(Hero Hero, ItemDef Item, Combatant? Target) : GameEvent;
public sealed record CombatEnded(CombatPhase Outcome, int Gold) : GameEvent;

public static class GameEventText
{
    /// <summary>One-line description for logs and the Sim. Contains no ids, so equal seeds give equal text.</summary>
    public static string Describe(GameEvent e) => e switch
    {
        CombatStarted s => "Initiative: " + string.Join(", ", s.TurnOrder.Select(c => c.Name)),
        RoundStarted r => $"--- Round {r.Round} ---",
        TurnStarted t => $"{t.Actor.Name}'s turn ({t.Actor.Hp}/{t.Actor.MaxHp} HP)",
        CardsDrawn d => $"{d.Hero.Name} draws {d.Count}",
        IntentChosen i => $"{i.Enemy.Name} intends {i.Intent.Name}" + (i.Intent.Big ? " (BIG)" : "") +
                          (i.Target is null ? "" : $" -> {i.Target.Name}") +
                          (i.Odds is { } o ? $" ({o})" : ""),
        CardPlayed p => $"{p.Actor.Name} plays {p.Card.Name}" + (p.Target is null ? "" : $" on {p.Target.Name}"),
        CheckRolled c => $"  {(c.IsReroll ? "REROLL " : "")}{c.Label}: d20 {c.Result.Kept} {Signed(c.Result.Modifier)} = {c.Result.Total} vs {c.Versus} {c.Result.Target}" +
                         (c.Parts is { Count: > 0 } ? $" [{string.Join(", ", c.Parts.Select(p => p.Text))}]" : "") + " -> " +
                         (c.Result.Critical ? "CRIT" : c.Result.Fumble ? "FUMBLE" : c.Result.Success ? "success" : "fail"),
        DamageRolled d => $"    {d.Dice}{(d.Critical ? " CRIT" : "")}: [{string.Join(" ", d.Faces)}] = {d.Raw}" +
                          (d.Notes.Count > 0 ? $" ({string.Join(", ", d.Notes)}) = {d.Total}" : ""),
        LuckOffered l => $"  {l.Hero.Name} may spend Luck to reroll ({l.LuckLeft} left)",
        LuckSpent l => $"  {l.Hero.Name} spends Luck ({l.LuckLeft} left)",
        TimingOffered t => $"  BIG attack! {t.Defender.Name} braces against {t.Attacker.Name}'s {t.Intent.Name}",
        TimingResolved t => $"  timing: {t.Grade}",
        RollModified m => $"  {m.By.Name} changes it: needs {m.Result.Target} -> " + (m.Result.Success ? "still hits" : "now misses"),
        DamageDealt d => $"  {d.Target.Name} takes {d.Amount} {d.Type}" +
                         (d.Blocked > 0 ? $" ({d.Blocked} blocked)" : "") + $" [{d.Target.Hp}/{d.Target.MaxHp}]",
        Healed h => $"  {h.Target.Name} heals {h.Amount} [{h.Target.Hp}/{h.Target.MaxHp}]",
        BlockGained b => $"  {b.Target.Name} gains {b.Amount} Block",
        ConditionApplied a => $"  {a.Target.Name} is {a.Condition} ({a.Rounds})",
        ConditionExpired x => $"  {x.Target.Name} is no longer {x.Condition}",
        DieFixed f => $"  next hero d20 is a {f.Value}",
        HeroDowned h => $"  {h.Hero.Name} is DOWNED",
        DeathSaveRolled s => $"  {s.Hero.Name} death save: {s.Roll} ({s.Successes} ok / {s.Failures} fail)",
        CombatantDied d => $"  {d.Combatant.Name} dies",
        ReactionOffered o => $"  {o.Hero.Name} may react: " + string.Join(", ", o.Options.Select(c => c.Def.Name)),
        ItemUsed u => $"{u.Hero.Name} uses {u.Item.Name}" + (u.Target is null || ReferenceEquals(u.Target, u.Hero) ? "" : $" on {u.Target.Name}"),
        PartyFled f => $"The party flees (-{f.TorchPaid} Torch, -{f.GoldPaid} gold)",
        CombatEnded end => $"=== {end.Outcome} (+{end.Gold} gold) ===",
        _ => e.ToString()
    };

    private static string Signed(int v) => v >= 0 ? $"+ {v}" : $"- {-v}";
}
