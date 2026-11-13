using Pixel8.Core.Cards;
using Pixel8.Core.Combat;
using Pixel8.Core.Content;
using Pixel8.Core.Dice;
using Pixel8.Core.Heroes;
using Pixel8.Core.Randomness;
using Pixel8.Core.Rewards;

namespace Pixel8.Core.Runs;

/// <summary>Title-screen option: a merchant tile on each Depth, a shop at every campfire, or a shop that's always open (M).</summary>
public enum ShopMode { OnMap, AtCampfires, Anytime }

/// <summary>A level-up reward each hero picks: +HP, upgrade one of their cards, or remove one for good.</summary>
public enum LevelUpChoice { MaxHp, UpgradeCard, RemoveCard }

/// <summary>What an event option did, for the event screen.</summary>
public sealed class EventResult
{
    public bool Success { get; set; } = true;
    public Hero? Roller { get; set; }
    public CheckResult? Roll { get; set; }
    public List<string> Lines { get; } = new();
    /// <summary>The player must now pick a card to remove (<see cref="Run.RemoveCardFree"/>).</summary>
    public bool RemoveCard { get; set; }
}

/// <summary>
/// One playthrough: the party, the current Depth and the random streams, which are forked
/// by system so a fight never changes the next dungeon's layout for the same seed.
/// </summary>
public sealed class Run
{
    public const int FinalDepth = 3;

    private readonly Rng _combatRng;
    private readonly Rng _encounterRng;

    public Run(ContentDb content, ulong seed, IEnumerable<string> classIds, string? difficultyId = null)
    {
        Content = content;
        Seed = seed;
        DifficultyId = difficultyId ?? content.Balance.DefaultDifficulty;
        Difficulty = content.Balance.DifficultyOf(DifficultyId);
        var root = new Rng(seed);
        Party = Party.Create(content, classIds, root.Fork("party"), Difficulty);
        _combatRng = root.Fork("combat");
        _encounterRng = root.Fork("encounters");
        LootRng = root.Fork("loot");
    }

    public ContentDb Content { get; }
    public ulong Seed { get; }
    public Party Party { get; }
    public int Depth { get; set; } = 1;
    public string DifficultyId { get; }
    public DifficultyDef Difficulty { get; }

    /// <summary>Treasure, traps, draft and other out-of-combat rolls.</summary>
    public Rng LootRng { get; }

    /// <summary>Seed for a Depth's dungeon layout; reroll bumps it (debug R key).</summary>
    public ulong DungeonSeed(int depth, int reroll = 0) => Seed * 31 + (ulong)depth * 1000 + (ulong)reroll;

    /// <summary>Timed reactions setting, copied into every fight.</summary>
    public TimingMode Timing { get; set; } = TimingMode.On;

    /// <summary>Where the merchant is found (title-screen option).</summary>
    public ShopMode Shop { get; set; } = ShopMode.OnMap;

    public CombatState StartCombat(CombatKind kind)
    {
        var enemies = Encounters.Roll(Content, kind, Depth, _encounterRng, Difficulty);
        return new CombatState(Party, enemies, Content, _combatRng, kind, Difficulty)
        {
            Timing = Timing,
            Dark = Party.Torch <= 0
        };
    }

    // ------------------------------------------------------------------ experience

    private readonly List<Hero> _levelUps = new();

    /// <summary>Heroes still to pick a level-up reward, in order (one entry per hero per level gained).</summary>
    public IReadOnlyList<Hero> PendingLevelUps => _levelUps;

    /// <summary>Total XP needed for the next level.</summary>
    public int NextLevelXp => Content.Balance.XpForLevel(Party.Level + 1);

    /// <summary>Adds party XP. Every level gained queues one reward pick for each living hero. Returns levels gained.</summary>
    public int AwardXp(int xp)
    {
        if (xp <= 0) return 0;
        Party.Xp += xp;
        int gained = 0;
        while (Party.Xp >= Content.Balance.XpForLevel(Party.Level + 1))
        {
            Party.Level++;
            gained++;
            _levelUps.AddRange(Party.Heroes.Where(h => !h.IsDead));
        }
        return gained;
    }

