using Pixel8.Core.Cards;
using Pixel8.Core.Content;
using Pixel8.Core.Heroes;

namespace Pixel8.Core.Combat;

/// <summary>
/// A simple greedy policy for the Sim and tests: play every playable card (heals only when someone is hurt),
/// Strike with any unused Action, always react. Good enough for balance smoke tests, not a real AI.
/// </summary>
public static class AutoPlayer
{
    /// <summary>
    /// Modelled player skill for timed reactions: relative weights for Miss / Ok / Good / Perfect.
    /// The default is an average player; the Sim compares it with all-Miss and all-Perfect.
    /// </summary>
    public static (int Miss, int Ok, int Good, int Perfect) TimingSkill { get; set; } = (15, 30, 35, 20);

    /// <summary>Spend Luck on every failed attack roll while the party has any.</summary>
    public static bool SpendLuck { get; set; } = true;

    public static TimingGrade RollTimingGrade(CombatState combat)
    {
        var s = TimingSkill;
        var table = new List<(TimingGrade, int)>
        {
            (TimingGrade.Miss, s.Miss), (TimingGrade.Ok, s.Ok), (TimingGrade.Good, s.Good), (TimingGrade.Perfect, s.Perfect)
        };
        table.RemoveAll(t => t.Item2 <= 0);
        return table.Count == 0 ? TimingGrade.Miss : combat.Rng.Weighted(table);
    }

    /// <summary>Plays a whole fight. Returns the outcome; throws if it doesn't end within <paramref name="maxDecisions"/>.</summary>
    public static CombatPhase RunToEnd(CombatState combat, int maxDecisions = 5000)
    {
        if (combat.Phase == CombatPhase.NotStarted) combat.Start();
        for (int i = 0; i < maxDecisions && !combat.IsOver; i++)
        {
            if (combat.Phase == CombatPhase.HeroTurn) PlayTurn(combat);
            else if (combat.Phase == CombatPhase.AwaitingReaction) combat.ResolveReaction(combat.Pending!.Options[0]);
            else if (combat.Phase == CombatPhase.AwaitingLuck) combat.ResolveLuck(SpendLuck);
            else if (combat.Phase == CombatPhase.AwaitingTiming) combat.ResolveTiming(RollTimingGrade(combat));
        }
        if (!combat.IsOver) throw new InvalidOperationException($"Fight did not end within {maxDecisions} decisions.");
        return combat.Phase;
    }

    public static void PlayTurn(CombatState combat)
    {
        var hero = combat.ActiveHero ?? throw new InvalidOperationException("No active hero.");

        UseQuickExtras(combat, hero);
        if (combat.Phase != CombatPhase.HeroTurn) return;

        // Out of Action cards? Spend the Action to Rearm the best ones from the discard (round 8).
        // Round 12: only Rearm when it brings back a damaging Action card (Rearm and pure-control cards alternating
        // forever was a Sim stalemate: Hypnotic Pattern / Entangle every other turn, no damage from anyone).
        if (!hero.Deck.Hand.Any(c => c.Def.Speed == CardSpeed.Action) && combat.CanRearm(out _))
        {
            int limit = combat.RearmLimit(hero);
            var picks = hero.Deck.Discard
                .OrderByDescending(c => c.Def.Speed == CardSpeed.Action && Damages(c.Def))
                .ThenByDescending(c => c.Def.Speed == CardSpeed.Action)
                .ThenByDescending(c => c.Def.Rarity)
                .ThenByDescending(c => c.Upgraded)
                .Take(limit)
                .ToList();
            if (picks.Count >= Math.Min(2, limit) && picks.Any(c => c.Def.Speed == CardSpeed.Action && Damages(c.Def)))
                combat.Rearm(picks);
        }

        bool played = true;
        while (played && combat.Phase == CombatPhase.HeroTurn)
        {
            played = false;
            foreach (var card in hero.Deck.Hand.ToList())
            {
                if (!combat.CanPlay(card)) continue;
                // Round 12: once a fight drags on, stop spending the Action on control-only cards and attack instead.
                if (combat.Round > StallRound && card.Def.Speed == CardSpeed.Action && IsControlOnly(card.Def)) continue;
                var side = combat.TargetSideFor(card.Def);
                var target = PickTarget(combat, side, card.Def);
                if (side != TargetSide.None && target is null) continue;
                combat.PlayCard(card, target);
                played = true;
                break;
            }
        }

        if (combat.Phase == CombatPhase.HeroTurn && combat.CanUseBasic())
        {
            // A class basic that is an Action attack (Fire Bolt) replaces Strike.
            var basic = combat.ClassBasic(hero);
            // A Druid in Wild Shape strikes with the beast instead.
            bool attackBasic = basic is { Speed: CardSpeed.Action, Check: CheckKind.Attack } && combat.CanUseBasic(basic)
                               && combat.FormOf(hero) is null;
            combat.UseBasic(attackBasic ? basic!.Id : ContentDb.BasicStrikeId, WeakestEnemy(combat));
        }

        if (combat.Phase == CombatPhase.HeroTurn)
            combat.EndTurn();
    }

