using Pixel8.Core.Cards;
using Pixel8.Core.Combat;
using Pixel8.Core.Content;
using Pixel8.Core.Heroes;
using Pixel8.Core.Runs;

namespace Pixel8.Tests;

/// <summary>Round 8 (v0.2.0): events that start fights, surprise, Depth-limited events, max HP loss, rarity rolls.</summary>
public class FightEventTests
{
    private static readonly string[] Trio = { "fighter", "rogue", "cleric" };

    private static Run MakeRun(int gold = 50, int depth = 1)
    {
        var run = new Run(TestContent.Db, 21, Trio) { Timing = TimingMode.Off, Depth = depth };
        run.Party.StartDepth(20);
        run.Party.Gold = gold;
        return run;
    }

    [Fact]
    public void Repo_events_load_without_errors()
    {
        Assert.Empty(TestContent.Db.Validate());
        foreach (var id in new[] { "bandit_toll", "trapped_chest", "cursed_altar", "captive", "collapsed_tunnel" })
            Assert.True(TestContent.Db.Events.ContainsKey(id), id);
    }

    [Fact]
    public void Deep_events_never_appear_on_depth_1()
    {
        var run = MakeRun();
        for (int i = 0; i < 400; i++)
            Assert.True(run.PickEvent()!.MinDepth <= 1);
    }

    [Fact]
    public void A_fight_outcome_asks_for_a_fight()
    {
        var run = MakeRun();
        var toll = TestContent.Db.Events["bandit_toll"];
        var result = run.ResolveEvent(toll.Options[2]);   // cut the rope
        Assert.NotNull(result.Fight);
        Assert.Equal(CombatKind.Normal, result.Fight!.Kind);
        Assert.False(result.Fight.Surprise);
    }

    [Fact]
    public void Paying_the_toll_starts_no_fight()
    {
        var run = MakeRun(gold: 20);
        var result = run.ResolveEvent(TestContent.Db.Events["bandit_toll"].Options[0]);
        Assert.Null(result.Fight);
        Assert.Equal(5, run.Party.Gold);
    }

    [Fact]
    public void Rushing_the_guards_is_an_elite_ambush()
    {
        var run = MakeRun(depth: 2);
        var result = run.ResolveEvent(TestContent.Db.Events["captive"].Options[1]);
        Assert.Equal(CombatKind.Elite, result.Fight!.Kind);
        Assert.True(result.Fight.Surprise);
        var c = run.StartCombat(result.Fight);
        Assert.Equal(CombatKind.Elite, c.Kind);
        Assert.False(c.CanFlee(out _));
    }

    [Fact]
    public void Surprise_gives_heroes_advantage_in_round_1()
    {
        var run = MakeRun();
        var c = run.StartCombat(new FightRequest(CombatKind.Normal, Surprise: true));
        c.Start();
        Assert.Equal(1, c.Round);
        Assert.True(c.SurpriseNow);

        var hero = c.ActiveHero!;
        var target = c.Enemies.First(e => e.IsAlive);
        var odds = c.PreviewAttack(hero, target);
        c.Surprise = false;
        var flat = c.PreviewAttack(hero, target);
        Assert.True(odds.Chance > flat.Chance);
        c.Surprise = true;

        c.UseBasic(ContentDb.BasicStrikeId, target);
        var roll = c.Events.OfType<CheckRolled>().First(e => e.Roller.IsHero);
        Assert.Contains(roll.Breakdown, p => p.Label == "ADV SURPRISE");
    }

    [Fact]
    public void Surprise_ends_after_round_1()
    {
        var run = MakeRun();
        var c = run.StartCombat(new FightRequest(CombatKind.Normal, Surprise: true));
        c.Start();
        // Everyone passes until round 2 begins.
        for (int i = 0; i < 500 && !c.IsOver && c.Round == 1; i++)
        {
            switch (c.Phase)
            {
                case CombatPhase.HeroTurn: c.EndTurn(); break;
                case CombatPhase.AwaitingReaction: c.ResolveReaction(null); break;
                case CombatPhase.AwaitingLuck: c.ResolveLuck(false); break;
                case CombatPhase.AwaitingTiming: c.ResolveTiming(TimingGrade.Miss); break;
            }
        }
        if (!c.IsOver) Assert.False(c.SurpriseNow);
    }

