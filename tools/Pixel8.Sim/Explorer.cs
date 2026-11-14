using Pixel8.Core.Cards;
using Pixel8.Core.Combat;
using Pixel8.Core.Content;
using Pixel8.Core.Dungeon;
using Pixel8.Core.Gear;
using Pixel8.Core.Heroes;
using Pixel8.Core.Rewards;
using Pixel8.Core.Runs;

namespace Pixel8.Sim;

/// <summary>What one fight looked like, for the round 9 fight-length targets.</summary>
public sealed record FightLog(int Depth, CombatKind Kind, string Lead, bool Won, int Rounds, int HeroActions, int BasicAttacks, int Rearms, int Grazes);

/// <summary>How one simulated run went.</summary>
public sealed class RunLog
{
    public int DepthsCleared { get; set; }
    public bool Won => DepthsCleared >= Run.FinalDepth;
    /// <summary>What ended the run: "Normal", "Elite", "Boss" or "Event fight", with the Depth; empty when won.</summary>
    public string DeathCause { get; set; } = "";
    public int Level { get; set; }
    public int Gold { get; set; }
    public int Steps { get; set; }
    public int DarkFights { get; set; }
    public int Mimics { get; set; }
    public int GearFound { get; set; }
    /// <summary>Equipped rare pieces at the end.</summary>
    public int RaresEquipped { get; set; }
    public bool FullyArmed { get; set; }
    public List<FightLog> Fights { get; } = new();
    /// <summary>Round 11: the party's class ids (for the per-class table).</summary>
    public List<string> Classes { get; } = new();
}

/// <summary>
/// Plays a whole run (all five Depths) on real generated dungeons through the Run API, the way a sensible
/// player would. It explores while the Torch and HP allow, detours to campfires when hurt, visits the merchant,
/// and heads for the boss once the Torch says it should. It knows the map layout (no fog), so it's a little
/// better than a real player at routing. Combat uses the greedy <see cref="AutoPlayer"/>.
/// </summary>
public sealed class Explorer
{
    /// <summary>Round 12: fights that hit the decision cap (one line each: Depth, line-up, HP), reported after the tables.</summary>
    public static readonly System.Collections.Concurrent.ConcurrentQueue<string> Stalemates = new();

    private readonly ContentDb _content;
    private readonly Run _run;
    private readonly RunLog _log = new();
    private DungeonGrid _grid = null!;
    private Tile _pos = null!;
    private readonly HashSet<Tile> _usedCampfires = new();
    private readonly HashSet<Tile> _visitedMerchants = new();
    private bool _bossDown;

    /// <summary>Torch to keep in hand beyond the walk to the boss before exploring further.</summary>
    public int TorchMargin { get; init; } = 2;

    public Explorer(ContentDb content, ulong seed, string difficultyId, IEnumerable<string> classes)
    {
        _content = content;
        _run = new Run(content, seed, classes, difficultyId);
        _log.Classes.AddRange(_run.Party.Heroes.Select(h => h.Class.Id));
    }

    public RunLog Play()
    {
        for (int depth = 1; depth <= Run.FinalDepth; depth++)
        {
            _run.Depth = depth;
            _grid = DungeonGenerator.Generate(_run.DungeonSeed(depth), _run.DungeonParamsFor(depth));
            _pos = _grid.Entrance;
            _run.Party.StartDepth(_grid.Torch);
            _usedCampfires.Clear();
            _visitedMerchants.Clear();
            _bossDown = false;

            for (int guard = 0; guard < 600 && !_bossDown; guard++)
            {
                var target = ChooseTarget();
                if (!Step(target)) return Finish();
            }
            if (!_bossDown) { _log.DeathCause = $"Stuck D{depth}"; return Finish(); }
            _log.DepthsCleared = depth;
        }
        return Finish();
    }

    private RunLog Finish()
    {
        _log.Level = _run.Party.Level;
        _log.Gold = _run.Party.Gold;
        _log.RaresEquipped = _run.Party.Heroes.Where(h => !h.IsDead).SelectMany(h => h.Loadout.Equipped).Count(g => g.EffectiveRarity >= Rarity.Rare);
        _log.FullyArmed = _run.PartyFullyArmed;
        return _log;
    }

    // ------------------------------------------------------------------ routing