    public bool CanRemoveFrom(Hero hero) => hero.Deck.AllCards.Count() > Content.Balance.MinDeckSize;

    public bool CanLevelUp(Hero hero, LevelUpChoice choice) => choice switch
    {
        LevelUpChoice.MaxHp => true,
        LevelUpChoice.UpgradeCard => hero.Deck.AllCards.Any(c => c.CanUpgrade),
        LevelUpChoice.RemoveCard => CanRemoveFrom(hero),
        _ => false
    };

    /// <summary>
    /// Resolves the next pending level-up, which must belong to <paramref name="hero"/>:
    /// +HP (max and current), upgrade one of their cards, or remove one of their cards for good.
    /// </summary>
    public void ApplyLevelUp(Hero hero, LevelUpChoice choice, CardInstance? card = null)
    {
        if (_levelUps.Count == 0 || !ReferenceEquals(_levelUps[0], hero))
            throw new InvalidOperationException($"{hero.Name} has no level-up waiting.");
        if (!CanLevelUp(hero, choice)) throw new InvalidOperationException($"{hero.Name} can't take {choice} now.");

        switch (choice)
        {
            case LevelUpChoice.MaxHp:
            {
                int hp = Content.Balance.LevelUpHp;
                hero.MaxHp += hp;
                if (!hero.IsDowned && !hero.IsDead) hero.Hp = Math.Min(hero.MaxHp, hero.Hp + hp);
                break;
            }
            case LevelUpChoice.UpgradeCard:
                if (card is null || !card.CanUpgrade || !hero.Deck.AllCards.Contains(card))
                    throw new ArgumentException("Pick one of the hero's cards that can be upgraded.", nameof(card));
                card.Upgraded = true;
                break;
            case LevelUpChoice.RemoveCard:
                if (card is null || !hero.Deck.RemoveCard(card))
                    throw new ArgumentException("Pick one of the hero's cards.", nameof(card));
                break;
        }
        _levelUps.RemoveAt(0);
    }

    // ------------------------------------------------------------------ events

    /// <summary>A random event for this Depth (weighted, respecting minDepth), or null when events.json has none.</summary>
    public EventDef? PickEvent()
    {
        var table = Content.Events.Values
            .Where(e => e.MinDepth <= Depth)
            .OrderBy(e => e.Id, StringComparer.Ordinal)
            .Select(e => (e, Math.Max(1, e.Weight)))
            .ToList();
        return table.Count == 0 ? null : LootRng.Weighted(table);
    }

    public bool CanChoose(EventOption option, out string reason)
    {
        reason = Party.Gold < option.Gold ? $"Needs {option.Gold} gold."
            : Party.Torch < option.Torch ? $"Needs {option.Torch} Torch."
            : "";
        return reason.Length == 0;
    }

    /// <summary>
    /// Pays the option's cost, rolls its check (best hero rolls d20 + the named stat vs DC) and applies the
    /// success or failure outcomes. A "removeCard" outcome is left for the player (<see cref="EventResult.RemoveCard"/>).
    /// </summary>
    public EventResult ResolveEvent(EventOption option)
    {
        if (!CanChoose(option, out string reason)) throw new InvalidOperationException(reason);
        Party.Gold -= option.Gold;
        Party.Torch -= option.Torch;

        var result = new EventResult();
        bool success = true;
        if (option.Check is { } check)
        {
            var roller = Party.Heroes.Where(h => h.CanAct).OrderByDescending(h => EventMod(h, check.Mod)).FirstOrDefault();
            int mod = roller is null ? 0 : EventMod(roller, check.Mod);
            var roll = D20.Roll(LootRng, mod, check.Dc);
            success = roll.Success;
            result.Roller = roller;
            result.Roll = roll;
            result.Lines.Add($"{roller?.Name ?? "The party"} rolls d20 {roll.Kept}{(mod >= 0 ? "+" : "")}{mod} = {roll.Total} vs DC {check.Dc}: {(success ? "success!" : "failure.")}");
        }
        result.Success = success;
        foreach (var outcome in (success ? option.Success : option.Failure) ?? Array.Empty<EventOutcome>())
            ApplyOutcome(outcome, result);
        return result;
    }