    [Fact]
    public void Exact_enemies_are_spawned_when_listed()
    {
        var run = MakeRun();
        var ids = TestContent.Db.Enemies.Values.Where(e => e.Tier == 1).Take(1).Select(e => e.Id).ToList();
        ids.Add(ids[0]);
        var c = run.StartCombat(new FightRequest(CombatKind.Normal, Enemies: ids));
        Assert.Equal(2, c.Enemies.Count);
        Assert.All(c.Enemies, e => Assert.Equal(ids[0], e.Def.Id));
        Assert.EndsWith(" A", c.Enemies[0].Name);
    }

    [Fact]
    public void Losing_max_hp_never_downs_a_hero()
    {
        var run = MakeRun(depth: 2);
        foreach (var h in run.Party.Heroes) h.Hp = 2;
        int[] before = run.Party.Heroes.Select(h => h.MaxHp).ToArray();
        run.ResolveEvent(TestContent.Db.Events["cursed_altar"].Options[0]);
        for (int i = 0; i < before.Length; i++)
        {
            var h = run.Party.Heroes[i];
            Assert.Equal(before[i] - 3, h.MaxHp);
            Assert.True(h.Hp >= 1 && !h.IsDowned);
        }
    }

    [Fact]
    public void Rarity_rolls_prefer_that_rarity()
    {
        var run = MakeRun();
        var hero = run.Party.Heroes[0];
        var card = Pixel8.Core.Rewards.CardDraft.RollCardFor(hero, TestContent.Db, run.LootRng, rarity: Rarity.Uncommon);
        Assert.NotNull(card);
        bool hasUncommon = TestContent.Db.Cards.Values.Any(c => c.Rarity == Rarity.Uncommon
            && (c.ClassId == hero.Class.Id || c.ClassId == "neutral"));
        if (hasUncommon) Assert.Equal(Rarity.Uncommon, card!.Rarity);
    }
}

/// <summary>Round 8 step 3: Poison, two-random targets, Draw, the new cards, and the hit-rate changes.</summary>
public class NewCardEffectTests
{
    private static CardInstance InHand(CombatState c, Hero hero, string cardId)
    {
        var card = new CardInstance(TestContent.Db.Card(cardId));
        hero.Deck.AddCard(card, new Pixel8.Core.Randomness.Rng(1));
        while (!hero.Deck.Hand.Contains(card) && hero.Deck.DrawPile.Count > 0)
        {
            if (hero.Deck.Hand.Count >= hero.Deck.MaxHand) hero.Deck.Play(hero.Deck.Hand[0]);
            hero.Deck.Draw(1);
        }
        return card;
    }

    private static void PassUntil(CombatState c, Func<bool> done)
    {
        for (int i = 0; i < 500 && !c.IsOver && !done(); i++)
        {
            switch (c.Phase)
            {
                case CombatPhase.HeroTurn: c.EndTurn(); break;
                case CombatPhase.AwaitingReaction: c.ResolveReaction(null); break;
                case CombatPhase.AwaitingLuck: c.ResolveLuck(false); break;
                case CombatPhase.AwaitingTiming: c.ResolveTiming(TimingGrade.Miss); break;
            }
        }
    }

    [Fact]
    public void Every_class_has_five_uncommons_and_three_rares()
    {
        foreach (var cls in new[] { "fighter", "rogue", "wizard", "cleric" })
        {
            var cards = TestContent.Db.Cards.Values.Where(c => c.ClassId == cls).ToList();
            Assert.Equal(5, cards.Count(c => c.Rarity == Rarity.Uncommon));
            Assert.Equal(3, cards.Count(c => c.Rarity == Rarity.Rare));
            Assert.All(cards, c => Assert.NotNull(c.Upgrade));
        }
        Assert.True(TestContent.Db.Cards.Values.Count(c => c.ClassId == "neutral") >= 7);
    }

    [Fact]
    public void Only_rare_cards_draw()
    {
        foreach (var c in TestContent.Db.Cards.Values)
            if (c.Effects.Any(e => e.Type == "Draw")) Assert.Equal(Rarity.Rare, c.Rarity);
    }

