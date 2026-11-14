using Pixel8.Core.Cards;
using Pixel8.Core.Content;
using Pixel8.Core.Dice;
using Pixel8.Core.Effects;
using Pixel8.Core.Heroes;
using Pixel8.Core.Randomness;

namespace Pixel8.Core.Combat;

public enum CombatKind { Normal, Elite, Boss }

public enum CombatPhase
{
    NotStarted,
    /// <summary>Rules are running (enemy turns). Never visible between public calls.</summary>
    Resolving,
    /// <summary>Waiting for the player: <see cref="CombatState.ActiveHero"/> plays cards, then EndTurn or Flee.</summary>
    HeroTurn,
    /// <summary>Waiting for the player: a hero may play a Reaction (<see cref="CombatState.Pending"/>).</summary>
    AwaitingReaction,
    /// <summary>Waiting for the player: a hero's d20 failed; spend 1 Luck to reroll? (<see cref="CombatState.PendingLuck"/>).</summary>
    AwaitingLuck,
    /// <summary>Waiting for the player: a big attack hit; play the timing mini-game (<see cref="CombatState.PendingTiming"/>).</summary>
    AwaitingTiming,
    Victory,
    Defeat,
    Fled
}

public enum TargetSide { None, Enemy, Ally }

/// <summary>How well the defender timed a big attack. Perfect = dodge, Good = half, Ok = −25%, Miss = full.</summary>
public enum TimingGrade { Miss, Ok, Good, Perfect }

/// <summary>Player setting: play the timing mini-game, turn it off (always Ok), or auto-succeed (always Perfect).</summary>
public enum TimingMode { On, Off, Auto }

/// <summary>
/// Timed-reaction rules. The rules never read the clock: the UI (or AutoPlayer) measures the press and
/// hands a <see cref="TimingGrade"/> to <see cref="CombatState.ResolveTiming"/>, so seeds still reproduce runs.
/// </summary>
public static class TimingRules
{
    /// <summary>Damage multiplier for a grade.</summary>
    public static double Multiplier(TimingGrade grade) => grade switch
    {
        TimingGrade.Perfect => 0,
        TimingGrade.Good => 0.5,
        TimingGrade.Ok => 0.75,
        _ => 1
    };

    /// <summary>
    /// Grades a press on a 0..1 bar. <paramref name="window"/> is the Good zone's full width (difficulty
    /// TimingWindow): Perfect is its middle third, Ok reaches twice as far either side, anything else is a Miss.
    /// </summary>
    public static TimingGrade Grade(double marker, double center, double window)
    {
        double d = Math.Abs(marker - center);
        double half = Math.Max(0.01, window) / 2;
        return d <= half / 3 ? TimingGrade.Perfect
            : d <= half ? TimingGrade.Good
            : d <= half * 2 ? TimingGrade.Ok
            : TimingGrade.Miss;
    }

    public static string Label(TimingGrade grade) => grade switch
    {
        TimingGrade.Perfect => "PERFECT",
        TimingGrade.Good => "GOOD",
        TimingGrade.Ok => "OK",
        _ => "MISS"
    };
}

/// <summary>What an enemy will do this round, with the odds the player sees.</summary>
public sealed record EnemyIntent(Enemy Enemy, IntentDef Intent, Hero? Target, CheckOdds? Odds);

/// <summary>A decision point: an enemy attack hit and the defender holds a Reaction that could matter.</summary>
public sealed record PendingReaction(
    Enemy Attacker, Hero Defender, IntentDef Intent, CheckResult Result, IReadOnlyList<CardInstance> Options);

/// <summary>A decision point: a hero's attack roll failed and the party has Luck to reroll it.</summary>
public sealed record PendingLuck(
    Hero Hero, CardDef Card, CardInstance? Instance, Combatant Target, CheckResult Result,
    RollMode Mode, IReadOnlyList<RollPart> Parts, string Label);

/// <summary>A decision point: a big attack hit; <see cref="Window"/> is the difficulty's Good-zone width.</summary>
/// <param name="Area">For an area attack (dragon breath): every hero it reached. One press grades the whole attack.</param>
public sealed record PendingTiming(Enemy Attacker, Hero Defender, IntentDef Intent, CheckResult Result, double Window,
    IReadOnlyList<AreaHit>? Area = null);

/// <summary>One hero caught by an area attack: Full damage (failed save / hit), Half (made save) or neither.</summary>
public sealed record AreaHit(Hero Hero, CheckResult Check, bool Full, bool Half);

/// <summary>Per-hero action economy: one Action, one Quick and one Reaction, refreshed at the start of the hero's turn.</summary>
public sealed class TurnEconomy
{
    public bool ActionUsed { get; set; }
    public bool QuickUsed { get; set; }
    public bool ReactionUsed { get; set; }

    public void Reset() => ActionUsed = QuickUsed = ReactionUsed = false;
}

/// <summary>
/// One fight, as a resumable state machine. The caller drives it:
/// <code>
/// var c = new CombatState(party, enemies, content, rng);
/// c.Start();
/// while (!c.IsOver)
///     if (c.Phase == CombatPhase.HeroTurn)        { c.PlayCard(...); ... c.EndTurn(); }
///     else if (c.Phase == CombatPhase.AwaitingReaction) c.ResolveReaction(cardOrNull);
///     else if (c.Phase == CombatPhase.AwaitingLuck)     c.ResolveLuck(spend);
///     else if (c.Phase == CombatPhase.AwaitingTiming)   c.ResolveTiming(grade);
/// </code>
/// Enemy turns run automatically inside those calls. Everything that happened is in <see cref="Events"/>.
///
/// MVP rules (locked 2026-11-13): no formation (any enemy can be targeted), flee from Normal fights only
/// for Torch + gold, Second Wind draws 1 card per hero at the start of a fight, heroes act before enemies
/// each round (balance.json heroesActFirst).
/// </summary>
public sealed class CombatState
{
    private readonly List<Enemy> _enemies;
    private readonly List<Combatant> _order = new();
    private readonly Dictionary<int, TurnEconomy> _economy = new();
    private readonly Dictionary<int, HashSet<int>> _hitThisRound = new();
    private readonly Dictionary<int, EnemyIntent> _intents = new();
    private readonly HashSet<(int HeroId, string CardId)> _usedOnce = new();
    /// <summary>Recharge intents that have been used and not yet recharged: (enemy id, intent name).</summary>
    private readonly HashSet<(int EnemyId, string Intent)> _uncharged = new();
    /// <summary>Poison damage per turn, by combatant id, while they are <see cref="Cond.Poisoned"/>.</summary>
    private readonly Dictionary<int, int> _poison = new();
    private readonly List<GameEvent> _events = new();
    private int _turnIndex = -1;

    // ---- round 11: new classes and deep-Depth monsters
    /// <summary>Most enemies on screen at once (summons stop there).</summary>
    public const int MaxEnemies = 4;
    private readonly List<Enemy> _fallen = new();
    private readonly HashSet<int> _regenTired = new();
    private readonly Dictionary<int, (int Count, DiceExpr Die)> _inspiration = new();
    private readonly Dictionary<int, (DiceExpr Dice, DiceExpr Undead)> _smite = new();
    private readonly Dictionary<int, WildForm> _forms = new();
    private readonly Dictionary<int, (Hero Hunter, DiceExpr Dice)> _hunted = new();
    private readonly Dictionary<int, int> _huntedKills = new();
    private readonly Dictionary<int, int> _layPool = new();
    private readonly Dictionary<(int HeroId, string CardId), int> _uses = new();
    private readonly HashSet<int> _regenBlocked = new();
    private readonly HashSet<int> _regrowBlocked = new();
    private readonly Dictionary<int, int> _heads = new();
    private readonly HashSet<int> _summoned = new();
    private readonly Dictionary<int, IntentDef> _legendary = new();
    private readonly HashSet<string> _feats = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>An enemy's extra end-of-round turn (legendary actions). Lives in the turn order, never shown.</summary>
    private sealed class LegendaryActor : Combatant
    {
        public LegendaryActor(Enemy owner) : base(owner.Name, 1, owner.Ac, 0) => Owner = owner;
        public Enemy Owner { get; }
        public override bool IsHero => false;
        public override bool IsAlive => Owner.IsAlive;
        public override bool CanAct => Owner.CanAct;
    }
    private CheckResult? _incoming;

    public CombatState(Party party, IEnumerable<Enemy> enemies, ContentDb content, Rng rng,
        CombatKind kind = CombatKind.Normal, DifficultyDef? difficulty = null)
    {
        Difficulty = difficulty ?? new DifficultyDef();
        FightLuck = Math.Max(0, Difficulty.FightLuck);
        Party = party;
        _enemies = enemies.ToList();
        Content = content;
        Rng = rng;
        Kind = kind;
        if (_enemies.Count == 0) throw new ArgumentException("A fight needs at least one enemy.", nameof(enemies));
        foreach (var h in party.Heroes) _economy[h.Id] = new TurnEconomy();
    }

    public Party Party { get; }
    public IReadOnlyList<Hero> Heroes => Party.Heroes;
    public IReadOnlyList<Enemy> Enemies => _enemies;
    /// <summary>Round 11: every enemy that took part, including fallen ones whose places summons took.</summary>
    public IEnumerable<Enemy> AllEnemies => _enemies.Concat(_fallen);
    public ContentDb Content { get; }
    public Rng Rng { get; }
    public CombatKind Kind { get; }
    public DifficultyDef Difficulty { get; }

    public CombatPhase Phase { get; private set; } = CombatPhase.NotStarted;
    public bool IsOver => Phase is CombatPhase.Victory or CombatPhase.Defeat or CombatPhase.Fled;
    public int Round { get; private set; }
    public Hero? ActiveHero { get; private set; }
    public PendingReaction? Pending { get; private set; }
    public PendingLuck? PendingLuck { get; private set; }
    public PendingTiming? PendingTiming { get; private set; }

    /// <summary>Timed reactions on big attacks (player setting). Off = always Ok, Auto = always Perfect.</summary>
    public TimingMode Timing { get; set; } = TimingMode.On;

    /// <summary>Offer a Luck reroll after a failed hero attack roll (when the party has Luck).</summary>
    public bool LuckPrompts { get; set; } = true;

    /// <summary>Luck rerolls spent in this fight (fight Luck and run Luck together).</summary>
    public int LuckSpentThisFight { get; private set; }

    /// <summary>
    /// Luck this fight started with, from the difficulty (Easy 2 / Normal 1 / Hard 0). Spent before the party's
    /// run Luck (<see cref="Party.Luck"/>, which only events add to). Round 8.
    /// </summary>
    public int FightLuck { get; set; }

    /// <summary>Rerolls left right now: fight Luck plus the party's run Luck.</summary>
    public int LuckLeft => FightLuck + Party.Luck;

    /// <summary>A failed hero attack can be rerolled now.</summary>
    public bool LuckAvailable => LuckLeft > 0;