    private static int EventMod(Hero h, string mod) => mod.ToLowerInvariant() switch
    {
        "atk" => h.AttackMod,
        "dex" => h.Class.InitMod + 2,
        "spell" => h.SpellAttackMod,
        _ => 0
    };

    private void ApplyOutcome(EventOutcome o, EventResult result)
    {
        var living = Party.Heroes.Where(h => !h.IsDead).ToList();
        int amount = int.TryParse(o.Amount, out int a) ? a : 0;
        switch (o.Type.ToLowerInvariant())
        {
            case "gold":
                Party.Gold = Math.Max(0, Party.Gold + amount);
                result.Lines.Add(amount >= 0 ? $"+{amount} gold." : $"{amount} gold.");
                break;
            case "luck":
                Party.Luck = Math.Max(0, Party.Luck + amount);
                result.Lines.Add(amount >= 0 ? $"+{amount} Luck." : $"{amount} Luck.");
                break;
            case "torch":
                Party.Torch = Math.Max(0, Party.Torch + amount);
                result.Lines.Add(amount >= 0 ? $"+{amount} Torch." : $"{amount} Torch.");
                break;
            case "xp":
            {
                int levels = AwardXp(amount);
                result.Lines.Add($"+{amount} XP." + (levels > 0 ? " Level up!" : ""));
                break;
            }
            case "heal":
            {
                double fraction = double.TryParse(o.Amount, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double f) ? f : 0.2;
                foreach (var h in living)
                {
                    int heal = Math.Max(1, (int)Math.Round(h.MaxHp * fraction));
                    if (h.IsDowned) h.Revive(heal);
                    else h.Hp = Math.Min(h.MaxHp, h.Hp + heal);
                }
                result.Lines.Add($"The party heals {(int)Math.Round(fraction * 100)}%.");
                break;
            }
            case "maxhp":
                foreach (var h in living)
                {
                    h.MaxHp += amount;
                    h.Hp = Math.Min(h.MaxHp, h.Hp + amount);
                }
                result.Lines.Add($"Every hero gains {amount} max HP.");
                break;
            case "damage":
            {
                var targets = o.Target.Equals("random", StringComparison.OrdinalIgnoreCase) && living.Count > 0
                    ? new List<Hero> { LootRng.Pick(living) }
                    : living;
                foreach (var h in targets)
                {
                    // Out of combat, events hurt but never knock a hero down.
                    int dmg = DiceExpr.Parse(o.Dice ?? "1d4").Roll(LootRng);
                    h.Hp = Math.Max(1, h.Hp - dmg);
                    result.Lines.Add($"{h.Name} takes {dmg} damage.");
                }
                break;
            }
            case "item":
                if (o.Id is not null && Content.Items.TryGetValue(o.Id, out var item))
                {
                    Party.Bag.Add(item.Id);
                    result.Lines.Add($"Found: {item.Name}.");
                }
                break;
            case "upgraderandom":
            {
                var options = UpgradableCards().ToList();
                if (options.Count == 0) { result.Lines.Add("Nothing left to upgrade."); break; }
                var (hero, card) = LootRng.Pick(options);
                card.Upgraded = true;
                result.Lines.Add($"{hero.Name}'s {card.BaseDef.Name} is upgraded!");
                break;
            }
            case "addcard":
            {
                if (living.Count == 0) break;
                var hero = LootRng.Pick(living);
                var def = CardDraft.RollCardFor(hero, Content, LootRng);
                if (def is null) break;
                hero.Deck.AddCard(new CardInstance(def), LootRng);
                result.Lines.Add($"{def.Name} joins {hero.Name}'s deck.");
                break;
            }
            case "removecard":
                result.RemoveCard = true;
                result.Lines.Add("Choose a card to give up.");
                break;
        }
    }

    /// <summary>Removes a card chosen after an event (respects the minimum deck size).</summary>
    public bool RemoveCardFree(Hero hero, CardInstance card) => CanRemoveFrom(hero) && hero.Deck.RemoveCard(card);