    [Fact]
    public void Poison_ticks_at_the_start_of_each_turn_then_wears_off()
    {
        var c = TestContent.MakeFight(5, new[] { "fighter" }, new[] { "ogre" }, CombatKind.Boss);
        c.LuckPrompts = false;
        c.Timing = TimingMode.Off;
        c.Heroes[0].MaxHp = c.Heroes[0].Hp = 999;   // survive the ogre while we wait
        c.Start();
        var ogre = c.Enemies[0];
        c.ApplyPoison(ogre, 3, 2);
        Assert.Equal(3, c.PoisonOn(ogre));
        int Ticks() => c.Events.OfType<DamageDealt>().Count(d => d.Target == ogre && d.Type == "poison");
        PassUntil(c, () => c.Round >= 4);
        Assert.Equal(2, Ticks());
        Assert.False(ogre.Conditions.Has(Cond.Poisoned));
        Assert.Equal(0, c.PoisonOn(ogre));
    }

    [Fact]
    public void Poison_can_finish_an_enemy()
    {
        var c = TestContent.MakeFight(6, new[] { "fighter" }, new[] { "kobold" });
        c.LuckPrompts = false;
        c.Start();
        c.Enemies[0].Hp = 2;
        c.ApplyPoison(c.Enemies[0], 5, 3);
        PassUntil(c, () => false);
        Assert.Equal(CombatPhase.Victory, c.Phase);
    }

    [Fact]
    public void Random2_hits_two_different_enemies()
    {
        var c = TestContent.MakeFight(7, new[] { "fighter" }, new[] { "kobold", "kobold", "kobold" });
        c.LuckPrompts = false;
        c.Start();
        var hero = c.ActiveHero!;
        var card = InHand(c, hero, "fighter.whirlwind");
        c.PlayCard(card);
        var hit = c.Events.OfType<DamageRolled>().Where(d => d.Source == hero).Select(d => d.Target).ToList();
        Assert.Equal(2, hit.Count);
        Assert.Equal(2, hit.Distinct().Count());
    }

    [Fact]
    public void Draw_cards_fill_the_hand()
    {
        var c = TestContent.MakeFight(8, new[] { "fighter" }, new[] { "ogre" }, CombatKind.Boss);
        c.LuckPrompts = false;
        c.Start();
        var hero = c.ActiveHero!;
        var card = InHand(c, hero, "fighter.action_surge");
        // Leave room in hand and something to draw.
        while (hero.Deck.Hand.Count > 2) hero.Deck.Play(hero.Deck.Hand.First(x => x != card));
        hero.Deck.AddCard(new CardInstance(TestContent.Db.Card("fighter.cleave")), new Pixel8.Core.Randomness.Rng(2));
        hero.Deck.AddCard(new CardInstance(TestContent.Db.Card("fighter.cleave")), new Pixel8.Core.Randomness.Rng(3));
        int before = hero.Deck.Hand.Count;
        c.PlayCard(card);
        Assert.Equal(before - 1 + 2, hero.Deck.Hand.Count);
        Assert.Contains(card, hero.Deck.Exhausted);
    }

    [Fact]
    public void Proficiency_and_difficulty_add_to_hero_attacks()
    {
        var run = new Run(TestContent.Db, 9, new[] { "fighter" }, "normal") { Timing = TimingMode.Off };
        run.Party.StartDepth(20);
        var c = run.StartCombat(CombatKind.Normal);
        var hero = run.Party.Heroes[0];
        Assert.Equal(hero.AttackMod + 1, c.AttackModOf(hero, spell: false));   // Normal +1, level 1 = no proficiency

        run.Party.Level = 4;
        Assert.Equal(1, c.ProficiencyBonus);
        Assert.Equal(hero.AttackMod + 2, c.AttackModOf(hero, spell: false));
        Assert.Equal(hero.SpellDc + 1, c.SpellDcOf(hero));
        run.Party.Level = 8;
        Assert.Equal(3, c.ProficiencyBonus);   // levels 3, 5, 7
        Assert.Equal(4, c.HeroDamageBonus);    // levels 2, 4, 6, 8

        c.LuckPrompts = false;
        c.Start();
        c.UseBasic(ContentDb.BasicStrikeId, c.Enemies.First(e => e.IsAlive));
        var roll = c.Events.OfType<CheckRolled>().First(e => e.Roller.IsHero);
        Assert.Contains(roll.Breakdown, p => p.Label == "PROF" && p.Value == 3);
        Assert.Contains(roll.Breakdown, p => p.Label == "NORMAL" && p.Value == 1);
        Assert.Equal(hero.AttackMod + 4, roll.Result.Modifier);
    }