    /// <summary>
    /// Quick-slot extras before cards: a potion on a badly hurt ally, then the class basic when it's useful
    /// (Second Wind when hurt, Cunning before attacking, Spare the Dying on a Downed ally).
    /// </summary>
    private static void UseQuickExtras(CombatState combat, Hero hero)
    {
        var hurt = combat.Heroes
            .Where(h => h.IsAlive && (h.IsDowned || h.Hp * 10 < h.MaxHp * 3))
            .OrderBy(h => h.IsDowned ? -1 : h.Hp)
            .FirstOrDefault();
        string? potion = combat.Party.Bag.FirstOrDefault(id => combat.Content.Items.TryGetValue(id, out var it)
            && it.Effects.Any(e => e.Type.Equals("Heal", StringComparison.OrdinalIgnoreCase)));
        if (hurt is not null && potion is not null && combat.CanUseItem(potion))
        {
            var side = combat.TargetSideFor(combat.Content.Items[potion]);
            combat.UseItem(potion, side == TargetSide.None ? null : hurt);
            return;
        }

        var basic = combat.ClassBasic(hero);
        if (basic is null || basic.Speed != CardSpeed.Quick || !combat.CanUseBasic(basic)) return;

        switch (basic.Id)
        {
            case "basic.second_wind" when hero.Hp * 2 < hero.MaxHp:
                combat.UseBasic(basic.Id);
                break;
            case "basic.cunning" when !hero.Deck.Hand.Any(c => c.Def.Speed == CardSpeed.Quick):
                combat.UseBasic(basic.Id);
                break;
            case "basic.spare_the_dying":
            {
                var target = combat.Heroes.FirstOrDefault(h => h.IsDowned && !h.IsDead)
                             ?? combat.Heroes.Where(h => h.CanAct).OrderBy(h => h.Hp).First();
                combat.UseBasic(basic.Id, target);
                break;
            }
            // Round 11
            case "basic.bardic_inspiration":
            {
                var target = combat.Heroes.Where(h => h.CanAct && combat.InspirationOf(h) == 0)
                    .OrderByDescending(h => h.AttackMod).FirstOrDefault();
                if (target is not null) combat.UseBasic(basic.Id, target);
                break;
            }
            case "basic.hunters_mark":
            {
                var target = combat.Enemies.Where(e => e.IsAlive).OrderByDescending(e => e.Hp).FirstOrDefault();
                if (target is not null) combat.UseBasic(basic.Id, target);
                break;
            }
            case "basic.lay_on_hands":
            {
                var target = combat.Heroes.Where(h => h.IsAlive && (h.IsDowned || h.Hp * 2 < h.MaxHp))
                    .OrderBy(h => h.IsDowned ? -1 : h.Hp).FirstOrDefault();
                if (target is not null) combat.UseBasic(basic.Id, target);
                break;
            }
        }
    }

    /// <summary>Sim/test policy for level-ups: always take +HP.</summary>
    public static void ResolveLevelUps(Pixel8.Core.Runs.Run run)
    {
        while (run.PendingLevelUps.Count > 0)
            run.ApplyLevelUp(run.PendingLevelUps[0], Pixel8.Core.Runs.LevelUpChoice.MaxHp);
    }

    private static Combatant? PickTarget(CombatState combat, TargetSide side, CardDef? card = null)
    {
        // Round 11 buffs (Inspiration, temp HP) go to anyone standing; everything else keeps the old rule (only the hurt).
        bool heals = card is null || !card.Effects.Any(e => e.Type is "Inspire" or "TempHp");
        return side switch
        {
            TargetSide.Enemy => WeakestEnemy(combat),
            // Heals and Block go to whoever is hurt (none if nobody is); Inspiration and temp HP to the most hurt standing hero.
            TargetSide.Ally when heals => combat.Heroes
                .Where(h => h.IsAlive && h.Hp < h.MaxHp)
                .OrderBy(h => (double)h.Hp / h.MaxHp)
                .FirstOrDefault(),
            TargetSide.Ally => combat.Heroes
                .Where(h => h.CanAct)
                .OrderBy(h => (double)h.Hp / h.MaxHp)
                .FirstOrDefault(),
            _ => null
        };
    }

    /// <summary>Round 12: after this round the AutoPlayer stops playing control-only Action cards.</summary>
    public const int StallRound = 6;

    private static readonly HashSet<string> DamageEffects = new(StringComparer.OrdinalIgnoreCase)
        { "Damage", "WeaponDamage", "BonusDamage", "Poison", "Smite" };

    public static bool Damages(CardDef def) => def.Effects.Any(e => DamageEffects.Contains(e.Type));

    /// <summary>Applies conditions (Charmed, Restrained…) but deals no damage, heals or protects nobody.</summary>
    public static bool IsControlOnly(CardDef def) =>
        def.Effects.Any(e => e.Type.Equals("Apply", StringComparison.OrdinalIgnoreCase)) && !Damages(def)
        && !def.Effects.Any(e => e.Type is "Heal" or "Block" or "TempHp" or "Stabilize" or "LayOnHands");

    private static Enemy? WeakestEnemy(CombatState combat) =>
        combat.Enemies.Where(e => e.IsAlive).OrderBy(e => e.Hp).FirstOrDefault();
}
