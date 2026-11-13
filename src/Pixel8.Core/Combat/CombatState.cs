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
public sealed record PendingTiming(Enemy Attacker, Hero Defender, IntentDef Intent, CheckResult Result, double Window);

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
    private readonly List<GameEvent> _events = new();
    private int _turnIndex = -1;
    private CheckResult? _incoming;

    public CombatState(Party party, IEnumerable<Enemy> enemies, ContentDb content, Rng rng,
        CombatKind kind = CombatKind.Normal, DifficultyDef? difficulty = null)
    {
        Difficulty = difficulty ?? new DifficultyDef();
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

    /// <summary>Luck rerolls spent in this fight; capped by balance.json luckRerollsPerFight (1).</summary>
    public int LuckSpentThisFight { get; private set; }

    /// <summary>A failed hero attack can be rerolled now: the party has Luck and hasn't used this fight's reroll.</summary>
    public bool LuckAvailable => Party.Luck > 0 && LuckSpentThisFight < Content.Balance.LuckRerollsPerFight;

    /// <summary>Torch out: heroes attack with disadvantage (set by Run when the fight starts with Torch 0).</summary>
    public bool Dark { get; set; }

    /// <summary>Party XP for this fight, set on Victory (enemy "xp" or balance xpByTier).</summary>
    public int XpEarned { get; private set; }
    public IReadOnlyList<Combatant> TurnOrder => _order;
    public IReadOnlyList<GameEvent> Events => _events;
    public int GoldEarned { get; private set; }

    /// <summary>Set by FixNextDie (Loaded Die): the next d20 any hero rolls uses this natural value.</summary>
    public int? FixedNextDie { get; set; }

    public TurnEconomy EconomyOf(Hero hero) => _economy[hero.Id];

    // ================================================================== flow

    public void Start()
    {
        if (Phase != CombatPhase.NotStarted) throw new InvalidOperationException("Combat already started.");

        foreach (var h in Heroes.Where(h => h.IsAlive))
        {
            h.Block = 0;
            h.Conditions.Clear();
            int drawn = h.Deck.Draw(Content.Balance.SecondWindDraw).Count;
            if (drawn > 0) Emit(new CardsDrawn(h, drawn));
        }

        // Initiative: d20 + InitMod, ties to heroes, then a random tiebreak. Rolled into a list first so
        // each combatant rolls exactly once, in a fixed order. With HeroesActFirst (default) the whole party
        // goes before any enemy, and initiative only orders each side.
        var rolls = Heroes.Where(h => h.IsAlive).Cast<Combatant>().Concat(_enemies)
            .Select(c => (Combatant: c, Total: Rng.Roll(20) + c.InitMod, Tie: Rng.Next(1000)))
            .ToList();
        bool heroesFirst = Content.Balance.HeroesActFirst;
        _order.AddRange(rolls
            .OrderByDescending(r => heroesFirst && r.Combatant.IsHero)
            .ThenByDescending(r => r.Total)
            .ThenByDescending(r => r.Combatant.IsHero)
            .ThenBy(r => r.Tie)
            .Select(r => r.Combatant));
        Emit(new CombatStarted(_order.ToList()));

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
        Party.Gold -= gold;
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

            BeginTurn(actor);

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

    private void BeginTurn(Combatant actor)
    {
        Emit(new TurnStarted(actor));
        actor.Block = 0;
        foreach (string expired in actor.Conditions.TickStartOfTurn())
            Emit(new ConditionExpired(actor, expired));
        if (actor is Hero h) _economy[h.Id].Reset();
    }

    private bool CheckForEnd()
    {
        if (IsOver) return true;
        if (_enemies.All(e => !e.IsAlive))
        {
            GoldEarned = _enemies.Sum(e => e.Def.Gold);
            XpEarned = _enemies.Sum(e => Content.Balance.XpOf(e.Def));
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
            h.Conditions.Clear();
        }
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
        return SlotFree(ActiveHero, def.Speed);
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
        Resolve(hero, card.Def, card, target);
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
        Emit(new CardPlayed(hero, def, target));
        Resolve(hero, def, null, target);
        CheckForEnd();
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
            Party.Luck--;
            LuckSpentThisFight++;
            Emit(new LuckSpent(p.Hero, Party.Luck));
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
        FinishEnemyAttack(p.Attacker, p.Defender, p.Intent, p.Result, grade);
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
                var result = RollHeroAttack(hero, target, def.Name, def.Spell, out var mode, out var parts);
                if (!result.Success && LuckPrompts && LuckAvailable)
                {
                    PendingLuck = new PendingLuck(hero, def, card, target, result, mode, parts, def.Name);
                    Phase = CombatPhase.AwaitingLuck;
                    Emit(new LuckOffered(hero, result, Party.Luck));
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
                    var save = D20.Roll(Rng, saveMod, hero.SpellDc);
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
        }
        RunGated(def, hero, card, target, result, result.Success, result.Critical);
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
        int critMin = marked ? 19 : 20;
        int mod = spell ? hero.SpellAttackMod : hero.AttackMod;
        mode = D20.Combine(advantage: prone || cunning, disadvantage: Dark);
        int? fixedDie = TakeFixedDie();
        var result = D20.Roll(Rng, mod, target.Ac, mode, critMin, fixedDie);
        if (cunning && hero.Conditions.Remove(Cond.Cunning))
            Emit(new ConditionExpired(hero, Cond.Cunning));

        var list = new List<RollPart> { new(spell ? "SPELL" : "ATK", mod) };
        if (hero.Conditions.Has(Cond.Blessed))
        {
            int bless = Rng.Roll(4);
            result = result.WithExtraModifier(bless);
            list.Add(new RollPart("BLESS", bless));
        }
        if (prone) list.Add(new RollPart("ADV PRONE", Tag: true));
        if (cunning) list.Add(new RollPart("ADV CUNNING", Tag: true));
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
            advantage: target.Conditions.Has(Cond.Prone) || hero.Conditions.Has(Cond.Cunning),
            disadvantage: Dark);
        bool blessed = hero.Conditions.Has(Cond.Blessed);
        int attackMod = card is { Spell: true } ? hero.SpellAttackMod : hero.AttackMod;

        double ChanceWith(int mod) => FixedNextDie is int f
            ? (D20.Evaluate(f, f, f, mod, target.Ac, critMin).Success ? 1.0 : 0.0)
            : D20.Odds(mod, target.Ac, mode, critMin).Chance;

        double chance = blessed
            ? Enumerable.Range(1, 4).Average(k => ChanceWith(attackMod + k))
            : ChanceWith(attackMod);
        return new CheckOdds(D20.Needed(attackMod + (blessed ? 1 : 0), target.Ac, critMin), mode, chance);
    }

    /// <summary>Chance that the enemy fails its save against this hero's spell DC (i.e. the spell lands fully).</summary>
    public double PreviewSaveFail(Hero hero, Enemy enemy) => 1 - D20.Odds(enemy.SaveMod, hero.SpellDc).Chance;

    // ================================================================== enemies

    private void ChooseIntents()
    {
        _intents.Clear();
        foreach (var enemy in _enemies.Where(e => e.IsAlive))
        {
            var table = enemy.Def.Intents.Select(i => (i, i.Weight)).ToList();
            var intent = Rng.Weighted(table);
            Hero? target = intent.Kind == IntentKind.Attack ? ChooseHeroTarget() : null;
            CheckOdds? odds = target is null ? null : D20.Odds(EnemyAttackBonus(intent), target.Ac);
            var planned = new EnemyIntent(enemy, intent, target, odds);
            _intents[enemy.Id] = planned;
            Emit(new IntentChosen(enemy, intent, target, odds));
        }
    }

    /// <summary>The intent as it stands right now (a Taunt played after intents were chosen redirects the attack).</summary>
    public EnemyIntent? IntentFor(Enemy enemy)
    {
        if (!_intents.TryGetValue(enemy.Id, out var planned)) return null;
        if (planned.Intent.Kind != IntentKind.Attack) return planned;
        var target = Taunter() ?? (planned.Target is { CanAct: true } t ? t : null);
        return planned with
        {
            Target = target,
            Odds = target is null ? null : D20.Odds(EnemyAttackBonus(planned.Intent), target.Ac)
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

    private void ExecuteIntent(Enemy enemy)
    {
        var intent = _intents.TryGetValue(enemy.Id, out var planned) ? planned.Intent : enemy.Def.Intents[0];

        if (intent.Kind == IntentKind.Guard)
        {
            Emit(new CardPlayed(enemy, GuardCard(intent), null));
            GainBlock(enemy, intent.Block);
            return;
        }

        var target = Taunter() ?? (planned?.Target is { CanAct: true } t ? t : ChooseHeroTarget());
        if (target is null) return;

        var result = D20.Roll(Rng, EnemyAttackBonus(intent), target.Ac);
        var parts = new List<RollPart> { new("ATK", intent.AttackBonus) };
        if (Difficulty.EnemyAttackBonus != 0)
            parts.Add(new RollPart(Difficulty.Name.ToUpperInvariant(), Difficulty.EnemyAttackBonus));
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

    /// <param name="grade">Timing grade for a big attack; null for ordinary attacks (and when timing already applied).</param>
    private void FinishEnemyAttack(Enemy attacker, Hero target, IntentDef intent, CheckResult result, TimingGrade? grade)
    {
        if (!result.Success) return;

        var dice = DiceExpr.Parse(intent.Damage);
        int amount = dice.Roll(Rng, result.Critical, out int[] faces);
        var notes = new List<string>();
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
        Emit(new DamageRolled(attacker, target, dice, faces, result.Critical, amount, notes, intent.Name));
        DealDamage(attacker, target, amount, intent.Name);
    }

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
        int dealt = amount - blocked;
        target.Hp = Math.Max(0, target.Hp - dealt);
        Emit(new DamageDealt(source, target, dealt, blocked, type));

        if (target.Hp == 0)
        {
            if (target is Hero h)
            {
                h.IsDowned = true;
                h.IsStable = false;
                h.DeathSaveSuccesses = 0;
                h.DeathSaveFailures = 0;
                h.Block = 0;
                h.Conditions.Clear();
                Emit(new HeroDowned(h));
            }
            else
            {
                Emit(new CombatantDied(target));
            }
        }
        return dealt;
    }

    public void Heal(Combatant target, int amount)
    {
        if (!target.IsAlive || amount <= 0) return;
        if (target is Hero { IsDowned: true } h)
            h.Revive(amount);
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
                if (target.Conditions.Has(raw["target.".Length..])) return true;
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