    /// <summary>Debug "cannot die": damage never takes a hero below 1 HP.</summary>
    public bool Invulnerable { get; set; }

    /// <summary>Torch out: heroes attack with disadvantage (set by Run when the fight starts with Torch 0).</summary>
    public bool Dark { get; set; }

    /// <summary>The heroes ambushed the foes (an event's surprise fight): their attacks have advantage in round 1.</summary>
    public bool Surprise { get; set; }

    public bool SurpriseNow => Surprise && Round <= 1;

    /// <summary>+1 per balance.json proficiencyLevels entry the party has reached (levels 4 and 8).</summary>
    public int ProficiencyBonus => Content.Balance.ProficiencyAt(Party.Level);

    /// <summary>A hero's full attack modifier: class (weapon or spell) + proficiency + the difficulty's hero bonus.</summary>
    public int AttackModOf(Hero hero, bool spell) =>
        (spell ? hero.SpellAttackMod : hero.AttackMod) + ProficiencyBonus + Difficulty.HeroAttackBonus;

    /// <summary>A hero's spell save DC including proficiency.</summary>
    public int SpellDcOf(Hero hero) => hero.SpellDc + ProficiencyBonus;

    /// <summary>+1 hero weapon and spell damage per balance.json damageLevels entry reached (levels 2, 4, 6, 8).</summary>
    public int HeroDamageBonus => Content.Balance.DamageBonusAt(Party.Level);

    private readonly Dictionary<int, int> _missStreak = new();

    /// <summary>"FOCUS": +2 (focusPerMiss) to a hero's attack roll for each miss in a row; a hit resets it.</summary>
    public int FocusOf(Hero hero) => _missStreak.GetValueOrDefault(hero.Id) * Content.Balance.FocusPerMiss;

    /// <summary>What a missed attack still deals (not on a natural 1): the hero's damage bonus, plus level scaling.</summary>
    public int GrazeOf(Hero hero, bool spell) =>
        Math.Max(1, spell ? hero.SpellAttackMod - 2 : hero.Weapon.Bonus) + HeroDamageBonus;

    /// <summary>Party XP for this fight, set on Victory (enemy "xp" or balance xpByTier).</summary>
    public int XpEarned { get; private set; }
    public IReadOnlyList<Combatant> TurnOrder => _order.Where(c => c is not LegendaryActor).ToList();

    /// <summary>The Depth the fight is on (round 11: summoned enemies get that Depth's AC cap).</summary>
    public int Depth { get; set; }

    /// <summary>
    /// Round 11: class feats that happened in this fight, for achievements: fighter.last_stand, rogue.venom,
    /// wizard.triple, cleric.revive, bard.encore, ranger.marked3, druid.wild_win, paladin.smite_crit.
    /// </summary>
    public IReadOnlyCollection<string> Feats => _feats;
    public IReadOnlyList<GameEvent> Events => _events;
    public int GoldEarned { get; private set; }

    /// <summary>Set by FixNextDie (Loaded Die): the next d20 any hero rolls uses this natural value.</summary>
    public int? FixedNextDie { get; set; }

    public TurnEconomy EconomyOf(Hero hero) => _economy[hero.Id];

    // ================================================================== flow

    public void Start()
    {
        if (Phase != CombatPhase.NotStarted) throw new InvalidOperationException("Combat already started.");

        foreach (var e in _enemies.Where(e => e.Def.Heads > 0)) _heads[e.Id] = e.Def.Heads;
        foreach (var h in Heroes.Where(h => h.IsAlive))
        {
            h.Block = 0;
            h.TempHp = 0;
            h.Conditions.Clear();
            int drawn = h.Deck.Draw(Content.Balance.SecondWindDraw).Count;
            if (drawn > 0) Emit(new CardsDrawn(h, drawn));
        }
        // Round 10 gear: Luck for the fight (Stone of Good Luck). Crystal Orb Block comes on each hero's first turn.
        FightLuck += Heroes.Where(h => h.CanAct).Sum(h => h.FightLuckBonus);

        // Initiative: d20 + InitMod, ties to heroes, then a random tiebreak. Rolled into a list first so
        // each combatant rolls exactly once, in a fixed order. With HeroesActFirst (default) the whole party
        // goes before any enemy, and initiative only orders each side.
        var rolls = Heroes.Where(h => h.IsAlive).Cast<Combatant>().Concat(_enemies)
            .Select(c => (Combatant: c, Total: Rng.Roll(20) + c.InitMod + (c is Hero gh ? gh.InitBonus : 0), Tie: Rng.Next(1000)))
            .ToList();
        bool heroesFirst = Content.Balance.HeroesActFirst;
        _order.AddRange(rolls
            .OrderByDescending(r => heroesFirst && r.Combatant.IsHero)
            .ThenByDescending(r => r.Total)
            .ThenByDescending(r => r.Combatant.IsHero)
            .ThenBy(r => r.Tie)
            .Select(r => r.Combatant));
        // Round 11: enemies with legendary intents get an extra turn at the end of every round.
        foreach (var e in _enemies.Where(e => e.Def.Intents.Any(i => i.Legendary)).ToList())
            _order.Add(new LegendaryActor(e));
        Emit(new CombatStarted(TurnOrder));

        Phase = CombatPhase.Resolving;
        StartRound();
        Advance();
    }

    /// <summary>The active hero ends their turn; enemies act until the next decision point.</summary>
    public void EndTurn()
    {
        RequireHeroTurn();
        ActiveHero = null;
        Phase = CombatPhase.Resolving;
        Advance();
    }

    public bool CanFlee(out string reason)
    {
        int torch = Content.Balance.FleeTorchCost;
        reason = Phase != CombatPhase.HeroTurn ? "Only on a hero's turn."
            : Kind != CombatKind.Normal ? "You can't flee from an Elite or Boss."
            : Party.Torch < torch ? $"Fleeing costs {torch} Torch."
            : "";
        return reason.Length == 0;
    }

    /// <summary>Flee a Normal fight: pays the Torch cost and up to the gold cost. Enemies stay on the tile.</summary>
    public void Flee()
    {
        if (!CanFlee(out string reason)) throw new InvalidOperationException(reason);
        int torch = Content.Balance.FleeTorchCost;
        int gold = Math.Min(Party.Gold, Content.Balance.FleeGoldCost);
        Party.Torch -= torch;
        Party.Spend(gold);
        Emit(new PartyFled(torch, gold));
        Finish(CombatPhase.Fled);
    }

    private void StartRound()
    {
        Round++;
        _turnIndex = -1;
        _hitThisRound.Clear();
        Emit(new RoundStarted(Round));
        ChooseIntents();
    }

    private void Advance()
    {
        while (true)
        {
            if (CheckForEnd()) return;

            if (++_turnIndex >= _order.Count)
                StartRound();
            if (_turnIndex < 0) _turnIndex = 0;

            var actor = _order[_turnIndex];
            if (!actor.IsAlive) continue;

            if (actor is LegendaryActor legend)
            {
                if (Cond.SkipsTurn(legend.Owner.Conditions) || !_legendary.TryGetValue(legend.Owner.Id, out var extra)) continue;
                Emit(new LegendaryTurn(legend.Owner, extra));
                ExecuteIntent(legend.Owner, extra);
                if (Phase is CombatPhase.AwaitingReaction or CombatPhase.AwaitingTiming) return;
                continue;
            }

            bool wasDowned = actor is Hero { IsDowned: true };
            string? skip = BeginTurn(actor);
            // Poison can finish an enemy or knock a hero down at the start of their turn: they lose that turn.
            if (!actor.IsAlive || (!wasDowned && actor is Hero { IsDowned: true })) continue;
            if (skip is not null && actor.CanAct)
            {
                Emit(new TurnSkipped(actor, skip));
                continue;
            }

            if (actor is Hero hero)
            {
                if (hero.IsDowned)
                {
                    if (!hero.IsStable) RollDeathSave(hero);
                    continue;
                }
                ActiveHero = hero;
                Phase = CombatPhase.HeroTurn;
                return;
            }

            ExecuteIntent((Enemy)actor);
            if (Phase is CombatPhase.AwaitingReaction or CombatPhase.AwaitingTiming) return;
        }
    }

    /// <summary>Start-of-turn upkeep. Returns the condition that makes the actor lose this turn, if any (checked before durations tick).</summary>
    private string? BeginTurn(Combatant actor)
    {
        string? skip = actor.Conditions.Has(Cond.Paralyzed) ? Cond.Paralyzed
            : actor.Conditions.Has(Cond.Charmed) ? Cond.Charmed : null;
        Emit(new TurnStarted(actor));
        actor.Block = 0;
        // Poison bites before durations tick, so "rounds: 3" means three turns of damage.
        if (actor.Conditions.Has(Cond.Poisoned) && _poison.TryGetValue(actor.Id, out int poison))
            DealDamage(actor, actor, poison, "poison");
        foreach (string expired in actor.Conditions.TickStartOfTurn())
        {
            if (expired.Equals(Cond.Poisoned, StringComparison.OrdinalIgnoreCase)) _poison.Remove(actor.Id);
            Emit(new ConditionExpired(actor, expired));
        }
        if (actor is Hero h)
        {
            _economy[h.Id].Reset();
            // Round 10: Crystal Orb. Given at the start of the hero's first turn, after Block clears, so it lasts
            // through the enemies' first round.
            if (Round == 1 && h.StartBlock > 0 && h.CanAct) GainBlock(h, h.StartBlock);
            // Round 10: Ring of Regeneration.
            int regen = h.Loadout.Passives.Where(p => p.Type.Equals("regen", StringComparison.OrdinalIgnoreCase)).Sum(p => p.Amount);
            if (regen > 0 && h.CanAct && h.Hp < h.MaxHp) Heal(h, Math.Min(regen, h.MaxHp - h.Hp));
        }
        else if (actor is Enemy en && en.IsAlive)
        {
            // Round 12: regeneration tires. After regenTiresAfterRound, regen and regrowth stop for good (no endless fights).
            int tires = Content.Balance.RegenTiresAfterRound;
            if (tires > 0 && Round > tires && (en.Def.Regen > 0 || en.Def.Heads > 0))
            {
                if (_regenTired.Add(en.Id)) Emit(new RegenTired(en));
                _regenBlocked.Add(en.Id);
                _regrowBlocked.Add(en.Id);
            }
            // Round 11: Troll / Vampire regeneration, unless it took fire, acid or radiant since its last turn.
            if (en.Def.Regen > 0 && en.Hp < en.MaxHp && !_regenBlocked.Contains(en.Id))
                Heal(en, Math.Min(en.Def.Regen, en.MaxHp - en.Hp));
            _regenBlocked.Remove(en.Id);
            // Hydra: a lost head grows back each turn, unless the stump was burned.
            if (en.Def.Heads > 0 && _heads.TryGetValue(en.Id, out int heads) && heads < en.Def.Heads && !_regrowBlocked.Contains(en.Id))
            {
                _heads[en.Id] = heads + 1;
                Emit(new HeadsRegrew(en, heads + 1));
            }
            _regrowBlocked.Remove(en.Id);
        }
        return skip;
    }

