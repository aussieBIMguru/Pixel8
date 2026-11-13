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
        TimingOffered t => $"BIG ATTACK! {N(t.Defender)} BRACES",
        TimingResolved t => t.Grade switch
        {
            TimingGrade.Perfect => $"{N(t.Defender)} DODGES!",
            TimingGrade.Good => $"{N(t.Defender)} ROLLS WITH IT (1/2)",
            TimingGrade.Ok => $"{N(t.Defender)} FLINCHES (-25%)",
            _ => $"{N(t.Defender)} TAKES IT FULL"
        },
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
