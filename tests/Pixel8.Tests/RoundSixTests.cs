using Pixel8.Core.Cards;
using Pixel8.Core.Combat;
using Pixel8.Core.Content;
using Pixel8.Core.Dungeon;
using Pixel8.Core.Heroes;
using Pixel8.Core.Randomness;
using Pixel8.Core.Runs;

namespace Pixel8.Tests;

/// <summary>Session 6: darkness, class basics, items in combat, XP and level-ups, events, merchant placement.</summary>
public class DarknessTests
{
    [Fact]
    public void Dark_gives_disadvantage_and_shows_a_tag()
    {
        var c = TestContent.MakeFight(1, new[] { "fighter" }, new[] { "ogre" }, CombatKind.Boss);
        c.LuckPrompts = false;
        double light = c.PreviewAttack(c.Heroes[0], c.Enemies[0]).Chance;
        c.Dark = true;
        double dark = c.PreviewAttack(c.Heroes[0], c.Enemies[0]).Chance;
        Assert.True(dark < light);

        c.Start();
        c.UseBasic(ContentDb.BasicStrikeId, c.Enemies[0]);
        var roll = c.Events.OfType<CheckRolled>().Last(r => r.Roller is Hero);
        Assert.Contains(roll.Breakdown, p => p.Tag && p.Label == "DIS DARK");
    }

    [Fact]
    public void Run_starts_fights_dark_only_when_the_torch_is_out()
    {
        var run = new Run(TestContent.Db, 1, new[] { "fighter", "rogue", "wizard" });
        run.Party.StartDepth(5);
        Assert.False(run.StartCombat(CombatKind.Normal).Dark);
        run.Party.Torch = 0;
        Assert.True(run.StartCombat(CombatKind.Normal).Dark);
    }
}

public class ClassBasicTests
{
    private static CombatState Solo(string cls, ulong seed = 3)
    {
        var c = TestContent.MakeFight(seed, new[] { cls }, new[] { "ogre" }, CombatKind.Boss);
        c.LuckPrompts = false;
        c.Start();
        Assert.Equal(CombatPhase.HeroTurn, c.Phase);
        return c;
    }

    [Fact]
    public void Every_class_has_a_basic_action()
    {
        foreach (var cls in TestContent.Db.Heroes.Values)
        {
            Assert.False(string.IsNullOrEmpty(cls.BasicAction), cls.Id);
            Assert.Equal("basic", TestContent.Db.Card(cls.BasicAction!).ClassId);
        }
    }

    [Fact]
    public void Second_wind_heals_once_per_fight_and_leaves_the_action_free()
    {
        var c = Solo("fighter");
        var hero = c.Heroes[0];
        hero.Hp = 5;
        c.UseBasic("basic.second_wind");
        Assert.True(hero.Hp > 5);
        Assert.True(c.CanUseBasic());   // Strike / Guard still available
        Assert.False(c.CanUseBasic(TestContent.Db.Card("basic.second_wind")));
    }

    [Fact]
    public void Cunning_gives_the_next_attack_advantage_then_ends()
    {
        var c = Solo("rogue");
        var hero = c.Heroes[0];
        c.UseBasic("basic.cunning");
        Assert.True(hero.Conditions.Has(Cond.Cunning));
        c.UseBasic(ContentDb.BasicStrikeId, c.Enemies[0]);
        var roll = c.Events.OfType<CheckRolled>().Last(r => r.Roller is Hero);
        Assert.Contains(roll.Breakdown, p => p.Label == "ADV CUNNING");
        Assert.False(hero.Conditions.Has(Cond.Cunning));
    }

    [Fact]
    public void Fire_bolt_uses_the_spell_attack()
    {
        var c = Solo("wizard");
        var hero = c.Heroes[0];
        c.UseBasic("basic.fire_bolt", c.Enemies[0]);
        var roll = c.Events.OfType<CheckRolled>().Last(r => r.Roller is Hero);
        Assert.Contains(roll.Breakdown, p => p.Label == "SPELL" && p.Value == hero.SpellAttackMod);
        Assert.Equal(hero.SpellAttackMod, roll.Result.Modifier);
    }