    private bool CheckForEnd()
    {
        if (IsOver) return true;
        if (_enemies.All(e => !e.IsAlive))
        {
            GoldEarned = _enemies.Concat(_fallen).Sum(e => e.Def.Gold);
            XpEarned = _enemies.Concat(_fallen).Sum(e => Content.Balance.XpOf(e.Def));
            if (Heroes.Any(h => h.CanAct && _forms.ContainsKey(h.Id))) _feats.Add("druid.wild_win");
            Party.Gold += GoldEarned;
            foreach (var h in Heroes.Where(h => h.IsDowned && !h.IsDead))
                h.Revive(1);
            if (Difficulty.VictoryHealFraction > 0)
            {
                foreach (var h in Heroes.Where(h => h.CanAct))
                {
                    int amount = Math.Min(h.MaxHp - h.Hp, Math.Max(1, (int)Math.Round(h.MaxHp * Difficulty.VictoryHealFraction)));
                    if (amount > 0) Heal(h, amount);
                }
            }
            Finish(CombatPhase.Victory);
            return true;
        }
        if (Heroes.All(h => !h.CanAct))
        {
            Finish(CombatPhase.Defeat);
            return true;
        }
        return false;
    }

    private void Finish(CombatPhase outcome)
    {
        Phase = outcome;
        ActiveHero = null;
        Pending = null;
        PendingLuck = null;
        PendingTiming = null;
        foreach (var h in Heroes)
        {
            h.Block = 0;
            h.TempHp = 0;
            h.Conditions.Clear();
        }
        _forms.Clear();
        Emit(new CombatEnded(outcome, outcome == CombatPhase.Victory ? GoldEarned : 0));
    }

    // ================================================================== hero actions

    public bool CanPlay(CardInstance card)
    {
        if (Phase != CombatPhase.HeroTurn || ActiveHero is null) return false;
        if (!ActiveHero.Deck.Hand.Contains(card)) return false;
        return SlotFree(ActiveHero, card.Def.Speed);
    }

    /// <summary>Strike / Guard are free to use: the active hero's Action slot is free.</summary>
    public bool CanUseBasic() => Phase == CombatPhase.HeroTurn && ActiveHero is not null && !_economy[ActiveHero.Id].ActionUsed;

    /// <summary>The active-or-given hero's class basic action (heroes.json "basicAction"), if any.</summary>
    public CardDef? ClassBasic(Hero hero) =>
        hero.Class.BasicAction is { Length: > 0 } id && Content.Cards.TryGetValue(id, out var def) ? def : null;

    /// <summary>
    /// Any basic action: Strike and Guard for everyone, plus the hero's own class basic. Uses the card's speed slot;
    /// an Exhaust basic (Second Wind) works once per fight.
    /// </summary>
    public bool CanUseBasic(CardDef def)
    {
        if (Phase != CombatPhase.HeroTurn || ActiveHero is null) return false;
        if (!string.Equals(def.ClassId, "basic", StringComparison.OrdinalIgnoreCase)) return false;
        bool shared = def.Id.Equals(ContentDb.BasicStrikeId, StringComparison.OrdinalIgnoreCase)
                      || def.Id.Equals(ContentDb.BasicGuardId, StringComparison.OrdinalIgnoreCase);
        if (!shared && !string.Equals(ActiveHero.Class.BasicAction, def.Id, StringComparison.OrdinalIgnoreCase)) return false;
        if (def.Exhaust && _usedOnce.Contains((ActiveHero.Id, def.Id))) return false;
        if (BasicUsesLeft(ActiveHero, def) == 0) return false;
        return SlotFree(ActiveHero, def.Speed);
    }

    /// <summary>
    /// Round 11: how many more times this fight the hero can use a limited basic (Bardic Inspiration's uses, Lay on Hands'
    /// pool, an Exhaust basic's single use), or null when it has no limit.
    /// </summary>
    public int? BasicUsesLeft(Hero hero, CardDef def)
    {
        if (def.Effects.Any(e => e.Type.Equals("LayOnHands", StringComparison.OrdinalIgnoreCase))) return LayOnHandsLeft(hero);
        if (def.Uses > 0) return Math.Max(0, def.Uses - _uses.GetValueOrDefault((hero.Id, def.Id)));
        if (def.Exhaust) return _usedOnce.Contains((hero.Id, def.Id)) ? 0 : 1;
        return null;
    }

    public void PlayCard(CardInstance card, Combatant? target = null)
    {
        var hero = RequireHeroTurn();
        if (!hero.Deck.Hand.Contains(card)) throw new InvalidOperationException($"{card} is not in {hero.Name}'s hand.");
        if (card.Def.Speed == CardSpeed.Reaction) throw new InvalidOperationException("Reactions are played when triggered.");
        if (!SlotFree(hero, card.Def.Speed)) throw new InvalidOperationException($"{hero.Name} has already used their {card.Def.Speed}.");
        ValidateTarget(card.Def, target);

        Spend(hero, card.Def.Speed);
        hero.Deck.Play(card);
        Emit(new CardPlayed(hero, card.Def, target));
        ResolveCounted(hero, card.Def, card, target);
        CheckForEnd();
    }