    private double HpRatio
    {
        get
        {
            var alive = _run.Party.Heroes.Where(h => !h.IsDead).ToList();
            return alive.Count == 0 ? 0 : alive.Sum(h => (double)h.Hp) / alive.Sum(h => h.MaxHp);
        }
    }

    private Tile ChooseTarget()
    {
        var fromHere = _grid.Distances(new[] { _pos });
        int Dist(Tile t) => fromHere[t.X, t.Y];
        var bossDist = _grid.Distances(new[] { _grid.Boss });
        int torch = _run.Party.Torch;
        double hp = HpRatio;

        // Hurt, or the Torch is low: an unused campfire within reach comes first.
        var camp = _grid.Rooms
            .Where(t => t.Kind == TileKind.Campfire && !_usedCampfires.Contains(t) && Dist(t) >= 0 && Dist(t) <= torch)
            .OrderBy(Dist).FirstOrDefault();
        if (camp is not null && (hp < 0.6 || torch <= bossDist[_pos.X, _pos.Y] + 1)) return camp;

        // A merchant with gold to spend.
        var shop = _grid.Rooms
            .Where(t => t.Kind == TileKind.Merchant && !_visitedMerchants.Contains(t) && Dist(t) >= 0)
            .OrderBy(Dist).FirstOrDefault();
        if (shop is not null && _run.Party.Gold >= 40 && Affordable(shop, fromHere, bossDist)) return shop;

        // Explore: the nearest unvisited room worth the trip (elites only when healthy), if the Torch allows.
        if (hp >= 0.45)
        {
            var next = _grid.Rooms
                .Where(t => !t.Visited && t.Kind != TileKind.Boss && Dist(t) > 0)
                .Where(t => t.Kind != TileKind.Elite || hp >= 0.75)
                .OrderBy(t => Dist(t) - Priority(t))
                .ThenBy(t => t.Y).ThenBy(t => t.X)
                .FirstOrDefault(t => Affordable(t, fromHere, bossDist));
            if (next is not null) return next;
        }

        // Otherwise the boss, resting first if a campfire is on the way.
        if (camp is not null && hp < 0.8) return camp;
        return _grid.Boss;
    }

    /// <summary>Going to <paramref name="t"/> still leaves enough Torch to walk from there to the boss.</summary>
    private bool Affordable(Tile t, int[,] fromHere, int[,] bossDist) =>
        fromHere[t.X, t.Y] + bossDist[t.X, t.Y] + TorchMargin <= _run.Party.Torch;

    /// <summary>Rough "worth a detour" in steps.</summary>
    private static int Priority(Tile t) => t.Kind switch
    {
        TileKind.Treasure => t.Exposed ? 3 : 2,
        TileKind.Campfire => 1,
        TileKind.Event => 1,
        TileKind.Encounter => 1,
        TileKind.Elite => 1,
        _ => 0
    };

    /// <summary>Moves one room toward <paramref name="target"/> and resolves it. Returns false when the party is wiped.</summary>
    private bool Step(Tile target)
    {
        if (target == _pos)
        {
            // Standing on the target (a campfire we came back to, etc.): resolve it in place.
            return Arrive(_pos, firstVisit: false);
        }
        var toTarget = _grid.Distances(new[] { target });
        var next = _grid.LinkedNeighbours(_pos)
            .Where(n => toTarget[n.X, n.Y] >= 0)
            .OrderBy(n => toTarget[n.X, n.Y])
            .ThenBy(n => n.Kind == TileKind.Elite ? 1 : 0)
            .First();
        _pos = next;
        _run.Party.Torch = Math.Max(0, _run.Party.Torch - 1);
        _log.Steps++;
        bool first = !next.Visited;
        next.Visited = true;
        return Arrive(next, first);
    }