    [Fact]
    public void Hard_gets_no_hero_attack_bonus()
    {
        Assert.Equal(0, TestContent.Db.Balance.DifficultyOf("hard").HeroAttackBonus);
        Assert.Equal(1, TestContent.Db.Balance.DifficultyOf("easy").HeroAttackBonus);
    }
}

/// <summary>Round 8 step 4: new enemies, a boss per Depth, dragon breath (area save, recharge, one timing press).</summary>
public class BossAndAreaTests
{
    private static CombatState DragonFight(ulong seed, TimingMode timing)
    {
        var c = TestContent.MakeFight(seed, new[] { "fighter", "rogue", "cleric" }, new[] { "young_green_dragon" }, CombatKind.Boss);
        c.LuckPrompts = false;
        c.Timing = timing;
        foreach (var h in c.Heroes) h.MaxHp = h.Hp = 999;   // watch the dragon, don't die to it
        return c;
    }

    private static void Pass(CombatState c, Func<bool> done, int steps = 600)
    {
        if (c.Phase == CombatPhase.NotStarted) c.Start();
        for (int i = 0; i < steps && !c.IsOver && !done(); i++)
        {
            switch (c.Phase)
            {
                case CombatPhase.HeroTurn: c.EndTurn(); break;
                case CombatPhase.AwaitingReaction: c.ResolveReaction(null); break;
                case CombatPhase.AwaitingLuck: c.ResolveLuck(false); break;
                case CombatPhase.AwaitingTiming: c.ResolveTiming(TimingGrade.Miss); break;
            }
        }
    }

    [Fact]
    public void Each_depth_has_its_own_boss()
    {
        Assert.Equal(new[] { "ogre" }, Encounters.BossesFor(TestContent.Db, 1).Select(e => e.Id));
        Assert.Equal(new[] { "owlbear" }, Encounters.BossesFor(TestContent.Db, 2).Select(e => e.Id));
        Assert.Equal(new[] { "young_green_dragon" }, Encounters.BossesFor(TestContent.Db, 3).Select(e => e.Id));
    }

    [Fact]
    public void Deeper_normals_stay_out_of_depth_1()
    {
        var rng = new Pixel8.Core.Randomness.Rng(3);
        for (int i = 0; i < 300; i++)
            foreach (var e in Encounters.Roll(TestContent.Db, CombatKind.Normal, 1, rng))
                Assert.True(e.Def.MinDepth <= 1, e.Def.Id);
    }

    [Fact]
    public void Breath_makes_every_hero_save_and_hits_them_all()
    {
        var c = DragonFight(4, TimingMode.Off);
        int Saves() => c.Events.OfType<CheckRolled>().Count(r => r.Label.Contains("Poison Breath"));
        Pass(c, () => Saves() >= 3);
        Assert.Equal(3, Saves());
        var breathHits = c.Events.OfType<DamageDealt>().Where(d => d.Type == "Poison Breath").Select(d => d.Target).Distinct().Count();
        Assert.Equal(3, breathHits);
        Assert.Single(c.Events.OfType<DamageRolled>().Where(d => d.Type == "Poison Breath"));
    }

    [Fact]
    public void Breath_needs_a_recharge_between_uses()
    {
        var c = DragonFight(5, TimingMode.Off);
        Pass(c, () => c.Round >= 25);
        var log = c.Events.ToList();
        int lastBreath = -1;
        for (int i = 0; i < log.Count; i++)
        {
            if (log[i] is DamageRolled { Type: "Poison Breath" })
            {
                if (lastBreath >= 0)
                    Assert.Contains(log.Skip(lastBreath).Take(i - lastBreath), e => e is IntentRecharged);
                lastBreath = i;
            }
        }
        Assert.True(lastBreath >= 0, "The dragon never breathed in 25 rounds.");
    }