    public void UseBasic(string basicCardId, Combatant? target = null)
    {
        var hero = RequireHeroTurn();
        var def = Content.Card(basicCardId);
        if (!string.Equals(def.ClassId, "basic", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"'{basicCardId}' is not a basic action.");
        if (!CanUseBasic(def)) throw new InvalidOperationException($"{hero.Name} can't use {def.Name} now.");
        ValidateTarget(def, target);

        Spend(hero, def.Speed);
        if (def.Exhaust) _usedOnce.Add((hero.Id, def.Id));
        if (def.Uses > 0) _uses[(hero.Id, def.Id)] = _uses.GetValueOrDefault((hero.Id, def.Id)) + 1;
        if (def.Id.Equals("basic.second_wind", StringComparison.OrdinalIgnoreCase) && hero.Hp <= 5) _feats.Add("fighter.last_stand");
        Emit(new CardPlayed(hero, def, target));
        ResolveCounted(hero, def, null, target);
        CheckForEnd();
    }

    /// <summary>Resolves a card and notes multi-kills (round 11: the Wizard's Chain Reaction feat).</summary>
    private void ResolveCounted(Hero hero, CardDef def, CardInstance? card, Combatant? target)
    {
        var before = _enemies.Where(e => e.IsAlive).ToList();
        Resolve(hero, def, card, target);
        if (hero.Class.Id.Equals("wizard", StringComparison.OrdinalIgnoreCase) && before.Count(e => !e.IsAlive) >= 3)
            _feats.Add("wizard.triple");
    }

    // ------------------------------------------------------------------ items

    /// <summary>A Bag item can be used as the active hero's Quick action.</summary>
    public bool CanUseItem(string itemId) =>
        Phase == CombatPhase.HeroTurn && ActiveHero is not null && !_economy[ActiveHero.Id].QuickUsed
        && Party.Bag.Contains(itemId) && Content.Items.ContainsKey(itemId);

    public TargetSide TargetSideFor(ItemDef item) => TargetSideForEffects(item.Effects);

    /// <summary>Uses (and removes) one Bag item as a Quick action. Healing items can target any living ally.</summary>
    public void UseItem(string itemId, Combatant? target = null)
    {
        var hero = RequireHeroTurn();
        if (!CanUseItem(itemId)) throw new InvalidOperationException($"{hero.Name} can't use '{itemId}' now.");
        var item = Content.Items[itemId];
        var side = TargetSideFor(item);
        ValidateSide(side, item.Name, target);
        if (side == TargetSide.None) target = hero;

        Spend(hero, CardSpeed.Quick);
        Party.Bag.Remove(itemId);
        Emit(new ItemUsed(hero, item, target));
        foreach (var e in item.Effects)
            RunEffect(e, hero, null, target, null, critical: false, checkBased: false);
        CheckForEnd();
    }

    /// <summary>Answer a <see cref="PendingReaction"/>: play one of the offered cards, or null to take the hit.</summary>
    public void ResolveReaction(CardInstance? card)
    {
        if (Phase != CombatPhase.AwaitingReaction || Pending is null)
            throw new InvalidOperationException("No reaction is pending.");

        var p = Pending;
        Pending = null;
        var result = p.Result;

        if (card is not null)
        {
            if (!p.Options.Contains(card)) throw new InvalidOperationException($"{card} is not a valid reaction here.");
            _economy[p.Defender.Id].ReactionUsed = true;
            p.Defender.Deck.Play(card);
            Emit(new CardPlayed(p.Defender, card.Def, p.Attacker));

            _incoming = result;
            foreach (var e in card.Def.Effects)
                RunEffect(e, p.Defender, card, p.Attacker, null, critical: false, checkBased: false);
            result = _incoming.Value;
            _incoming = null;
            Emit(new RollModified(p.Defender, result));
        }

        Phase = CombatPhase.Resolving;
        if (!TryOfferTiming(p.Attacker, p.Defender, p.Intent, result))
            FinishEnemyAttack(p.Attacker, p.Defender, p.Intent, result, null);
        if (Phase == CombatPhase.Resolving) Advance();
    }

    /// <summary>Answer a <see cref="PendingLuck"/>: true spends 1 Luck to reroll the d20, false keeps the miss.</summary>
    public void ResolveLuck(bool spend)
    {
        if (Phase != CombatPhase.AwaitingLuck || PendingLuck is null)
            throw new InvalidOperationException("No Luck reroll is pending.");

        var p = PendingLuck;
        PendingLuck = null;
        Phase = CombatPhase.HeroTurn;
        var result = p.Result;

        if (spend && LuckAvailable)
        {
            if (FightLuck > 0) FightLuck--;
            else Party.Luck--;
            LuckSpentThisFight++;
            Emit(new LuckSpent(p.Hero, LuckLeft));
            result = D20.Reroll(Rng, result, p.Mode);
            Emit(new CheckRolled(p.Hero, p.Target, result, p.Label, p.Parts, "AC", IsReroll: true));
        }

        FinishHeroAttack(p.Hero, p.Card, p.Instance, p.Target, result);
        CheckForEnd();
    }

    /// <summary>Answer a <see cref="PendingTiming"/> with how well the player timed it.</summary>
    public void ResolveTiming(TimingGrade grade)
    {
        if (Phase != CombatPhase.AwaitingTiming || PendingTiming is null)
            throw new InvalidOperationException("No timed reaction is pending.");

        var p = PendingTiming;
        PendingTiming = null;
        Phase = CombatPhase.Resolving;
        Emit(new TimingResolved(p.Defender, grade));
        if (p.Area is { } area) FinishArea(p.Attacker, p.Intent, area, grade);
        else FinishEnemyAttack(p.Attacker, p.Defender, p.Intent, p.Result, grade);
        Advance();
    }

    private Hero RequireHeroTurn()
    {
        if (Phase != CombatPhase.HeroTurn || ActiveHero is null)
            throw new InvalidOperationException($"Not a hero's turn (phase {Phase}).");
        return ActiveHero;
    }

    private bool SlotFree(Hero hero, CardSpeed speed)
    {
        var eco = _economy[hero.Id];
        return speed switch
        {
            CardSpeed.Action => !eco.ActionUsed,
            CardSpeed.Quick => !eco.QuickUsed,
            _ => false
        };
    }

    private void Spend(Hero hero, CardSpeed speed)
    {
        var eco = _economy[hero.Id];
        if (speed == CardSpeed.Action) eco.ActionUsed = true;
        else if (speed == CardSpeed.Quick) eco.QuickUsed = true;
    }

    // ================================================================== targeting

    /// <summary>Whether the card needs the player to pick an enemy, an ally, or nothing.</summary>
    public TargetSide TargetSideFor(CardDef def)
    {
        if (def.Check == CheckKind.Attack) return TargetSide.Enemy;
        if (def.Check == CheckKind.Save)
            return CheckTargetKey(def) == TargetKey.Target ? TargetSide.Enemy : TargetSide.None;
        return TargetSideForEffects(def.Effects);
    }

    private TargetSide TargetSideForEffects(IEnumerable<EffectDef> effects)
    {
        foreach (var e in effects)
        {
            var effect = Content.Effects.Get(e.Type);
            if (KeyOf(e, effect) == TargetKey.Target)
                return effect.TargetsAllies ? TargetSide.Ally : TargetSide.Enemy;
        }
        return TargetSide.None;
    }

    private void ValidateTarget(CardDef def, Combatant? target) => ValidateSide(TargetSideFor(def), def.Name, target);

    private static void ValidateSide(TargetSide side, string name, Combatant? target)
    {
        switch (side)
        {
            case TargetSide.Enemy when target is not Enemy { IsAlive: true }:
                throw new ArgumentException($"{name} needs a living enemy target.", nameof(target));
            case TargetSide.Ally when target is not Hero { IsAlive: true }:
                throw new ArgumentException($"{name} needs an ally target.", nameof(target));
        }
    }

    private static string KeyOf(EffectDef e, IEffect effect) =>
        e.Args is not null && e.Args.TryGetValue("target", out var k) ? k : effect.DefaultTarget;

    /// <summary>For Save cards: who rolls the save (from the first gated effect, e.g. Burning Hands → allEnemies).</summary>
    private string CheckTargetKey(CardDef def)
    {
        var e = def.Effects.FirstOrDefault(x => !x.When.Equals("always", StringComparison.OrdinalIgnoreCase))
                ?? def.Effects[0];
        return KeyOf(e, Content.Effects.Get(e.Type));
    }

    private IEnumerable<Combatant> OpponentsOf(Combatant c) =>
        c.IsHero ? _enemies.Where(e => e.IsAlive) : Heroes.Where(h => h.CanAct);

    private IEnumerable<Combatant> AlliesOf(Combatant c) =>
        c.IsHero ? Heroes.Where(h => h.IsAlive) : _enemies.Where(e => e.IsAlive);

    private List<Combatant> ResolveTargets(string key, Combatant source, Combatant? anchor, bool checkBased)
    {
        switch (key.ToLowerInvariant())
        {
            case "self":
            case "none":
                return new() { source };
            case "party":
                return AlliesOf(source).ToList();
            case "allenemies":
                return checkBased && anchor is not null ? new() { anchor } : OpponentsOf(source).ToList();
            case "random2":
            {
                // Each creature already rolled its own check: the effect lands on that one.
                if (checkBased && anchor is not null) return new() { anchor };
                var pool = OpponentsOf(source).ToList();
                Rng.Shuffle(pool);
                return pool.Take(2).ToList();
            }
            case "adjacent":
            {
                // No formation in the MVP: "adjacent" means another living enemy, picked at random.
                var others = OpponentsOf(source).Where(c => c != anchor).ToList();
                return others.Count == 0 ? new() : new() { Rng.Pick(others) };
            }
            default:
                return anchor is null ? new() : new() { anchor };
        }
    }

    // ================================================================== resolution

    private void Resolve(Hero hero, CardDef def, CardInstance? card, Combatant? selected)
    {
        switch (def.Check)
        {
            case CheckKind.Attack:
            {
                var target = selected!;
                var result = RollHeroAttack(hero, target, def.Name, SpellAttack(hero, def), out var mode, out var parts);
                if (!result.Success && LuckPrompts && LuckAvailable)
                {
                    PendingLuck = new PendingLuck(hero, def, card, target, result, mode, parts, def.Name);
                    Phase = CombatPhase.AwaitingLuck;
                    Emit(new LuckOffered(hero, result, LuckLeft));
                    return;
                }
                FinishHeroAttack(hero, def, card, target, result);
                break;
            }

            case CheckKind.Save:
            {
                foreach (var target in ResolveTargets(CheckTargetKey(def), hero, selected, checkBased: false))
                {
                    int saveMod = target is Enemy en ? en.SaveMod : 0;
                    // Round 11: a Restrained or Paralyzed creature can barely dodge (disadvantage on the DEX save).
                    var saveMode = D20.Combine(false, target.Conditions.Has(Cond.Restrained) || target.Conditions.Has(Cond.Paralyzed));
                    var save = D20.Roll(Rng, saveMod, SpellDcOf(hero), saveMode);
                    Emit(new CheckRolled(target, hero, save, $"{target.Name} saves vs {def.Name}",
                        new[] { new RollPart("DEX", saveMod) }, "DC"));
                    // The card "hits" when the save fails.
                    RunGated(def, hero, card, target, save, hit: !save.Success, crit: false);
                }
                break;
            }

            default:
                foreach (var e in def.Effects.Where(e => Gate(e.When, hit: true, crit: false, uncheckedCard: true)))
                    RunEffect(e, hero, card, selected, null, critical: false, checkBased: false);
                break;
        }
    }

    private void FinishHeroAttack(Hero hero, CardDef def, CardInstance? card, Combatant target, CheckResult result)
    {
        if (result.Success)
        {
            if (!_hitThisRound.TryGetValue(target.Id, out var set))
                _hitThisRound[target.Id] = set = new HashSet<int>();
            set.Add(hero.Id);
            _missStreak[hero.Id] = 0;
        }
        else
        {
            _missStreak[hero.Id] = _missStreak.GetValueOrDefault(hero.Id) + 1;
        }
        RunGated(def, hero, card, target, result, result.Success, result.Critical);
        if (result.Success && DealsDamageOnHit(def)) OnHeroAttackHit(hero, target, result.Critical);
        if (!result.Success && !result.Fumble && Content.Balance.GrazeOnMiss && DealsDamageOnHit(def))
            Graze(hero, target, SpellAttack(hero, def));
    }

    /// <summary>Round 11: a Druid in Wild Shape attacks with their spell attack bonus (the beast's bite or claws).</summary>
    private bool SpellAttack(Hero hero, CardDef def) => def.Spell || _forms.ContainsKey(hero.Id);

    /// <summary>Round 11: after a damaging attack hits: Hunter's Mark dice, then a primed Divine Smite.</summary>
    private void OnHeroAttackHit(Hero hero, Combatant target, bool critical)
    {
        if (target.IsAlive && _hunted.TryGetValue(target.Id, out var hunt) && ReferenceEquals(hunt.Hunter, hero))
        {
            int extra = RollDamage(hero, target, hunt.Dice, critical, type: "hunt");
            DealDamage(hero, target, extra, "hunt");
        }
        if (target.IsAlive && _smite.Remove(hero.Id, out var smite))
        {
            if (hero.Conditions.Remove(Cond.Smiting)) Emit(new ConditionExpired(hero, Cond.Smiting));
            Emit(new Smote(hero, target, critical));
            if (critical) _feats.Add("paladin.smite_crit");
            int amount = RollDamage(hero, target, smite.Dice, critical, type: "radiant");
            if (target is Enemy { Def.IsUndead: true })
                amount += RollDamage(hero, target, smite.Undead, critical, type: "radiant");
            DealDamage(hero, target, amount, "radiant");
        }
    }

    /// <summary>Attack cards whose hit deals damage graze on a miss; pure debuffs (Dirty Trick) don't.</summary>
    private static bool DealsDamageOnHit(CardDef def) =>
        def.Effects.Any(e => e.When.Equals("onHit", StringComparison.OrdinalIgnoreCase)
            && (e.Type.Equals("WeaponDamage", StringComparison.OrdinalIgnoreCase) || e.Type.Equals("Damage", StringComparison.OrdinalIgnoreCase)));

    /// <summary>A miss that still clips the target (round 8): no dice, just the hero's damage bonus.</summary>
    private void Graze(Hero hero, Combatant target, bool spell)
    {
        if (!target.IsAlive) return;
        int amount = GrazeOf(hero, spell);
        Emit(new DamageRolled(hero, target, new DiceExpr(0, 0, amount), Array.Empty<int>(), false, amount, new[] { "GRAZE" }, "graze"));
        DealDamage(hero, target, amount, "graze");
    }

    // ================================================================== rearm (round 8)

    /// <summary>How many cards the active hero may Rearm right now (difficulty's rearmCards, limited by discard and hand room).</summary>
    public int RearmLimit(Hero hero) =>
        Math.Max(0, Math.Min(Math.Max(1, Difficulty.RearmCards) + hero.RearmBonus, Math.Min(hero.Deck.Discard.Count, hero.Deck.MaxHand - hero.Deck.Hand.Count)));

    /// <summary>Rearm is a basic Action: the hero has their Action, cards in their discard and room in hand.</summary>
    public bool CanRearm(out string reason)
    {
        reason = "";
        if (Phase != CombatPhase.HeroTurn || ActiveHero is null) { reason = "Not your turn."; return false; }
        var hero = ActiveHero;
        if (_economy[hero.Id].ActionUsed) reason = "Action already used this turn.";
        else if (hero.Deck.Discard.Count == 0) reason = "Nothing in the discard pile.";
        else if (hero.Deck.Hand.Count >= hero.Deck.MaxHand) reason = "Your hand is full.";
        return reason.Length == 0;
    }

    /// <summary>Rearm: spend the Action to take up to <see cref="RearmLimit"/> chosen cards from the discard back into hand.</summary>
    public void Rearm(IReadOnlyList<CardInstance> picks)
    {
        var hero = RequireHeroTurn();
        if (!CanRearm(out string reason)) throw new InvalidOperationException(reason);
        int limit = RearmLimit(hero);
        if (picks.Count == 0 || picks.Count > limit) throw new ArgumentException($"Pick 1 to {limit} cards.", nameof(picks));
        if (picks.Distinct().Count() != picks.Count || picks.Any(c => !hero.Deck.Discard.Contains(c)))
            throw new ArgumentException("Pick cards from your discard pile.", nameof(picks));

        Spend(hero, CardSpeed.Action);
        foreach (var card in picks) hero.Deck.ReturnToHand(card);
        Emit(new Rearmed(hero, picks.Select(c => c.DisplayName).ToList()));
    }

    private void RunGated(CardDef def, Combatant source, CardInstance? card, Combatant anchor, CheckResult check, bool hit, bool crit)
    {
        foreach (var e in def.Effects)
            if (Gate(e.When, hit, crit, uncheckedCard: false))
                RunEffect(e, source, card, anchor, check, crit, checkBased: true);
    }

    private static bool Gate(string when, bool hit, bool crit, bool uncheckedCard) => when.ToLowerInvariant() switch
    {
        "always" => true,
        "onhit" => hit,
        "oncrit" => hit && crit,
        "onmiss" => !hit && !uncheckedCard,
        _ => false
    };

    private void RunEffect(EffectDef def, Combatant source, CardInstance? card, Combatant? anchor,
        CheckResult? check, bool critical, bool checkBased)
    {
        var effect = Content.Effects.Get(def.Type);
        foreach (var target in ResolveTargets(KeyOf(def, effect), source, anchor, checkBased))
        {
            effect.Apply(new EffectContext
            {
                Combat = this,
                Source = source,
                Target = target,
                Def = def,
                Card = card,
                Check = check,
                Critical = critical
            });
        }
    }

    private CheckResult RollHeroAttack(Hero hero, Combatant target, string label, bool spell,
        out RollMode mode, out IReadOnlyList<RollPart> parts)
    {
        bool marked = target.Conditions.Has(Cond.Marked);
        bool prone = target.Conditions.Has(Cond.Prone);
        bool cunning = hero.Conditions.Has(Cond.Cunning);
        bool held = target.Conditions.Has(Cond.Restrained) || target.Conditions.Has(Cond.Paralyzed) || target.Conditions.Has(Cond.Outlined);
        bool restrained = hero.Conditions.Has(Cond.Restrained);
        int critMin = marked ? 19 : 20;
        int focus = FocusOf(hero);
        int mod = AttackModOf(hero, spell) + focus;
        mode = D20.Combine(advantage: prone || cunning || held || SurpriseNow, disadvantage: Dark || restrained);
        int? fixedDie = TakeFixedDie();
        var result = D20.Roll(Rng, mod, target.Ac, mode, critMin, fixedDie);
        if (cunning && hero.Conditions.Remove(Cond.Cunning))
            Emit(new ConditionExpired(hero, Cond.Cunning));

        var list = new List<RollPart> { new(spell ? "SPELL" : "ATK", spell ? hero.SpellAttackMod : hero.AttackMod) };
        if (ProficiencyBonus != 0) list.Add(new RollPart("PROF", ProficiencyBonus));
        if (Difficulty.HeroAttackBonus != 0) list.Add(new RollPart(Difficulty.Name.ToUpperInvariant(), Difficulty.HeroAttackBonus));
        if (focus != 0) list.Add(new RollPart("FOCUS", focus));
        if (hero.Conditions.Has(Cond.Blessed))
        {
            int bless = Rng.Roll(4);
            result = result.WithExtraModifier(bless);
            list.Add(new RollPart("BLESS", bless));
        }
        result = TryInspire(hero, result, list);
        if (prone) list.Add(new RollPart("ADV PRONE", Tag: true));
        if (held) list.Add(new RollPart("ADV HELD", Tag: true));
        if (restrained) list.Add(new RollPart("DIS RESTRAINED", Tag: true));
        if (cunning) list.Add(new RollPart("ADV CUNNING", Tag: true));
        if (SurpriseNow) list.Add(new RollPart("ADV SURPRISE", Tag: true));
        if (Dark) list.Add(new RollPart("DIS DARK", Tag: true));
        if (marked) list.Add(new RollPart("CRIT 19-20", Tag: true));
        if (fixedDie is int f) list.Add(new RollPart($"LOADED {f}", Tag: true));
        parts = list;

        Emit(new CheckRolled(hero, target, result, label, parts));
        return result;
    }

    private int? TakeFixedDie()
    {
        int? f = FixedNextDie;
        FixedNextDie = null;
        return f;
    }

    /// <summary>
    /// Exact chance to hit, for the card UI ("HIT 11-20 · 50%"). Accounts for Marked, Prone, Blessed and a fixed die.
    /// With Bless the range shown assumes the minimum +1; the percentage is exact.
    /// </summary>
    public CheckOdds PreviewAttack(Hero hero, Combatant target, CardDef? card = null)
    {
        int critMin = target.Conditions.Has(Cond.Marked) ? 19 : 20;
        var mode = D20.Combine(
            advantage: target.Conditions.Has(Cond.Prone) || hero.Conditions.Has(Cond.Cunning) || SurpriseNow
                       || target.Conditions.Has(Cond.Restrained) || target.Conditions.Has(Cond.Paralyzed) || target.Conditions.Has(Cond.Outlined),
            disadvantage: Dark || hero.Conditions.Has(Cond.Restrained));
        bool blessed = hero.Conditions.Has(Cond.Blessed);
        int attackMod = AttackModOf(hero, card is { Spell: true } || _forms.ContainsKey(hero.Id)) + FocusOf(hero);

        double ChanceWith(int mod) => FixedNextDie is int f
            ? (D20.Evaluate(f, f, f, mod, target.Ac, critMin).Success ? 1.0 : 0.0)
            : D20.Odds(mod, target.Ac, mode, critMin).Chance;

        double chance = blessed
            ? Enumerable.Range(1, 4).Average(k => ChanceWith(attackMod + k))
            : ChanceWith(attackMod);
        return new CheckOdds(D20.Needed(attackMod + (blessed ? 1 : 0), target.Ac, critMin), mode, chance);
    }

    /// <summary>Chance that the enemy fails its save against this hero's spell DC (i.e. the spell lands fully).</summary>
    public double PreviewSaveFail(Hero hero, Enemy enemy) => 1 - D20.Odds(enemy.SaveMod, SpellDcOf(hero)).Chance;

    // ================================================================== enemies

    private void ChooseIntents()
    {
        _intents.Clear();
        // Recharge 5–6: each used ability rolls a d6 at the start of the round to come back.
        foreach (var (enemyId, name) in _uncharged.OrderBy(u => u.EnemyId).ThenBy(u => u.Intent, StringComparer.Ordinal).ToList())
        {
            var owner = _enemies.FirstOrDefault(e => e.Id == enemyId);
            var def = owner?.Def.Intents.FirstOrDefault(i => i.Name == name);
            if (owner is null || !owner.IsAlive || def is null) { _uncharged.Remove((enemyId, name)); continue; }
            if (Rng.Roll(6) >= def.Recharge)
            {
                _uncharged.Remove((enemyId, name));
                Emit(new IntentRecharged(owner, def));
            }
        }
        _legendary.Clear();
        foreach (var enemy in _enemies.Where(e => e.IsAlive).ToList())
            PlanIntent(enemy);
    }

    /// <summary>Picks an enemy's intent for the round (and its legendary extra, if it has any).</summary>
    private void PlanIntent(Enemy enemy)
    {
        var table = enemy.Def.Intents
            .Where(i => !i.Legendary && (i.Recharge <= 0 || !_uncharged.Contains((enemy.Id, i.Name))))
            .Select(i => (i, i.Weight)).ToList();
        if (table.Count == 0) table = enemy.Def.Intents.Where(i => !i.Legendary).Select(i => (i, i.Weight)).ToList();
        var intent = Rng.Weighted(table);
        Hero? target = intent.Kind == IntentKind.Attack && !intent.Area && !intent.PerHead ? ChooseHeroTarget() : null;
        CheckOdds? odds = target is null || intent.Save is not null ? null
            : D20.Odds(EnemyAttackBonus(intent), target.Ac, EnemyAttackMode(enemy, target));
        var planned = new EnemyIntent(enemy, intent, target, odds);
        _intents[enemy.Id] = planned;
        Emit(new IntentChosen(enemy, intent, target, odds));

        var legendary = enemy.Def.Intents.Where(i => i.Legendary).Select(i => (i, i.Weight)).ToList();
        if (legendary.Count > 0) _legendary[enemy.Id] = Rng.Weighted(legendary);
    }

    /// <summary>Round 11: the extra end-of-round intent an enemy with legendary actions will use (shown under its intent).</summary>
    public IntentDef? LegendaryIntentFor(Enemy enemy) => _legendary.TryGetValue(enemy.Id, out var i) ? i : null;

    /// <summary>Round 11: how many heads a many-headed enemy has right now (0 for everyone else).</summary>
    public int HeadsOf(Enemy enemy) => _heads.GetValueOrDefault(enemy.Id);

    /// <summary>Advantage against a Restrained or Paralyzed hero; disadvantage for a Restrained enemy.</summary>
    private static RollMode EnemyAttackMode(Enemy enemy, Hero target) =>
        D20.Combine(target.Conditions.Has(Cond.Restrained) || target.Conditions.Has(Cond.Paralyzed),
            enemy.Conditions.Has(Cond.Restrained));

    /// <summary>The intent as it stands right now (a Taunt played after intents were chosen redirects the attack).</summary>
    public EnemyIntent? IntentFor(Enemy enemy)
    {
        if (!_intents.TryGetValue(enemy.Id, out var planned)) return null;
        if (planned.Intent.Kind != IntentKind.Attack || planned.Intent.Area || planned.Intent.PerHead) return planned;
        var target = Taunter() ?? (planned.Target is { CanAct: true } t ? t : null);
        return planned with
        {
            Target = target,
            Odds = target is null || planned.Intent.Save is not null ? null
                : D20.Odds(EnemyAttackBonus(planned.Intent), target.Ac, EnemyAttackMode(enemy, target))
        };
    }

    public IEnumerable<EnemyIntent> CurrentIntents() =>
        _enemies.Where(e => e.IsAlive).Select(IntentFor).OfType<EnemyIntent>();

    private Hero? Taunter() => Heroes.FirstOrDefault(h => h.CanAct && h.Conditions.Has(Cond.Taunting));

    private Hero? ChooseHeroTarget()
    {
        var taunter = Taunter();
        if (taunter is not null) return taunter;
        var living = Heroes.Where(h => h.CanAct).ToList();
        return living.Count == 0 ? null : Rng.Pick(living);
    }

    /// <param name="extra">A legendary intent used as the enemy's extra turn (round 11); null = its planned intent.</param>
    private void ExecuteIntent(Enemy enemy, IntentDef? extra = null)
    {
        EnemyIntent? planned = extra is null && _intents.TryGetValue(enemy.Id, out var p) ? p : null;
        var intent = extra ?? planned?.Intent ?? enemy.Def.Intents.First(i => !i.Legendary);

        if (intent.Kind == IntentKind.Guard)
        {
            Emit(new CardPlayed(enemy, GuardCard(intent), null));
            GainBlock(enemy, intent.Block);
            return;
        }

        if (intent.Recharge > 0) _uncharged.Add((enemy.Id, intent.Name));
        if (intent.Area)
        {
            ExecuteArea(enemy, intent, Heroes.Where(h => h.CanAct).ToList());
            return;
        }
        if (intent.PerHead)
        {
            ExecuteHeads(enemy, intent);
            return;
        }

        var target = Taunter() ?? (planned?.Target is { CanAct: true } t ? t : ChooseHeroTarget());
        if (target is null) return;

        // Round 11: a single-target save (Basilisk's gaze): no attack roll.
        if (intent.Save is { Length: > 0 })
        {
            ExecuteArea(enemy, intent, new List<Hero> { target });
            return;
        }

        var mode = EnemyAttackMode(enemy, target);
        var result = D20.Roll(Rng, EnemyAttackBonus(intent), target.Ac, mode);
        var parts = EnemyAttackParts(intent, enemy, target);
        Emit(new CheckRolled(enemy, target, result, $"{enemy.Name} {intent.Name}", parts));

        if (result.Success && TryOfferReaction(enemy, target, intent, result))
            return;
        if (TryOfferTiming(enemy, target, intent, result))
            return;
        FinishEnemyAttack(enemy, target, intent, result, null);
    }

    /// <summary>
    /// After a big attack's hit roll (and any reaction): pause for the timing mini-game, or with
    /// <see cref="TimingMode.Off"/>/<see cref="TimingMode.Auto"/> grade it and deal the damage at once.
    /// Returns true when it took over the attack (paused, or already dealt the damage).
    /// </summary>
    private bool TryOfferTiming(Enemy attacker, Hero defender, IntentDef intent, CheckResult result)
    {
        if (!result.Success || !intent.Big || !defender.CanAct) return false;

        if (Timing != TimingMode.On)
        {
            var grade = Timing == TimingMode.Auto ? TimingGrade.Perfect : TimingGrade.Ok;
            Emit(new TimingResolved(defender, grade));
            FinishEnemyAttack(attacker, defender, intent, result, grade);
            return true;
        }

        PendingTiming = new PendingTiming(attacker, defender, intent, result, Difficulty.TimingWindow);
        Phase = CombatPhase.AwaitingTiming;
        Emit(new TimingOffered(attacker, defender, intent));
        return true;
    }

    private static CardDef GuardCard(IntentDef intent) =>
        new("intent." + intent.Name, intent.Name, "enemy", CardSpeed.Action, CheckKind.None, Rarity.Common,
            $"Gain {intent.Block} Block.", Array.Empty<EffectDef>());

    private bool TryOfferReaction(Enemy attacker, Hero defender, IntentDef intent, CheckResult result)
    {
        if (_economy[defender.Id].ReactionUsed || !defender.CanAct) return false;

        var options = defender.Deck.Hand
            .Where(c => c.Def.Speed == CardSpeed.Reaction && ReactionCouldMatter(c.Def, result))
            .ToList();
        if (options.Count == 0) return false;

        Pending = new PendingReaction(attacker, defender, intent, result, options);
        Phase = CombatPhase.AwaitingReaction;
        Emit(new ReactionOffered(defender, options));
        return true;
    }

    /// <summary>Only offer a roll-modifying reaction when it would actually turn the hit into a miss.</summary>
    private static bool ReactionCouldMatter(CardDef card, CheckResult result)
    {
        var modifiers = card.Effects.Where(e => e.Type.Equals("ModifyIncomingRoll", StringComparison.OrdinalIgnoreCase)).ToList();
        if (modifiers.Count == 0) return true;
        if (result.Critical) return false;
        int delta = modifiers.Sum(e => e.Args is not null && e.Args.TryGetValue("targetDelta", out var v) && int.TryParse(v, out int d) ? d : 0);
        return result.Total < result.Target + delta;
    }

    private int EnemyAttackBonus(IntentDef intent) => intent.AttackBonus + Difficulty.EnemyAttackBonus;

    private List<RollPart> EnemyAttackParts(IntentDef intent, Enemy enemy, Hero target)
    {
        var parts = new List<RollPart> { new("ATK", intent.AttackBonus) };
        if (Difficulty.EnemyAttackBonus != 0)
            parts.Add(new RollPart(Difficulty.Name.ToUpperInvariant(), Difficulty.EnemyAttackBonus));
        if (target.Conditions.Has(Cond.Restrained) || target.Conditions.Has(Cond.Paralyzed)) parts.Add(new RollPart("ADV HELD", Tag: true));
        if (enemy.Conditions.Has(Cond.Restrained)) parts.Add(new RollPart("DIS RESTRAINED", Tag: true));
        return parts;
    }

    /// <summary>
    /// Round 11: one attack per head (Hydra). The bites pick their targets one by one and resolve straight away
    /// (no reactions or timing), so a five-headed round stays quick.
    /// </summary>
    private void ExecuteHeads(Enemy enemy, IntentDef intent)
    {
        int heads = Math.Max(1, _heads.GetValueOrDefault(enemy.Id, 1));
        for (int i = 0; i < heads && enemy.IsAlive; i++)
        {
            var target = Taunter() ?? ChooseHeroTarget();
            if (target is null) return;
            var result = D20.Roll(Rng, EnemyAttackBonus(intent), target.Ac, EnemyAttackMode(enemy, target));
            Emit(new CheckRolled(enemy, target, result, $"{enemy.Name} {intent.Name} {i + 1}/{heads}", EnemyAttackParts(intent, enemy, target)));
            FinishEnemyAttack(enemy, target, intent, result, null);
        }
    }

    /// <summary>
    /// Round 11: a hero hit by an intent with a condition (Paralyzing Touch, charm) saves against it if the intent names
    /// a save, else simply gets it. Bless and Inspiration help the save.
    /// </summary>
    private void ApplyIntentCondition(Enemy attacker, Hero target, IntentDef intent)
    {
        if (intent.Condition is not { Length: > 0 } cond || !target.CanAct) return;
        if (intent.ConditionSave is { Length: > 0 } ability)
        {
            var save = RollHeroSave(target, attacker, ability, intent.ConditionDc, $"{target.Name} saves vs {intent.Name}");
            if (save.Success) return;
        }
        ApplyCondition(target, cond, Math.Max(1, intent.ConditionRounds));
    }

    /// <summary>A hero's saving throw against an enemy ability: the save modifier, Bless, then Inspiration. Logged on the roll strip.</summary>
    private CheckResult RollHeroSave(Hero hero, Enemy enemy, string ability, int dc, string label)
    {
        int mod = HeroSaveMod(hero, ability);
        var save = D20.Roll(Rng, mod, dc);
        var parts = new List<RollPart> { new(ability.ToUpperInvariant(), mod) };
        if (hero.Conditions.Has(Cond.Blessed))
        {
            int bless = Rng.Roll(4);
            save = save.WithExtraModifier(bless);
            parts.Add(new RollPart("BLESS", bless));
        }
        save = TryInspire(hero, save, parts);
        Emit(new CheckRolled(hero, enemy, save, label, parts, "DC"));
        return save;
    }

    /// <summary>
    /// Round 11: spends one of the hero's Inspiration dice when it could turn a failed roll into a success
    /// (never on a natural 1). Adds an INSPIRE part to the roll strip.
    /// </summary>
    private CheckResult TryInspire(Hero hero, CheckResult result, List<RollPart> parts)
    {
        if (result.Success || result.Fumble) return result;
        if (!_inspiration.TryGetValue(hero.Id, out var insp) || insp.Count <= 0) return result;
        if (result.Total + insp.Die.Count * insp.Die.Sides + insp.Die.Bonus < result.Target) return result;

        int roll = insp.Die.Roll(Rng);
        var after = result.WithExtraModifier(roll);
        if (insp.Count <= 1)
        {
            _inspiration.Remove(hero.Id);
            if (hero.Conditions.Remove(Cond.Inspired)) Emit(new ConditionExpired(hero, Cond.Inspired));
        }
        else _inspiration[hero.Id] = (insp.Count - 1, insp.Die);
        parts.Add(new RollPart("INSPIRE", roll));
        Emit(new InspirationUsed(hero, roll, after.Success));
        if (after.Success) _feats.Add("bard.encore");
        return after;
    }

    /// <param name="grade">Timing grade for a big attack; null for ordinary attacks (and when timing already applied).</param>
    private void FinishEnemyAttack(Enemy attacker, Hero target, IntentDef intent, CheckResult result, TimingGrade? grade)
    {
        if (!result.Success) return;

        var dice = DiceExpr.Parse(intent.Damage);
        int amount = dice.Roll(Rng, result.Critical, out int[] faces);
        var notes = new List<string>();
        if (intent.Extra is { Length: > 0 } extra)
        {
            int more = DiceExpr.Parse(extra).Roll(Rng, result.Critical);
            amount += more;
            notes.Add($"+{more} {intent.ExtraType.ToUpperInvariant()}");
        }
        amount = ScaleEnemyDamage(amount, intent, grade, notes);
        Emit(new DamageRolled(attacker, target, dice, faces, result.Critical, amount, notes, intent.Name));
        int dealt = DealDamage(attacker, target, amount, intent.Name);
        if (intent.Drain && dealt > 0 && attacker.IsAlive) Heal(attacker, Math.Min(dealt, attacker.MaxHp - attacker.Hp));
        if (intent.Poison > 0 && target.CanAct && grade != TimingGrade.Perfect)
            ApplyPoison(target, intent.Poison, intent.PoisonRounds);
        if (grade != TimingGrade.Perfect) ApplyIntentCondition(attacker, target, intent);
    }

    /// <summary>Game-side damage scaling for enemy attacks: the intent's damageScale, the difficulty, then the timing grade.</summary>
    private int ScaleEnemyDamage(int amount, IntentDef intent, TimingGrade? grade, List<string> notes)
    {
        if (amount > 0 && intent.DamageScale != 1)
        {
            amount = Math.Max(1, (int)Math.Round(amount * intent.DamageScale));
            notes.Add($"x{intent.DamageScale:0.##}");
        }
        // Round 12: the Depth's enemy damage multiplier, then the difficulty's.
        double depthMult = Content.Balance.EnemyDamageAt(Depth, Difficulty);
        if (amount > 0 && depthMult != 1)
        {
            amount = Math.Max(1, (int)Math.Round(amount * depthMult));
            notes.Add($"DEPTH x{depthMult:0.##}");
        }
        double mult = Difficulty.EnemyDamageMultiplier;
        if (amount > 0 && mult != 1)
        {
            amount = Math.Max(1, (int)Math.Round(amount * mult));
            notes.Add($"{Difficulty.Name.ToUpperInvariant()} x{mult:0.##}");
        }
        if (grade is TimingGrade g && g != TimingGrade.Miss && amount > 0)
        {
            amount = g == TimingGrade.Perfect ? 0 : Math.Max(1, (int)Math.Round(amount * TimingRules.Multiplier(g)));
            notes.Add(g == TimingGrade.Perfect ? "DODGED" : $"{TimingRules.Label(g)} x{TimingRules.Multiplier(g):0.##}");
        }
        return amount;
    }

    /// <summary>A hero's save modifier for an enemy ability: CON from heroes.json conSave, DEX from initiative (+2).</summary>
    public static int HeroSaveMod(Hero hero, string ability) => ability.ToUpperInvariant() switch
    {
        "CON" => hero.ConSave,
        "DEX" => hero.Class.InitMod + 2 + hero.SaveBonus,
        "WIS" => hero.Class.WisSave + hero.SaveBonus,
        _ => 0
    };

    /// <summary>
    /// An area attack (dragon breath): every standing hero saves for half (or is attacked, without a save).
    /// A big one pauses once for the timing mini-game, which grades the damage for the whole party.
    /// </summary>
    private void ExecuteArea(Enemy enemy, IntentDef intent, List<Hero> targets)
    {
        var hits = new List<AreaHit>();
        bool damaging = !IsZeroDice(intent.Damage);
        foreach (var hero in targets)
        {
            if (intent.Save is { Length: > 0 } ability)
            {
                var save = RollHeroSave(hero, enemy, ability, intent.SaveDc, $"{hero.Name} saves vs {intent.Name}");
                hits.Add(new AreaHit(hero, save, Full: !save.Success, Half: save.Success && damaging));
            }
            else
            {
                var attack = D20.Roll(Rng, EnemyAttackBonus(intent), hero.Ac, EnemyAttackMode(enemy, hero));
                var parts = EnemyAttackParts(intent, enemy, hero);
                Emit(new CheckRolled(enemy, hero, attack, $"{enemy.Name} {intent.Name}", parts));
                hits.Add(new AreaHit(hero, attack, Full: attack.Success, Half: false));
            }
        }

        var first = hits.FirstOrDefault(h => h.Full || h.Half);
        if (first is null) return;

        if (intent.Big && damaging && Timing == TimingMode.On)
        {
            PendingTiming = new PendingTiming(enemy, first.Hero, intent, first.Check, Difficulty.TimingWindow, hits);
            Phase = CombatPhase.AwaitingTiming;
            Emit(new TimingOffered(enemy, first.Hero, intent));
            return;
        }
        TimingGrade? grade = null;
        if (intent.Big && damaging)
        {
            grade = Timing == TimingMode.Auto ? TimingGrade.Perfect : TimingGrade.Ok;
            Emit(new TimingResolved(first.Hero, grade.Value));
        }
        FinishArea(enemy, intent, hits, grade);
    }

    /// <summary>One damage roll for the whole area; full on a failed save (or hit), half on a made save.</summary>
    private void FinishArea(Enemy attacker, IntentDef intent, IReadOnlyList<AreaHit> hits, TimingGrade? grade)
    {
        var first = hits.FirstOrDefault(h => h.Full || h.Half);
        if (first is null) return;
        int amount = 0;
        if (!IsZeroDice(intent.Damage))
        {
            var dice = DiceExpr.Parse(intent.Damage);
            amount = dice.Roll(Rng, false, out int[] faces);
            var notes = new List<string>();
            amount = ScaleEnemyDamage(amount, intent, grade, notes);
            if (hits.Any(h => h.Half)) notes.Add("HALF ON SAVE");
            Emit(new DamageRolled(attacker, first.Hero, dice, faces, false, amount, notes, intent.Name));
        }
        foreach (var h in hits)
        {
            if (!h.Full && !h.Half) continue;
            int dealt = amount > 0 ? DealDamage(attacker, h.Hero, h.Full ? amount : amount / 2, intent.Name) : 0;
            if (intent.Drain && dealt > 0 && attacker.IsAlive) Heal(attacker, Math.Min(dealt, attacker.MaxHp - attacker.Hp));
            if (h.Full && intent.Poison > 0 && h.Hero.CanAct && grade != TimingGrade.Perfect)
                ApplyPoison(h.Hero, intent.Poison, intent.PoisonRounds);
            // A failed save (or a hit) also brings the intent's condition (Basilisk gaze: Restrained), without a second save.
            if (h.Full && intent.Condition is { Length: > 0 } cond && h.Hero.CanAct && grade != TimingGrade.Perfect)
                ApplyCondition(h.Hero, cond, Math.Max(1, intent.ConditionRounds));
        }
    }

    private static bool IsZeroDice(string dice) => DiceExpr.Parse(dice) is { Count: 0, Bonus: <= 0 } || dice.Trim() == "0";

    /// <summary>
    /// Rolls damage dice for an effect and logs a <see cref="DamageRolled"/> for the roll strip.
    /// <paramref name="half"/> halves the roll (a made save). Returns the amount to deal.
    /// </summary>
    /// <param name="type">Damage type (weapon, fire, force, …), passed on for the screen's hit effects.</param>
    public int RollDamage(Combatant source, Combatant target, DiceExpr dice, bool critical, bool half = false, string type = "damage")
    {
        int amount = dice.Roll(Rng, critical, out int[] faces);
        var notes = new List<string>();
        if (half)
        {
            amount /= 2;
            notes.Add("HALF");
        }
        Emit(new DamageRolled(source, target, dice, faces, critical, amount, notes, type));
        return amount;
    }

    // ================================================================== state changes (called by effects)

    public int DealDamage(Combatant source, Combatant target, int amount, string type)
    {
        if (!target.IsAlive || amount <= 0) return 0;
        if (target is Hero { IsDowned: true }) return 0;

        int blocked = Math.Min(target.Block, amount);
        target.Block -= blocked;
        int rest = amount - blocked;
        // Round 11: temp HP (Wild Shape) soaks after Block.
        int temp = Math.Min(target.TempHp, rest);
        target.TempHp -= temp;
        int dealt = rest - temp;
        if (Invulnerable && target is Hero) dealt = Math.Min(dealt, Math.Max(0, target.Hp - 1));
        target.Hp = Math.Max(0, target.Hp - dealt);
        Emit(new DamageDealt(source, target, dealt, blocked + temp, type));
        if (temp > 0 && target.TempHp == 0 && target is Hero shaped && _forms.Remove(shaped.Id, out var form))
        {
            shaped.Conditions.Remove(Cond.Wild);
            Emit(new WildShapeEnded(shaped, form));
        }
        if (target is Enemy hurt) AfterEnemyDamaged(hurt, dealt, type);

        if (target.Hp == 0)
        {
            if (target is Hero h)
            {
                h.IsDowned = true;
                h.IsStable = false;
                h.DeathSaveSuccesses = 0;
                h.DeathSaveFailures = 0;
                h.Block = 0;
                h.TempHp = 0;
                h.Conditions.Clear();
                // Round 11: going down ends Wild Shape, Inspiration and a primed Smite.
                _forms.Remove(h.Id);
                _inspiration.Remove(h.Id);
                _smite.Remove(h.Id);
                Emit(new HeroDowned(h));
            }
            else
            {
                Emit(new CombatantDied(target));
                OnEnemyDied((Enemy)target);
            }
        }
        else if (target is Enemy living && living.Def.Summon is { } summon && !_summoned.Contains(living.Id)
                 && living.Hp <= living.MaxHp * summon.AtHpFraction)
        {
            SummonFor(living, summon);
        }
        return dealt;
    }

    /// <summary>Round 11: damage types that stop regeneration and regrowth, and the Hydra's severed heads.</summary>
    private void AfterEnemyDamaged(Enemy enemy, int dealt, string type)
    {
        var def = enemy.Def;
        if (def.RegenStoppedBy?.Contains(type, StringComparer.OrdinalIgnoreCase) == true) _regenBlocked.Add(enemy.Id);
        if (def.RegrowStoppedBy?.Contains(type, StringComparer.OrdinalIgnoreCase) == true) _regrowBlocked.Add(enemy.Id);
        if (def.Heads > 0 && def.SeverAt > 0 && dealt >= def.SeverAt && enemy.Hp > 0
            && _heads.TryGetValue(enemy.Id, out int heads) && heads > 1)
        {
            _heads[enemy.Id] = heads - 1;
            Emit(new HeadSevered(enemy, heads - 1));
        }
    }

    /// <summary>Round 11: Hunter's Mark moves to the toughest foe left when its target dies.</summary>
    private void OnEnemyDied(Enemy enemy)
    {
        if (!_hunted.Remove(enemy.Id, out var hunt)) return;
        int kills = _huntedKills.GetValueOrDefault(hunt.Hunter.Id) + 1;
        _huntedKills[hunt.Hunter.Id] = kills;
        if (kills >= 3) _feats.Add("ranger.marked3");
        var next = _enemies.Where(e => e.IsAlive && !_hunted.ContainsKey(e.Id)).OrderByDescending(e => e.Hp).FirstOrDefault();
        if (next is null || !hunt.Hunter.CanAct) return;
        _hunted[next.Id] = hunt;
        ApplyCondition(next, Cond.Hunted, 99);
    }

    /// <summary>Round 11: an enemy calls allies (Lich raising Zombies). Dead foes' places are reused; at most <see cref="MaxEnemies"/> stand.</summary>
    private void SummonFor(Enemy summoner, SummonDef summon)
    {
        _summoned.Add(summoner.Id);
        var spawned = new List<Enemy>();
        foreach (var id in summon.Enemies)
        {
            if (_enemies.Count(e => e.IsAlive) >= MaxEnemies || !Content.Enemies.TryGetValue(id, out var def)) break;
            int same = _enemies.Concat(_fallen).Count(e => e.Def.Id == def.Id);
            string name = same == 0 ? def.Name : $"{def.Name} {(char)('A' + same)}";
            int hp = (int)Math.Round(def.Hp * def.HpScale * Content.Balance.EnemyHpMultiplier * Content.Balance.EnemyHpAt(Depth, Difficulty)
                                     * Difficulty.EnemyHpMultiplier);
            var enemy = new Enemy(def, name, hp) { Ac = Math.Min(def.Ac, Content.Balance.AcCap(Depth)) };
            int slot = _enemies.FindIndex(e => !e.IsAlive);
            if (slot >= 0)
            {
                _fallen.Add(_enemies[slot]);
                _enemies[slot] = enemy;
            }
            else _enemies.Add(enemy);
            _order.Add(enemy);
            if (def.Heads > 0) _heads[enemy.Id] = def.Heads;
            spawned.Add(enemy);
        }
        if (spawned.Count == 0) return;
        Emit(new EnemiesSummoned(summoner, spawned, summon.Text));
        foreach (var e in spawned) PlanIntent(e);
    }

    // ================================================================== round 11: class features (called by effects)

    /// <summary>The weapon dice a hero's Strike uses: the beast's attack while in Wild Shape, else their weapon.</summary>
    public DiceExpr WeaponOf(Hero hero) => _forms.TryGetValue(hero.Id, out var form) ? form.Dice : hero.Weapon;

    /// <summary>The hero's Wild Shape form, or null.</summary>
    public WildForm? FormOf(Hero hero) => _forms.TryGetValue(hero.Id, out var form) ? form : null;

    /// <summary>Inspiration dice the hero holds (Bard).</summary>
    public int InspirationOf(Hero hero) => _inspiration.TryGetValue(hero.Id, out var i) ? i.Count : 0;

    /// <summary>Enemies killed this fight while marked by the hero's Hunter's Mark.</summary>
    public int HuntedKills(Hero hero) => _huntedKills.GetValueOrDefault(hero.Id);

    public bool IsSmiting(Hero hero) => _smite.ContainsKey(hero.Id);

    /// <summary>Gives a hero Inspiration dice (at most 3 held).</summary>
    public void Inspire(Hero hero, DiceExpr die, int count)
    {
        if (!hero.CanAct) return;
        int held = InspirationOf(hero);
        _inspiration[hero.Id] = (Math.Min(3, held + count), die);
        ApplyCondition(hero, Cond.Inspired, 99);
        Emit(new InspirationGained(hero, _inspiration[hero.Id].Count, die));
    }

    public void PrimeSmite(Hero hero, DiceExpr dice, DiceExpr undead)
    {
        if (!hero.CanAct) return;
        _smite[hero.Id] = (dice, undead);
        ApplyCondition(hero, Cond.Smiting, 99);
        Emit(new SmitePrimed(hero));
    }

    public void WildShape(Hero hero, WildForm form)
    {
        if (!hero.CanAct) return;
        _forms[hero.Id] = form;
        ApplyCondition(hero, Cond.Wild, 99);
        Emit(new WildShapeStarted(hero, form));
        GainTempHp(hero, form.TempHp);
    }

    /// <summary>Temporary HP don't stack: the bigger amount stays.</summary>
    public void GainTempHp(Combatant target, int amount)
    {
        if (!target.IsAlive || amount <= target.TempHp) return;
        int gain = amount - target.TempHp;
        target.TempHp = amount;
        Emit(new TempHpGained(target, gain));
    }

    /// <summary>Hunter's Mark: the hero's hits on the target deal extra dice. A Ranger marks one foe at a time.</summary>
    public void Hunt(Hero hero, Combatant target, DiceExpr dice)
    {
        if (target is not Enemy { IsAlive: true }) return;
        foreach (var old in _hunted.Where(kv => ReferenceEquals(kv.Value.Hunter, hero)).Select(kv => kv.Key).ToList())
        {
            _hunted.Remove(old);
            var e = _enemies.FirstOrDefault(x => x.Id == old);
            if (e is not null && e.Conditions.Remove(Cond.Hunted)) Emit(new ConditionExpired(e, Cond.Hunted));
        }
        _hunted[target.Id] = (hero, dice);
        ApplyCondition(target, Cond.Hunted, 99);
    }

    /// <summary>What's left in the hero's Lay on Hands pool this fight.</summary>
    public int LayOnHandsLeft(Hero hero) =>
        _layPool.TryGetValue(hero.Id, out int left) ? left
        : Content.Balance.LayOnHandsBase + Content.Balance.LayOnHandsPerLevel * Math.Max(0, Party.Level - 1);

    /// <summary>Heals the target from the pool, as much as they're missing (a Downed ally gets back up).</summary>
    public void LayOnHands(Hero paladin, Combatant target)
    {
        int pool = LayOnHandsLeft(paladin);
        int missing = target.MaxHp - (target is Hero { IsDowned: true } ? 0 : target.Hp);
        int amount = Math.Min(pool, missing);
        if (amount <= 0) return;
        _layPool[paladin.Id] = pool - amount;
        Heal(target, amount, paladin);
    }

    public void Heal(Combatant target, int amount, Combatant? source = null)
    {
        if (!target.IsAlive || amount <= 0) return;
        if (target is Hero { IsDowned: true } h)
        {
            if (source is Hero { Class.Id: var cls } && cls.Equals("cleric", StringComparison.OrdinalIgnoreCase)) _feats.Add("cleric.revive");
            h.Revive(amount);
        }
        else
            target.Hp = Math.Min(target.MaxHp, target.Hp + amount);
        Emit(new Healed(target, amount));
    }

    public void GainBlock(Combatant target, int amount)
    {
        if (amount <= 0 || !target.IsAlive) return;
        target.Block += amount;
        Emit(new BlockGained(target, amount));
    }

    /// <summary>Poisons a creature: <paramref name="amount"/> damage at the start of each of its next <paramref name="rounds"/> turns.</summary>
    public void ApplyPoison(Combatant target, int amount, int rounds)
    {
        if (!target.IsAlive || target is Hero { IsDowned: true }) return;
        _poison[target.Id] = target.Conditions.Has(Cond.Poisoned) ? Math.Max(amount, _poison.GetValueOrDefault(target.Id)) : amount;
        ApplyCondition(target, Cond.Poisoned, rounds);
        if (target is Enemy && _enemies.Count(e => e.IsAlive && e.Conditions.Has(Cond.Poisoned)) >= 3
            && Heroes.Any(h => h.CanAct && h.Class.Id.Equals("rogue", StringComparison.OrdinalIgnoreCase)))
            _feats.Add("rogue.venom");
    }

    /// <summary>
    /// Round 10: a hero's weapon passives after a weapon hit lands (WeaponDamage effect): Flame Tongue and friends add
    /// dice of their damage type, Vicious weapons add dice on a crit, Dagger of Venom poisons.
    /// </summary>
    public void ApplyWeaponPassives(Hero hero, Combatant target, bool critical)
    {
        var weapon = hero.Loadout[Pixel8.Core.Gear.GearSlot.Weapon]?.Def.Passive;
        if (weapon is null || !target.IsAlive) return;
        switch (weapon.Type.ToLowerInvariant())
        {
            case "onhit" when weapon.Dice is not null:
            {
                int extra = RollDamage(hero, target, DiceExpr.Parse(weapon.Dice), critical, type: weapon.DamageType);
                DealDamage(hero, target, extra, weapon.DamageType);
                break;
            }
            case "oncrit" when critical && weapon.Dice is not null:
            {
                int extra = RollDamage(hero, target, DiceExpr.Parse(weapon.Dice), false, type: weapon.DamageType);
                DealDamage(hero, target, extra, weapon.DamageType);
                break;
            }
            case "poisononhit":
                ApplyPoison(target, Math.Max(1, weapon.Amount), Math.Max(1, weapon.Rounds));
                break;
        }
    }

    /// <summary>Poison damage per turn on a creature (0 if it isn't Poisoned). For the screen.</summary>
    public int PoisonOn(Combatant c) => c.Conditions.Has(Cond.Poisoned) ? _poison.GetValueOrDefault(c.Id) : 0;

    /// <summary>Debug: every enemy drops dead and the fight ends in victory (rewards as normal).</summary>
    public void DebugWin()
    {
        if (IsOver) return;
        foreach (var e in _enemies.Where(e => e.IsAlive))
        {
            e.Hp = 0;
            Emit(new CombatantDied(e));
        }
        Pending = null;
        PendingLuck = null;
        PendingTiming = null;
        CheckForEnd();
    }

    /// <summary>Debug: every living hero back to full HP (Downed heroes get up).</summary>
    public void DebugHealAll()
    {
        foreach (var h in Heroes.Where(h => !h.IsDead))
            Heal(h, h.MaxHp);
    }

    /// <summary>A Draw effect: the hero draws up to <paramref name="count"/> cards (hand limit applies).</summary>
    public void DrawCards(Hero hero, int count)
    {
        int drawn = hero.Deck.Draw(count).Count;
        if (drawn > 0) Emit(new CardsDrawn(hero, drawn));
    }

    public void ApplyCondition(Combatant target, string condition, int rounds)
    {
        if (string.IsNullOrWhiteSpace(condition) || !target.IsAlive) return;
        target.Conditions.Add(condition, rounds);
        Emit(new ConditionApplied(target, condition, target.Conditions.RoundsLeft(condition)));
    }

    /// <summary>Used by ModifyIncomingRoll while a reaction resolves: raises what the incoming attack needed.</summary>
    public void AdjustIncomingTarget(int delta)
    {
        if (_incoming is not CheckResult r) return;
        _incoming = D20.Evaluate(r.Die1, r.Die2, r.Kept, r.Modifier, r.Target + delta, r.CritMin);
    }

    /// <summary>Evaluates a BonusDamage "if": alternatives separated by '|', any one true is enough.</summary>
    public bool EvaluateCondition(string expression, Combatant source, Combatant target)
    {
        foreach (string raw in expression.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (raw.Equals("allyHitThisRound", StringComparison.OrdinalIgnoreCase))
            {
                if (_hitThisRound.TryGetValue(target.Id, out var by) && by.Any(id => id != source.Id)) return true;
            }
            else if (raw.StartsWith("target.", StringComparison.OrdinalIgnoreCase))
            {
                string what = raw["target.".Length..];
                if (target.Conditions.Has(what)) return true;
                if (what.Equals("hurt", StringComparison.OrdinalIgnoreCase) && target.Hp < target.MaxHp) return true;
                // Round 11: creature types (target.undead).
                if (target is Enemy en && string.Equals(en.Def.Type, what, StringComparison.OrdinalIgnoreCase)) return true;
            }
            else if (raw.StartsWith("self.", StringComparison.OrdinalIgnoreCase))
            {
                if (source.Conditions.Has(raw["self.".Length..])) return true;
            }
        }
        return false;
    }

    private void RollDeathSave(Hero hero)
    {
        int roll = TakeFixedDie() ?? Rng.Roll(20);
        if (roll == 20)
        {
            hero.Revive(1);
            Emit(new DeathSaveRolled(hero, roll, 0, 0));
            Emit(new Healed(hero, 1));
            return;
        }

        if (roll == 1) hero.DeathSaveFailures += 2;
        else if (roll >= 10) hero.DeathSaveSuccesses++;
        else hero.DeathSaveFailures++;
        Emit(new DeathSaveRolled(hero, roll, hero.DeathSaveSuccesses, hero.DeathSaveFailures));

        if (hero.DeathSaveFailures >= 3)
        {
            hero.IsDead = true;
            hero.IsDowned = false;
            Emit(new CombatantDied(hero));
        }
        else if (hero.DeathSaveSuccesses >= 3)
        {
            hero.IsStable = true;
        }
    }

    private void Emit(GameEvent e) => _events.Add(e);
}