    private bool Arrive(Tile t, bool firstVisit)
    {
        bool hostile = t.Kind is TileKind.Encounter or TileKind.Elite or TileKind.Boss;
        if (!(firstVisit || hostile || t.Kind is TileKind.Campfire or TileKind.Merchant)) return true;

        switch (t.Kind)
        {
            case TileKind.Treasure when t.Mimic:
                _log.Mimics++;
                if (!Fight(new FightRequest(CombatKind.Elite, Enemies: new[] { "mimic" }, Mimic: true), "Mimic")) return false;
                Clear(t);
                break;
            case TileKind.Treasure:
                _run.OpenTreasure();
                Clear(t);
                if (_run.RollLoot(LootSource.Treasure) is { } loot) TakeBest(loot);
                break;
            case TileKind.Trap:
                _run.SpringTrap();
                Clear(t);
                break;
            case TileKind.Encounter:
                if (!Fight(new FightRequest(CombatKind.Normal), "Normal")) return false;
                Clear(t);
                break;
            case TileKind.Elite:
                if (!Fight(new FightRequest(CombatKind.Elite), "Elite")) return false;
                Clear(t);
                break;
            case TileKind.Boss:
                if (!Fight(new FightRequest(CombatKind.Boss), "Boss")) return false;
                _bossDown = true;
                break;
            case TileKind.Event:
                Clear(t);
                if (!PlayEvent()) return false;
                break;
            case TileKind.Campfire when !_usedCampfires.Contains(t):
                _usedCampfires.Add(t);
                _run.Party.Torch += _content.Balance.CampfireTorch;
                Campfire();
                break;
            case TileKind.Merchant when !_visitedMerchants.Contains(t):
                _visitedMerchants.Add(t);
                Shop(_run.CreateMerchant());
                break;
        }
        return true;
    }

    private static void Clear(Tile t)
    {
        t.Kind = TileKind.Empty;
        t.Exposed = false;
        t.Mimic = false;
    }

    // ------------------------------------------------------------------ fights