    [Fact]
    public void Spare_the_dying_gets_a_downed_ally_up()
    {
        var c = TestContent.MakeFight(5, new[] { "fighter", "cleric" }, new[] { "ogre" }, CombatKind.Boss);
        c.Start();
        if (c.ActiveHero!.Class.Id != "cleric") c.EndTurn();
        Assert.Equal("cleric", c.ActiveHero!.Class.Id);

        var fighter = c.Heroes.First(h => h.Class.Id == "fighter");
        c.DealDamage(c.Enemies[0], fighter, 999, "test");
        Assert.True(fighter.IsDowned);
        c.UseBasic("basic.spare_the_dying", fighter);
        Assert.False(fighter.IsDowned);
        Assert.Equal(1, fighter.Hp);
    }

    [Fact]
    public void Heroes_cannot_use_another_class_basic()
    {
        var c = Solo("fighter");
        Assert.False(c.CanUseBasic(TestContent.Db.Card("basic.cunning")));
        Assert.Throws<InvalidOperationException>(() => c.UseBasic("basic.cunning"));
    }
}

public class CombatItemTests
{
    [Fact]
    public void A_potion_is_a_quick_action_on_an_ally()
    {
        var c = TestContent.MakeFight(2, new[] { "fighter" }, new[] { "ogre" }, CombatKind.Boss);
        c.Party.Bag.Add("potion_healing");
        c.Start();
        var hero = c.Heroes[0];
        hero.Hp = 3;
        Assert.Equal(TargetSide.Ally, c.TargetSideFor(TestContent.Db.Items["potion_healing"]));

        c.UseItem("potion_healing", hero);
        Assert.InRange(hero.Hp, 3 + 4, 3 + 10);
        Assert.Empty(c.Party.Bag);
        Assert.True(c.EconomyOf(hero).QuickUsed);
        Assert.Contains(c.Events, e => e is ItemUsed);
        Assert.False(c.CanUseItem("potion_healing"));
    }
}

public class LevelUpTests
{
    private static Run MakeRun()
    {
        var run = new Run(TestContent.Db, 7, new[] { "fighter", "rogue", "wizard" });
        run.Party.StartDepth(20);
        return run;
    }

    [Fact]
    public void Xp_levels_the_party_and_queues_a_pick_per_hero()
    {
        var run = MakeRun();
        Assert.Equal(0, run.AwardXp(10));
        int need = run.NextLevelXp - run.Party.Xp;
        Assert.Equal(1, run.AwardXp(need));
        Assert.Equal(2, run.Party.Level);
        Assert.Equal(3, run.PendingLevelUps.Count);
    }

    [Fact]
    public void Level_up_rewards_apply_in_order()
    {
        var run = MakeRun();
        run.AwardXp(run.NextLevelXp);
        var first = run.PendingLevelUps[0];
        var second = run.PendingLevelUps[1];
        Assert.Throws<InvalidOperationException>(() => run.ApplyLevelUp(second, LevelUpChoice.MaxHp));

        int max = first.MaxHp;
        run.ApplyLevelUp(first, LevelUpChoice.MaxHp);
        Assert.Equal(max + TestContent.Db.Balance.LevelUpHp, first.MaxHp);

        var card = second.Deck.AllCards.First(c => c.CanUpgrade);
        run.ApplyLevelUp(second, LevelUpChoice.UpgradeCard, card);
        Assert.True(card.Upgraded);

        var third = run.PendingLevelUps[0];
        int size = third.Deck.AllCards.Count();
        run.ApplyLevelUp(third, LevelUpChoice.RemoveCard, third.Deck.Hand[0]);
        Assert.Equal(size - 1, third.Deck.AllCards.Count());
        Assert.Empty(run.PendingLevelUps);
    }

    [Fact]
    public void Removal_stops_at_the_minimum_deck_size()
    {
        var run = MakeRun();
        var hero = run.Party.Heroes[0];
        while (hero.Deck.AllCards.Count() > TestContent.Db.Balance.MinDeckSize)
            hero.Deck.RemoveCard(hero.Deck.AllCards.First());
        Assert.False(run.CanLevelUp(hero, LevelUpChoice.RemoveCard));
        Assert.True(run.CanLevelUp(hero, LevelUpChoice.MaxHp));
    }

