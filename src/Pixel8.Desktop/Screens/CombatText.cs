using Pixel8.Core.Combat;

namespace Pixel8.Desktop.Screens;

/// <summary>Short, upper-case log lines for the combat screen. Null = not worth a log line.</summary>
public static class CombatText
{
    public static string? Line(GameEvent e) => e switch
    {
        RoundStarted r => $"-- ROUND {r.Round} --",
        CardPlayed p when p.Target is null || ReferenceEquals(p.Target, p.Actor) => $"{N(p.Actor)}: {U(p.Card.Name)}",
        CardPlayed p => $"{N(p.Actor)}: {U(p.Card.Name)} > {N(p.Target!)}",
        CheckRolled c => Check(c),
        RollModified m => m.Result.Success ? "STILL HITS!" : "NOW IT MISSES!",
        DamageDealt d when d.Blocked > 0 => $"{N(d.Target)} -{d.Amount} ({d.Blocked} BLOCKED)",
        DamageDealt d => $"{N(d.Target)} -{d.Amount}",
        Healed h => $"{N(h.Target)} +{h.Amount} HP",
        BlockGained b => $"{N(b.Target)} +{b.Amount} BLOCK",
        ConditionApplied a => $"{N(a.Target)} IS {U(a.Condition)}",
        HeroDowned h => $"{N(h.Hero)} IS DOWN!",
        DeathSaveRolled s => $"{N(s.Hero)} DEATH SAVE {s.Roll} ({s.Successes} OK {s.Failures} X)",
        CombatantDied d => $"{N(d.Combatant)} DIES",
        PartyFled => "THE PARTY FLEES!",
        ItemUsed u => u.Target is null || ReferenceEquals(u.Target, u.Hero)
            ? $"{N(u.Hero)}: {U(u.Item.Name)}"
            : $"{N(u.Hero)}: {U(u.Item.Name)} > {N(u.Target)}",
        LuckSpent l => $"LUCK! REROLL ({l.LuckLeft} LEFT)",
        Rearmed r => $"{N(r.Hero)} REARMS {r.Cards.Count}",
        TimingOffered t when t.Intent.Area => $"{U(t.Intent.Name)}! THE PARTY BRACES",
        TimingOffered t => $"BIG ATTACK! {N(t.Defender)} BRACES",
        IntentRecharged r => $"{N(r.Enemy)}: {U(r.Intent.Name)} IS READY!",
        TimingResolved t => t.Grade switch
        {
            TimingGrade.Perfect => $"{N(t.Defender)} DODGES!",
            TimingGrade.Good => $"{N(t.Defender)} ROLLS WITH IT (1/2)",
            TimingGrade.Ok => $"{N(t.Defender)} FLINCHES (-25%)",
            _ => $"{N(t.Defender)} TAKES IT FULL"
        },
        // Round 11
        TurnSkipped t => $"{N(t.Actor)} IS {U(t.Condition)}: TURN LOST",
        InspirationGained i => $"{N(i.Hero)} HAS {i.Dice} INSPIRATION",
        InspirationUsed i => i.Turned ? $"INSPIRATION +{i.Roll}: NOW IT WORKS!" : $"INSPIRATION +{i.Roll}: NOT ENOUGH",
        SmitePrimed sp => $"{N(sp.Hero)}'S BLADE GLOWS",
        Smote sm => sm.Critical ? $"CRITICAL SMITE ON {N(sm.Target)}!" : $"{N(sm.Hero)} SMITES {N(sm.Target)}",
        WildShapeStarted w => $"{N(w.Hero)} BECOMES A {U(w.Form.Name)}!",
        WildShapeEnded w => $"{N(w.Hero)} RETURNS TO FORM",
        TempHpGained t => $"{N(t.Target)} +{t.Amount} TEMP HP",
        HeadSevered h => $"A HEAD IS SEVERED! {h.HeadsLeft} LEFT",
        HeadsRegrew h => $"THE HEAD GROWS BACK! {h.Heads} HEADS",
        RegenTired r => $"{N(r.Enemy)}'S WOUNDS STOP CLOSING",
        EnemiesSummoned es => es.Text is { Length: > 0 } text ? U(text) : $"{N(es.Summoner)} CALLS FOR HELP!",
        LegendaryTurn l => $"{N(l.Enemy)} ACTS AGAIN: {U(l.Intent.Name)}",
        _ => null
    };

    private static string Check(CheckRolled c)
    {
        var r = c.Result;
        string mod = r.Modifier >= 0 ? $"+{r.Modifier}" : r.Modifier.ToString();
        string roll = $"{r.Kept}{mod}={r.Total} VS {r.Target}";

        if (c.Versus == "DC")
            return $"{N(c.Roller)} SAVES {roll} " + (r.Success ? "OK" : "FAIL");

        string verdict = r.Critical ? "CRIT!" : r.Fumble ? "FUMBLE" : r.Success ? "HIT" : "MISS";
        // Enemy attacks have no CardPlayed line, so say who swings at whom first.
        string prefix = c.Roller is Enemy ? $"{U(c.Label)} > {N(c.Against)}\n" : "";
        return $"{prefix}{(c.IsReroll ? " REROLL" : "")} D20 {roll} {verdict}";
    }

    private static string N(Combatant c) => c.Name.ToUpperInvariant();
    private static string U(string s) => s.ToUpperInvariant();
}