    public Merchant CreateMerchant() => Merchant.Create(Party, Content, LootRng);

    // ------------------------------------------------------------------ save / load

    /// <summary>Everything about the run except the map (the map screen adds <see cref="RunSave.Map"/>).</summary>
    public RunSave ToSave() => new()
    {
        Seed = Seed,
        DifficultyId = DifficultyId,
        Depth = Depth,
        Timing = Timing,
        Shop = Shop,
        Classes = Party.Heroes.Select(h => h.Class.Id).ToList(),
        Gold = Party.Gold,
        Luck = Party.Luck,
        Torch = Party.Torch,
        Xp = Party.Xp,
        Level = Party.Level,
        Bag = Party.Bag.ToList(),
        Heroes = Party.Heroes.Select(SaveGame.Hero).ToList(),
        PendingLevelUps = _levelUps.Select(h => Party.Heroes.ToList().IndexOf(h)).Where(i => i >= 0).ToList(),
        CombatRng = _combatRng.GetState(),
        EncounterRng = _encounterRng.GetState(),
        LootRng = LootRng.GetState()
    };

    /// <summary>Rebuilds a run from a save. Throws <see cref="InvalidDataException"/> for an unknown save version.</summary>
    public static Run FromSave(ContentDb content, RunSave save)
    {
        if (save.Version != RunSave.CurrentVersion)
            throw new InvalidDataException($"Save version {save.Version} isn't supported (expected {RunSave.CurrentVersion}).");
        if (save.Classes.Count == 0 || save.Heroes.Count != save.Classes.Count)
            throw new InvalidDataException("The save has no party.");

        var run = new Run(content, save.Seed, save.Classes, save.DifficultyId)
        {
            Depth = Math.Clamp(save.Depth, 1, FinalDepth),
            Timing = save.Timing,
            Shop = save.Shop
        };
        var party = run.Party;
        party.Gold = save.Gold;
        party.Luck = save.Luck;
        party.Torch = save.Torch;
        party.Xp = save.Xp;
        party.Level = Math.Max(1, save.Level);
        party.Bag.Clear();
        party.Bag.AddRange(save.Bag.Where(content.Items.ContainsKey));
        for (int i = 0; i < party.Heroes.Count; i++)
            SaveGame.Restore(party.Heroes[i], save.Heroes[i], content);
        foreach (int i in save.PendingLevelUps)
            if (i >= 0 && i < party.Heroes.Count && !party.Heroes[i].IsDead)
                run._levelUps.Add(party.Heroes[i]);
        if (save.CombatRng.Length == 4) run._combatRng.SetState(save.CombatRng);
        if (save.EncounterRng.Length == 4) run._encounterRng.SetState(save.EncounterRng);
        if (save.LootRng.Length == 4) run.LootRng.SetState(save.LootRng);
        return run;
    }

    /// <summary>Campfire "Upgrade" (instead of Rest): upgrades one card. Returns false if it can't be upgraded.</summary>
    public bool UpgradeCard(CardInstance card)
    {
        if (!card.CanUpgrade) return false;
        card.Upgraded = true;
        return true;
    }

    /// <summary>Every card in the party that can still be upgraded, by hero.</summary>
    public IEnumerable<(Hero Hero, CardInstance Card)> UpgradableCards() =>
        Party.Heroes.Where(h => !h.IsDead)
            .SelectMany(h => h.Deck.AllCards.Where(c => c.CanUpgrade).Select(c => (h, c)));

    /// <summary>
    /// Uses a Bag item outside combat (only Heal effects do anything here). Removes it from the Bag.
    /// Returns the HP healed, or -1 if the item isn't in the Bag or can't be used on that hero.
    /// </summary>
    public int UseItemOutOfCombat(string itemId, Hero target)
    {
        if (target.IsDead || !Party.Bag.Contains(itemId) || !Content.Items.TryGetValue(itemId, out var item)) return -1;
        var heals = item.Effects.Where(e => e.Type.Equals("Heal", StringComparison.OrdinalIgnoreCase)).ToList();
        if (heals.Count == 0) return -1;

        Party.Bag.Remove(itemId);
        int before = target.Hp;
        foreach (var e in heals)
        {
            string dice = e.Args is not null && e.Args.TryGetValue("dice", out var d) ? d : "1";
            int amount = DiceExpr.Parse(dice).Roll(LootRng);
            if (target.IsDowned) target.Revive(amount);
            else target.Hp = Math.Min(target.MaxHp, target.Hp + amount);
        }
        return target.Hp - before;
    }