    [Fact]
    public void Winning_a_fight_earns_xp_by_tier()
    {
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var c = TestContent.MakeFight(seed, new[] { "fighter", "rogue", "wizard" }, new[] { "goblin", "bugbear" });
            if (AutoPlayer.RunToEnd(c) != CombatPhase.Victory) continue;
            var b = TestContent.Db.Balance;
            Assert.Equal(b.XpOf(TestContent.Db.Enemy("goblin")) + b.XpOf(TestContent.Db.Enemy("bugbear")), c.XpEarned);
            return;
        }
        Assert.Fail("No victory in 20 fights.");
    }

    [Fact]
    public void Xp_table_extends_past_its_end()
    {
        var b = TestContent.Db.Balance;
        int last = b.LevelXp.Count;
        Assert.True(b.XpForLevel(last + 1) > b.XpForLevel(last));
        Assert.True(b.XpForLevel(last + 2) > b.XpForLevel(last + 1));
    }
}

public class EventTests
{
    private static Run MakeRun(int gold = 50)
    {
        var run = new Run(TestContent.Db, 11, new[] { "fighter", "rogue", "cleric" });
        run.Party.StartDepth(20);
        run.Party.Gold = gold;
        return run;
    }

    [Fact]
    public void Repo_has_events_and_every_one_can_be_picked()
    {
        Assert.True(TestContent.Db.Events.Count >= 5);
        var run = MakeRun();
        run.Depth = Run.FinalDepth;   // some events only appear from Depth 2
        var seen = new HashSet<string>();
        for (int i = 0; i < 300; i++) seen.Add(run.PickEvent()!.Id);
        Assert.Equal(TestContent.Db.Events.Count, seen.Count);
    }

    [Fact]
    public void Options_pay_their_cost_and_apply_outcomes()
    {
        var run = MakeRun(gold: 30);
        var well = TestContent.Db.Events["wishing_well"];
        int luck = run.Party.Luck;
        var result = run.ResolveEvent(well.Options[0]);   // toss a coin: -5 gold, +1 luck
        Assert.Equal(25, run.Party.Gold);
        Assert.Equal(luck + 1, run.Party.Luck);
        Assert.NotEmpty(result.Lines);
    }

    [Fact]
    public void Unaffordable_options_are_refused()
    {
        var run = MakeRun(gold: 0);
        var gambler = TestContent.Db.Events["gambler"];
        Assert.False(run.CanChoose(gambler.Options[0], out _));
        Assert.Throws<InvalidOperationException>(() => run.ResolveEvent(gambler.Options[0]));
    }

    [Fact]
    public void Checks_roll_and_hermit_asks_for_a_card()
    {
        var run = MakeRun();
        var shrine = TestContent.Db.Events["shrine"];
        var pray = run.ResolveEvent(shrine.Options[0]);
        Assert.NotNull(pray.Roll);
        Assert.NotNull(pray.Roller);

        var hermit = TestContent.Db.Events["hermit"];
        var give = run.ResolveEvent(hermit.Options[0]);
        Assert.True(give.RemoveCard);
        var hero = run.Party.Heroes[0];
        int size = hero.Deck.AllCards.Count();
        Assert.True(run.RemoveCardFree(hero, hero.Deck.Hand[0]));
        Assert.Equal(size - 1, hero.Deck.AllCards.Count());
    }

    [Fact]
    public void Event_damage_never_downs_a_hero()
    {
        var run = MakeRun();
        foreach (var h in run.Party.Heroes) h.Hp = 1;
        var camp = TestContent.Db.Events["abandoned_camp"];
        for (int i = 0; i < 20; i++) run.ResolveEvent(camp.Options[0]);
        Assert.All(run.Party.Heroes, h => Assert.True(h.Hp >= 1 && !h.IsDowned));
    }
}

public class MerchantPlacementTests
{
    [Fact]
    public void Every_depth_has_one_merchant_on_the_map()
    {
        for (ulong seed = 1; seed <= 60; seed++)
            for (int depth = 1; depth <= 3; depth++)
            {
                var g = DungeonGenerator.Generate(seed, new DungeonParams { Depth = depth });
                Assert.Single(g.Rooms, t => t.Kind == TileKind.Merchant);
            }
    }

    [Fact]
    public void Merchant_tile_can_be_turned_off()
    {
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var g = DungeonGenerator.Generate(seed, new DungeonParams { Merchant = false });
            Assert.DoesNotContain(g.Rooms, t => t.Kind == TileKind.Merchant);
        }
    }

    [Fact]
    public void Merchant_icon_shows_two_rooms_away()
    {
        var t = new Tile(0, 0) { Kind = TileKind.Merchant };
        Assert.Equal(TileKind.Merchant, Visibility.VisibleKind(t, Vis.Far));
    }
}