    private bool Fight(FightRequest request, string label)
    {
        var combat = _run.StartCombat(request);
        if (combat.Dark) _log.DarkFights++;
        CombatPhase outcome;
        try
        {
            outcome = AutoPlayer.RunToEnd(combat);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Fight did not end", StringComparison.Ordinal))
        {
            // Round 12: a stuck fight counts as a loss and is reported, instead of crashing the whole Sim.
            Stalemates.Enqueue($"D{_run.Depth} {label} round {combat.Round}: foes " +
                string.Join(", ", combat.Enemies.Where(e => e.IsAlive).Select(e => $"{e.Def.Name} {e.Hp}/{e.MaxHp}")) +
                " | heroes " + string.Join(", ", combat.Heroes.Select(h => $"{h.Name} {h.Hp}/{h.MaxHp}{(h.IsDowned ? " downed" : "")}")));
            _log.Fights.Add(Summarise(combat, false));
            _log.DeathCause = $"Stalemate {label} D{_run.Depth}";
            return false;
        }
        _log.Fights.Add(Summarise(combat, outcome == CombatPhase.Victory));
        if (outcome != CombatPhase.Victory)
        {
            _log.DeathCause = $"{label} D{_run.Depth}";
            return false;
        }

        _run.AwardXp(combat.XpEarned);
        AutoPlayer.ResolveLevelUps(_run);
        var draft = _run.CreateDraft();
        if (draft.Offers.Count > 0)
        {
            // The best card on offer: highest rarity, then the hero with the smallest deck.
            var pick = draft.Offers
                .OrderByDescending(o => o.Card.Rarity)
                .ThenBy(o => o.Hero.Deck.AllCards.Count())
                .First();
            draft.Choose(pick, _run.LootRng);
        }

        var source = request.Mimic ? LootSource.Mimic
            : request.Kind switch { CombatKind.Boss => LootSource.Boss, CombatKind.Elite => LootSource.Elite, _ => LootSource.Normal };
        if (_run.RollLoot(source) is { } loot) TakeBest(loot);
        return true;
    }

    // ------------------------------------------------------------------ gear (round 10)

    /// <summary>A rough "how strong is this hero" score for gear choices.</summary>
    private static double Score(GearStats st) =>
        2 * st.AttackMod + st.Weapon.Average + 3 * st.Ac + 2 * st.SpellDc + 2 * st.Saves + 0.5 * st.MaxHpBonus
        + 0.2 * st.Initiative + 4 * st.FightLuck + 4 * st.Rearm + st.StartBlock;

    /// <summary>How much better the best hero would get with this piece (0 if nobody gains).</summary>
    private double Gain(GearInstance g)
    {
        if (g.Def.Cursed) return 0;
        double best = 0;
        foreach (var h in _run.Party.Heroes.Where(h => !h.IsDead && h.CanUse(g.Def)))
            if (_run.CurrentStats(h) is { } now && _run.PreviewEquip(h, g) is { } then)
                best = Math.Max(best, Score(then) - Score(now));
        return best;
    }

    /// <summary>Takes the most useful choice (dropping the least valuable pack item if full), then re-equips.</summary>
    private void TakeBest(LootOffer offer)
    {
        var pick = offer.Choices.OrderByDescending(Gain).ThenByDescending(g => g.EffectiveRarity).First();
        if (_run.Party.PackFull)
        {
            var worst = _run.Party.Pack.OrderBy(Gain).ThenBy(g => _run.SellPrice(g)).First();
            if (Gain(worst) >= Gain(pick) && _run.SellPrice(worst) >= _run.SellPrice(pick)) return;
            _run.Drop(worst);
        }
        if (_run.TakeLoot(pick)) _log.GearFound++;
        EquipBest();
    }

    /// <summary>Greedily equips pack gear wherever it raises a hero's score.</summary>
    private void EquipBest()
    {
        for (int pass = 0; pass < 8; pass++)
        {
            bool changed = false;
            foreach (var g in _run.Party.Pack.Where(g => !g.Def.Cursed).ToList())
            {
                Hero? bestHero = null;
                double bestGain = 0.01;
                foreach (var h in _run.Party.Heroes.Where(h => !h.IsDead))
                {
                    if (!_run.CanEquip(h, g, out _)) continue;
                    if (_run.CurrentStats(h) is not { } now || _run.PreviewEquip(h, g) is not { } then) continue;
                    double gain = Score(then) - Score(now);
                    if (gain > bestGain) { bestGain = gain; bestHero = h; }
                }
                if (bestHero is null) continue;
                _run.Equip(bestHero, g);
                changed = true;
            }
            if (!changed) break;
        }
    }

    private FightLog Summarise(CombatState c, bool won)
    {
        int actions = 0, basics = 0, rearms = 0, grazes = 0;
        foreach (var e in c.Events)
        {
            switch (e)
            {
                case CardPlayed p when p.Actor.IsHero && p.Card.Speed == CardSpeed.Action:
                    actions++;
                    if (p.Card.Id == ContentDb.BasicStrikeId || p.Card.Id is "basic.fire_bolt" or "basic.produce_flame") basics++;
                    break;
                case Rearmed:
                    actions++;
                    rearms++;
                    break;
                case DamageRolled d when d.Source.IsHero && d.Notes.Any(n => n.Contains("GRAZE", StringComparison.OrdinalIgnoreCase)):
                    grazes++;
                    break;
            }
        }
        string lead = c.Enemies.OrderByDescending(e => e.Def.Tier).ThenByDescending(e => e.MaxHp).First().Def.Name;
        return new FightLog(_run.Depth, c.Kind, lead, won, c.Round, actions, basics, rearms, grazes);
    }

    // ------------------------------------------------------------------ events, campfires, shops

    private bool PlayEvent()
    {
        var ev = _run.PickEvent();
        if (ev is null) return true;
        var option = ev.Options
            .Where(o => _run.CanChoose(o, out _))
            .OrderByDescending(ScoreOption)
            .FirstOrDefault();
        if (option is null) return true;

        var result = _run.ResolveEvent(option);
        AutoPlayer.ResolveLevelUps(_run);
        if (result.RemoveCard) RemoveWeakestCard();
        if (result.Loot is { Choices.Count: > 0 } loot) TakeBest(loot);
        EquipBest();
        return result.Fight is null || Fight(result.Fight, "Event fight");
    }

    /// <summary>Expected value of an event option, in rough "gold" units.</summary>
    private double ScoreOption(EventOption o)
    {
        double p = 1;
        if (o.Check is { } check)
        {
            var roller = _run.Party.Heroes.Where(h => h.CanAct)
                .Select(h => check.Mod.ToLowerInvariant() switch
                {
                    "atk" => h.AttackMod,
                    "dex" => h.Class.InitMod + 2,
                    "spell" => h.SpellAttackMod,
                    _ => 0
                }).DefaultIfEmpty(0).Max();
            p = Pixel8.Core.Dice.D20.Odds(roller, check.Dc).Chance;
        }
        double Value(IReadOnlyList<EventOutcome>? outcomes) => (outcomes ?? Array.Empty<EventOutcome>()).Sum(OutcomeValue);
        return p * Value(o.Success) + (1 - p) * Value(o.Failure) - o.Gold - 3 * o.Torch;
    }

    private double OutcomeValue(EventOutcome o)
    {
        double a = double.TryParse(o.Amount, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;
        double hp = HpRatio;
        return o.Type.ToLowerInvariant() switch
        {
            "gold" => a,
            "luck" => 12 * a,
            "torch" => 3 * a,
            "xp" => a,
            "heal" => a * 100 * (1 - hp),
            "maxhp" => 6 * a,
            "damage" => -8,
            "item" => 20,
            "upgraderandom" => 25,
            "addcard" => 20,
            "removecard" => 15,
            "gear" => 30,
            "improvegear" => 25,
            "cursedgear" => -5,
            "fight" => string.Equals(o.Kind, "elite", StringComparison.OrdinalIgnoreCase)
                ? (hp > 0.8 ? 10 : -40)
                : (hp > 0.6 ? 12 : -25) + (o.Surprise ? 8 : 0),
            _ => 0
        };
    }

    private void Campfire()
    {
        // Rest when hurt, otherwise upgrade the best card.
        if (HpRatio < 0.8 || !_run.UpgradableCards().Any())
        {
            _run.Rest();
            return;
        }
        var (_, card) = _run.UpgradableCards().OrderByDescending(x => x.Card.BaseDef.Rarity).First();
        _run.UpgradeCard(card);
    }

    private void Shop(Merchant merchant)
    {
        var party = _run.Party;
        // Two healing potions, then a removal of the weakest card, then the best card we can afford.
        foreach (var offer in merchant.Offers.Where(o => o.Kind == ShopKind.Item).ToList())
            if (party.Bag.Count < 3 && Merchant.CanAfford(party, offer))
                merchant.Buy(party, offer, _run.LootRng);

        var removal = merchant.Offers.FirstOrDefault(o => o.Kind == ShopKind.RemoveCard);
        if (removal is not null && Merchant.CanAfford(party, removal) && WeakestCard() is { } weak && _run.CanRemoveFrom(weak.Hero))
            merchant.RemoveCard(party, removal, weak.Hero, weak.Card);

        foreach (var offer in merchant.Offers.Where(o => o.Kind == ShopKind.Card && !o.Hero!.IsDead)
                     .OrderByDescending(o => o.Card!.Rarity).ToList())
            if (Merchant.CanAfford(party, offer))
                merchant.Buy(party, offer, _run.LootRng);

        // Gear that makes someone noticeably stronger.
        foreach (var offer in merchant.Offers.Where(o => o.Kind == ShopKind.Gear && !o.Sold).OrderByDescending(o => Gain(o.Gear!)).ToList())
            if (Gain(offer.Gear!) >= 2 && Merchant.CanAfford(party, offer) && !party.PackFull)
            {
                merchant.Buy(party, offer, _run.LootRng);
                EquipBest();
            }
        // Sell what nobody wants.
        foreach (var g in party.Pack.Where(g => Gain(g) <= 0).ToList())
            Merchant.Sell(party, g, _run.SellPrice(g));

        var upgrade = merchant.Offers.FirstOrDefault(o => o.Kind == ShopKind.UpgradeCard);
        if (upgrade is not null && Merchant.CanAfford(party, upgrade))
        {
            var best = _run.UpgradableCards().OrderByDescending(x => x.Card.BaseDef.Rarity).FirstOrDefault();
            if (best.Card is not null) merchant.UpgradeCard(party, upgrade, best.Card);
        }
    }

    /// <summary>The weakest card in the party: a common, not upgraded, in the biggest deck.</summary>
    private (Hero Hero, CardInstance Card)? WeakestCard()
    {
        var pick = _run.Party.Heroes.Where(h => !h.IsDead && _run.CanRemoveFrom(h))
            .SelectMany(h => h.Deck.AllCards.Select(c => (Hero: h, Card: c)))
            .OrderBy(x => x.Card.BaseDef.Rarity)
            .ThenBy(x => x.Card.Upgraded)
            .ThenByDescending(x => x.Hero.Deck.AllCards.Count())
            .ThenBy(x => x.Card.Uid)
            .FirstOrDefault();
        return pick.Hero is null ? null : pick;
    }

    private void RemoveWeakestCard()
    {
        if (WeakestCard() is { } w) _run.RemoveCardFree(w.Hero, w.Card);
    }
}