    [Fact]
    public void Big_breath_asks_for_one_timing_press_for_the_party()
    {
        var c = DragonFight(6, TimingMode.On);
        Pass(c, () => c.Phase == CombatPhase.AwaitingTiming && c.PendingTiming!.Area is not null);
        Assert.Equal(CombatPhase.AwaitingTiming, c.Phase);
        Assert.Equal(3, c.PendingTiming!.Area!.Count);
        c.ResolveTiming(TimingGrade.Perfect);
        Assert.DoesNotContain(c.Events.OfType<DamageDealt>(), d => d.Type == "Poison Breath" && d.Amount > 0);
    }

    [Fact]
    public void Dragon_bite_adds_poison_dice()
    {
        var c = DragonFight(7, TimingMode.Off);
        Pass(c, () => c.Events.OfType<DamageRolled>().Any(d => d.Type == "Bite"));
        var bite = c.Events.OfType<DamageRolled>().First(d => d.Type == "Bite");
        Assert.Contains(bite.Notes, n => n.EndsWith("POISON"));
    }

    [Fact]
    public void Spider_bites_poison_heroes()
    {
        var c = TestContent.MakeFight(8, new[] { "fighter" }, new[] { "giant_wolf_spider", "giant_wolf_spider" });
        c.LuckPrompts = false;
        c.Heroes[0].MaxHp = c.Heroes[0].Hp = 999;
        Pass(c, () => c.Heroes[0].Conditions.Has(Cond.Poisoned));
        Assert.True(c.Heroes[0].Conditions.Has(Cond.Poisoned));
        Assert.Equal(2, c.PoisonOn(c.Heroes[0]));
    }

    [Fact]
    public void Dragon_hp_is_scaled_at_spawn()
    {
        var dragon = Encounters.Spawn(TestContent.Db, "young_green_dragon");
        Assert.Equal((int)Math.Round(136 * 0.6 * TestContent.Db.Balance.EnemyHpMultiplier), dragon.MaxHp);
    }
}

/// <summary>Round 8 anti-grind: Rearm, graze, focus, level damage and the AC cap.</summary>
public class AntiGrindTests
{
    private static CombatState Fight(ulong seed, string enemy = "ogre", CombatKind kind = CombatKind.Boss)
    {
        var c = TestContent.MakeFight(seed, new[] { "fighter" }, new[] { enemy }, kind);
        c.LuckPrompts = false;
        c.Timing = TimingMode.Off;
        c.Heroes[0].MaxHp = c.Heroes[0].Hp = 999;
        return c;
    }