    public CardDraft CreateDraft() => CardDraft.Create(Party, Content, LootRng);

    /// <summary>Campfire: every living hero heals a third of max HP and restocks their deck.</summary>
    public void Rest()
    {
        foreach (var h in Party.Heroes.Where(h => !h.IsDead))
        {
            if (h.IsDowned) h.Revive(1);
            h.Hp = Math.Min(h.MaxHp, h.Hp + Math.Max(1, h.MaxHp / 3));
            h.Deck.Restock(LootRng);
        }
    }
}

/// <summary>Builds enemy groups from the content tiers (1 = normal, 2 = elite, 3 = boss).</summary>
public static class Encounters
{
    /// <summary>Creates one enemy with balance scaling applied. Use this everywhere (game, Sim, tests).</summary>
    public static Enemy Spawn(ContentDb content, string enemyId, string? name = null, DifficultyDef? difficulty = null) =>
        Spawn(content, content.Enemy(enemyId), name, difficulty);

    public static Enemy Spawn(ContentDb content, EnemyDef def, string? name = null, DifficultyDef? difficulty = null) =>
        new(def, name, (int)Math.Round(def.Hp * content.Balance.EnemyHpMultiplier * (difficulty?.EnemyHpMultiplier ?? 1)));

    public static List<Enemy> Roll(ContentDb content, CombatKind kind, int depth, Rng rng, DifficultyDef? difficulty = null)
    {
        var tier1 = Fallback(PoolOf(content, 1, depth), PoolOf(content, 1, int.MaxValue));
        var picks = new List<EnemyDef>();

        switch (kind)
        {
            case CombatKind.Boss:
                picks.Add(rng.Pick(Fallback(PoolOf(content, 3, depth), tier1)));
                for (int i = 1; i < depth; i++) picks.Add(rng.Pick(tier1));
                break;
            case CombatKind.Elite:
                picks.Add(rng.Pick(Fallback(PoolOf(content, 2, depth), tier1)));
                for (int i = 0; i < depth; i++) picks.Add(rng.Pick(tier1));
                break;
            default:
            {
                var b = content.Balance;
                int bonus = difficulty?.GroupSizeBonus ?? 0;
                int min = Math.Clamp(Math.Max(1, b.NormalGroupMin) + depth - 1 + bonus, 1, 4);
                int max = Math.Clamp(Math.Max(min, b.NormalGroupMax + depth - 1 + bonus), min, 4);
                int count = rng.Range(min, max + 1);
                for (int i = 0; i < count; i++) picks.Add(rng.Pick(tier1));
                break;
            }
        }

        // Duplicates get letters so the log and target names stay readable: "Goblin A", "Goblin B".
        var counts = picks.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.Count());
        var seen = new Dictionary<string, int>();
        var enemies = new List<Enemy>();
        foreach (var def in picks)
        {
            string name = def.Name;
            if (counts[def.Id] > 1)
            {
                int n = seen.GetValueOrDefault(def.Id);
                seen[def.Id] = n + 1;
                name = $"{def.Name} {(char)('A' + n)}";
            }
            enemies.Add(Spawn(content, def, name, difficulty));
        }
        return enemies;
    }

    private static List<EnemyDef> PoolOf(ContentDb content, int tier, int depth) =>
        content.Enemies.Values
            .Where(e => e.Tier == tier && e.MinDepth <= depth)
            .OrderBy(e => e.Id, StringComparer.Ordinal)
            .ToList();

    private static List<EnemyDef> Fallback(List<EnemyDef> pool, List<EnemyDef> fallback) =>
        pool.Count > 0 ? pool : fallback;
}