    [Fact]
    public void A_miss_grazes_and_builds_focus_until_a_hit()
    {
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var c = Fight(seed);
            var ogre = c.Enemies[0];
            ogre.Ac = 25;   // only a 20 hits, so the first Strike almost surely misses
            c.Start();
            var hero = c.ActiveHero!;
            int hp = ogre.Hp;
            c.UseBasic(ContentDb.BasicStrikeId, ogre);
            var roll = c.Events.OfType<CheckRolled>().Last(e => e.Roller.IsHero);
            if (roll.Result.Success || roll.Result.Fumble) continue;

            Assert.Equal(hp - c.GrazeOf(hero, spell: false), ogre.Hp);
            Assert.Contains(c.Events.OfType<DamageRolled>(), d => d.Notes.Contains("GRAZE"));
            Assert.Equal(TestContent.Db.Balance.FocusPerMiss, c.FocusOf(hero));
            return;
        }
        Assert.Fail("No plain miss in 200 seeds.");
    }

    [Fact]
    public void A_natural_1_does_not_graze()
    {
        var c = Fight(3);
        var ogre = c.Enemies[0];
        c.Start();
        c.FixedNextDie = 1;
        int hp = ogre.Hp;
        c.UseBasic(ContentDb.BasicStrikeId, ogre);
        Assert.Equal(hp, ogre.Hp);
    }

    [Fact]
    public void A_hit_resets_focus()
    {
        var c = Fight(4);
        var ogre = c.Enemies[0];
        c.Start();
        var hero = c.ActiveHero!;
        c.FixedNextDie = 2;   // a miss (not a fumble) against the ogre's AC 11 with +5? 2+5 = 7 < 11
        c.UseBasic(ContentDb.BasicStrikeId, ogre);
        Assert.True(c.FocusOf(hero) > 0);
        c.EndTurn();
        for (int i = 0; i < 50 && c.Phase != CombatPhase.HeroTurn && !c.IsOver; i++)
            if (c.Phase == CombatPhase.AwaitingReaction) c.ResolveReaction(null);
        c.FixedNextDie = 20;
        c.UseBasic(ContentDb.BasicStrikeId, ogre);
        Assert.Equal(0, c.FocusOf(hero));
    }

    [Theory]
    [InlineData("easy", 3)]
    [InlineData("normal", 2)]
    [InlineData("hard", 1)]
    public void Rearm_takes_cards_back_by_difficulty(string difficulty, int expected)
    {
        var run = new Run(TestContent.Db, 5, new[] { "fighter" }, difficulty) { Timing = TimingMode.Off };
        run.Party.StartDepth(20);
        var c = run.StartCombat(CombatKind.Normal);
        c.LuckPrompts = false;
        c.Start();
        var hero = c.ActiveHero!;
        // Empty the hand into the discard pile.
        foreach (var card in hero.Deck.Hand.ToList()) hero.Deck.Play(card);
        Assert.Equal(expected, c.RearmLimit(hero));
        Assert.True(c.CanRearm(out _));

        var picks = hero.Deck.Discard.Take(expected).ToList();
        c.Rearm(picks);
        Assert.All(picks, p => Assert.Contains(p, hero.Deck.Hand));
        Assert.True(c.EconomyOf(hero).ActionUsed);
        Assert.False(c.CanRearm(out _));
        Assert.Throws<InvalidOperationException>(() => c.Rearm(hero.Deck.Discard.Take(1).ToList()));
    }

    [Fact]
    public void Rearm_refuses_too_many_or_cards_not_in_the_discard()
    {
        var c = Fight(6);
        c.Start();
        var hero = c.ActiveHero!;
        foreach (var card in hero.Deck.Hand.ToList()) hero.Deck.Play(card);
        int limit = c.RearmLimit(hero);
        Assert.Throws<ArgumentException>(() => c.Rearm(hero.Deck.Discard.Take(limit + 1).ToList()));
        var stranger = new CardInstance(TestContent.Db.Card("fighter.cleave"));
        Assert.Throws<ArgumentException>(() => c.Rearm(new[] { stranger }));
    }

    [Fact]
    public void Enemy_ac_is_capped_by_depth_but_published_in_data()
    {
        Assert.Equal(18, TestContent.Db.Enemy("young_green_dragon").Ac);
        Assert.Equal(16, Encounters.Spawn(TestContent.Db, "young_green_dragon", depth: 3).Ac);
        Assert.Equal(15, Encounters.Spawn(TestContent.Db, "hobgoblin", depth: 2).Ac);
        Assert.Equal(14, Encounters.Spawn(TestContent.Db, "goblin", depth: 1).Ac);   // 15 → 14
        Assert.Equal(12, Encounters.Spawn(TestContent.Db, "kobold", depth: 1).Ac);   // under the cap: unchanged
        Assert.Equal(18, Encounters.Spawn(TestContent.Db, "young_green_dragon").Ac); // no depth: published
    }

    [Fact]
    public void Level_damage_bonus_adds_to_weapon_damage()
    {
        var c = Fight(7);
        c.Party.Level = 4;   // +2 damage
        var ogre = c.Enemies[0];
        ogre.Ac = 1;
        c.Start();
        c.UseBasic(ContentDb.BasicStrikeId, ogre);
        var dmg = c.Events.OfType<DamageRolled>().Last(d => d.Source.IsHero);
        Assert.Equal(c.Heroes[0].Weapon.Bonus + 2, dmg.Dice.Bonus);
    }
}
