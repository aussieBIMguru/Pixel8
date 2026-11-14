using Pixel8.Core.Achievements;
using Pixel8.Core.Cards;
using Pixel8.Core.Combat;
using Pixel8.Core.Content;
using Pixel8.Core.Dice;
using Pixel8.Core.Gear;
using Pixel8.Core.Heroes;
using Pixel8.Core.Lore;
using Pixel8.Core.Randomness;
using Pixel8.Core.Runs;

namespace Pixel8.Tests;

/// <summary>Round 11 helpers.</summary>
internal static class R11
{
    public static ContentDb Db => TestContent.Db;

    /// <summary>Resolves enemy turns (no reactions, Miss on timing, no Luck) until a hero's turn or the end.</summary>
    public static void ToHeroTurn(CombatState c, int max = 500)
    {
        for (int i = 0; i < max && !c.IsOver && c.Phase != CombatPhase.HeroTurn; i++)
        {
            if (c.Phase == CombatPhase.AwaitingReaction) c.ResolveReaction(null);
            else if (c.Phase == CombatPhase.AwaitingTiming) c.ResolveTiming(TimingGrade.Miss);
            else if (c.Phase == CombatPhase.AwaitingLuck) c.ResolveLuck(false);
        }
    }

    public static void EndTurn(CombatState c)
    {
        c.EndTurn();
        ToHeroTurn(c);
    }

    public static CombatState Fight(ulong seed, string[] heroes, string[] enemies, CombatKind kind = CombatKind.Normal)
    {
        var c = TestContent.MakeFight(seed, heroes, enemies, kind);
        c.Invulnerable = true;
        c.LuckPrompts = false;
        c.Start();
        ToHeroTurn(c);
        return c;
    }
}

/// <summary>Round 11: Bard, Ranger, Druid, Paladin and their new effects.</summary>
public class NewClassTests
{
    private static readonly string[] NewClasses = { "bard", "ranger", "druid", "paladin" };

    [Fact]
    public void The_new_classes_load_with_basics_decks_and_card_pools()
    {
        var db = R11.Db;
        Assert.Empty(db.Validate());
        foreach (var id in NewClasses)
        {
            var cls = db.HeroClass(id);
            Assert.Equal(6, cls.StartingDeck!.Count);
            Assert.Equal("basic", db.Card(cls.BasicAction!).ClassId);
            var pool = db.Cards.Values.Where(c => c.ClassId == id).ToList();
            Assert.True(pool.Count(c => c.Rarity == Rarity.Common) >= 3, id);
            Assert.Equal(5, pool.Count(c => c.Rarity == Rarity.Uncommon));
            Assert.Equal(3, pool.Count(c => c.Rarity == Rarity.Rare));
            Assert.All(pool, c => Assert.NotNull(c.Upgrade));
        }
        Assert.Equal(3, db.Card("basic.bardic_inspiration").Uses);   // round 12: 2 -> 3
    }

    [Fact]
    public void Ranged_weapons_use_dex()
    {
        var run = new Run(R11.Db, 1, new[] { "ranger", "fighter" });
        var ranger = run.Party.Heroes[0];
        Assert.Equal(DiceExpr.Parse("1d8+3"), ranger.Weapon);
        Assert.Equal(5, ranger.AttackMod);

        var fighter = run.Party.Heroes[1];
        var bow = run.NewGear("longbow");
        run.TakeLoot(bow);
        run.Equip(fighter, bow);
        Assert.Equal(DiceExpr.Parse("1d8+1"), fighter.Weapon);   // Fighter DEX +1
        Assert.Equal(3, fighter.AttackMod);
    }

    [Fact]
    public void Bardic_inspiration_is_three_times_per_fight()
    {
        var c = R11.Fight(1, new[] { "bard" }, new[] { "goblin" });
        var bard = c.Heroes[0];
        var def = R11.Db.Card("basic.bardic_inspiration");
        c.UseBasic(def.Id, bard);
        Assert.Equal(1, c.InspirationOf(bard));
        Assert.Equal(2, c.BasicUsesLeft(bard, def));
        Assert.False(c.CanUseBasic(def));   // Quick already used

        R11.EndTurn(c);
        c.UseBasic(def.Id, bard);
        Assert.Equal(1, c.BasicUsesLeft(bard, def));
        R11.EndTurn(c);
        c.UseBasic(def.Id, bard);
        Assert.Equal(0, c.BasicUsesLeft(bard, def));
        R11.EndTurn(c);
        Assert.False(c.CanUseBasic(def));
    }

    [Fact]
    public void Inspiration_turns_a_near_miss_into_a_hit_and_is_kept_on_a_hit()
    {
        var c = R11.Fight(2, new[] { "bard" }, new[] { "goblin" });
        var bard = c.Heroes[0];
        var goblin = c.Enemies[0];
        int needed = goblin.Ac - c.AttackModOf(bard, spell: false);   // natural roll that just hits

        c.Inspire(bard, DiceExpr.Parse("1d6"), 1);
        c.FixedNextDie = needed - 1;                                    // misses by one: any d6 saves it
        c.UseBasic(ContentDb.BasicStrikeId, goblin);
        var used = c.Events.OfType<InspirationUsed>().Single();
        Assert.True(used.Turned);
        Assert.Equal(0, c.InspirationOf(bard));
        Assert.Contains("bard.encore", c.Feats);

        R11.EndTurn(c);
        if (!goblin.IsAlive) return;
        c.Inspire(bard, DiceExpr.Parse("1d6"), 1);
        c.FixedNextDie = 19;                                            // a hit without help
        c.UseBasic(ContentDb.BasicStrikeId, goblin);
        Assert.Equal(1, c.InspirationOf(bard));
    }

    [Fact]
    public void Divine_smite_lands_on_the_next_hit_only_with_extra_against_undead()
    {
        var c = R11.Fight(3, new[] { "paladin" }, new[] { "zombie" });
        var paladin = c.Heroes[0];
        var zombie = c.Enemies[0];
        c.PrimeSmite(paladin, DiceExpr.Parse("2d8"), DiceExpr.Parse("1d8"));
        Assert.True(c.IsSmiting(paladin));
        c.FixedNextDie = 19;
        c.UseBasic(ContentDb.BasicStrikeId, zombie);
        Assert.Single(c.Events.OfType<Smote>());
        Assert.False(c.IsSmiting(paladin));
        // Weapon, 2d8 smite, 1d8 undead.
        Assert.Equal(2, c.Events.OfType<DamageRolled>().Count(d => d.Type == "radiant"));
        Assert.DoesNotContain("paladin.smite_crit", c.Feats);
    }

    [Fact]
    public void A_critical_smite_is_a_feat()
    {
        var c = R11.Fight(4, new[] { "paladin" }, new[] { "ogre" }, CombatKind.Boss);
        c.PrimeSmite(c.Heroes[0], DiceExpr.Parse("2d8"), DiceExpr.Parse("1d8"));
        c.FixedNextDie = 20;
        c.UseBasic(ContentDb.BasicStrikeId, c.Enemies[0]);
        Assert.Contains("paladin.smite_crit", c.Feats);
    }

    [Fact]
    public void Hunters_mark_adds_dice_and_moves_when_its_target_dies()
    {
        var c = R11.Fight(5, new[] { "ranger" }, new[] { "goblin", "goblin" });
        var ranger = c.Heroes[0];
        var (first, second) = (c.Enemies[0], c.Enemies[1]);
        c.UseBasic("basic.hunters_mark", first);
        Assert.True(first.Conditions.Has(Cond.Hunted));

        c.DealDamage(ranger, first, 999, "test");
        Assert.Equal(1, c.HuntedKills(ranger));
        Assert.True(second.Conditions.Has(Cond.Hunted));

        c.FixedNextDie = 19;
        c.UseBasic(ContentDb.BasicStrikeId, second);
        Assert.Single(c.Events.OfType<DamageRolled>().Where(d => d.Type == "hunt"));
        Assert.False(c.CanUseBasic(R11.Db.Card("basic.hunters_mark")));   // once per fight
    }

    [Fact]
    public void Wild_shape_soaks_with_temp_hp_strikes_as_the_beast_and_ends()
    {
        var c = R11.Fight(6, new[] { "druid" }, new[] { "goblin" });
        c.Invulnerable = false;
        var druid = c.Heroes[0];
        var goblin = c.Enemies[0];
        c.WildShape(druid, new WildForm("wolf", "Wolf", DiceExpr.Parse("2d4+2"), 8));
        Assert.Equal(8, druid.TempHp);
        Assert.Equal(DiceExpr.Parse("2d4+2"), c.WeaponOf(druid));
        Assert.True(druid.Conditions.Has(Cond.Wild));

        int hp = druid.Hp;
        c.DealDamage(goblin, druid, 5, "test");
        Assert.Equal(hp, druid.Hp);
        Assert.Equal(3, druid.TempHp);
        c.DealDamage(goblin, druid, 6, "test");
        Assert.Equal(hp - 3, druid.Hp);
        Assert.Null(c.FormOf(druid));
        Assert.Single(c.Events.OfType<WildShapeEnded>());
    }

    [Fact]
    public void Winning_in_wild_shape_is_a_feat_and_temp_hp_ends_with_the_fight()
    {
        var c = R11.Fight(7, new[] { "druid" }, new[] { "goblin" });
        var druid = c.Heroes[0];
        c.WildShape(druid, new WildForm("bear", "Brown Bear", DiceExpr.Parse("2d6+3"), 15));
        c.DebugWin();
        Assert.Contains("druid.wild_win", c.Feats);
        Assert.Equal(0, druid.TempHp);
    }

    [Fact]
    public void Lay_on_hands_heals_from_a_pool()
    {
        var c = R11.Fight(8, new[] { "paladin" }, new[] { "goblin" });
        var paladin = c.Heroes[0];
        var def = R11.Db.Card("basic.lay_on_hands");
        paladin.Hp = 10;
        Assert.Equal(R11.Db.Balance.LayOnHandsBase, c.BasicUsesLeft(paladin, def));
        c.UseBasic(def.Id, paladin);
        Assert.Equal(10 + R11.Db.Balance.LayOnHandsBase, paladin.Hp);
        Assert.Equal(0, c.BasicUsesLeft(paladin, def));
        R11.EndTurn(c);
        Assert.False(c.CanUseBasic(def));
    }
}

/// <summary>Round 11: conditions and the deep-Depth monsters.</summary>
public class DeepDepthTests
{
    [Fact]
    public void A_paralyzed_enemy_loses_its_turn()
    {
        var c = R11.Fight(10, new[] { "bard" }, new[] { "goblin" });
        var goblin = c.Enemies[0];
        c.ApplyCondition(goblin, Cond.Paralyzed, 1);
        int before = c.Events.Count;
        R11.EndTurn(c);
        var since = c.Events.Skip(before).ToList();
        Assert.Contains(since, e => e is TurnSkipped t && ReferenceEquals(t.Actor, goblin));
        Assert.DoesNotContain(since, e => e is CheckRolled r && ReferenceEquals(r.Roller, goblin));
        Assert.False(goblin.Conditions.Has(Cond.Paralyzed));
    }

    [Fact]
    public void A_paralyzed_hero_loses_their_next_turn()
    {
        var c = R11.Fight(11, new[] { "fighter" }, new[] { "goblin" });
        var fighter = c.Heroes[0];
        Assert.Equal(1, c.Round);
        c.ApplyCondition(fighter, Cond.Paralyzed, 1);
        R11.EndTurn(c);
        Assert.Equal(3, c.Round);
        Assert.Contains(c.Events, e => e is TurnSkipped t && ReferenceEquals(t.Actor, fighter));
    }

    [Fact]
    public void Restrained_and_outlined_foes_are_easier_to_hit()
    {
        var c = R11.Fight(12, new[] { "fighter" }, new[] { "goblin" });
        var goblin = c.Enemies[0];
        Assert.Equal(RollMode.Normal, c.PreviewAttack(c.Heroes[0], goblin).Mode);
        c.ApplyCondition(goblin, Cond.Restrained, 1);
        Assert.Equal(RollMode.Advantage, c.PreviewAttack(c.Heroes[0], goblin).Mode);
        goblin.Conditions.Remove(Cond.Restrained);
        c.ApplyCondition(goblin, Cond.Outlined, 1);
        Assert.Equal(RollMode.Advantage, c.PreviewAttack(c.Heroes[0], goblin).Mode);
    }

    [Fact]
    public void The_troll_regenerates_unless_burned()
    {
        var c = R11.Fight(13, new[] { "fighter" }, new[] { "troll" });
        var fighter = c.Heroes[0];
        var troll = c.Enemies[0];
        int regen = troll.Def.Regen;
        c.DealDamage(fighter, troll, 20, "weapon");
        int hp = troll.Hp;
        R11.EndTurn(c);
        Assert.Equal(hp + regen, troll.Hp);

        c.DealDamage(fighter, troll, 10, "fire");
        hp = troll.Hp;
        R11.EndTurn(c);
        Assert.Equal(hp, troll.Hp);
        R11.EndTurn(c);
        Assert.Equal(hp + regen, troll.Hp);
    }

    [Fact]
    public void The_hydra_bites_once_per_head_loses_heads_and_regrows_them_unless_burned()
    {
        var c = R11.Fight(14, new[] { "fighter" }, new[] { "hydra" }, CombatKind.Boss);
        var fighter = c.Heroes[0];
        var hydra = c.Enemies[0];
        Assert.Equal(5, c.HeadsOf(hydra));
        c.DealDamage(fighter, hydra, hydra.Def.SeverAt, "weapon");
        Assert.Equal(4, c.HeadsOf(hydra));
        c.DealDamage(fighter, hydra, hydra.Def.SeverAt - 1, "weapon");
        Assert.Equal(4, c.HeadsOf(hydra));
        c.DealDamage(fighter, hydra, 1, "fire");

        bool bites = c.IntentFor(hydra)!.Intent.PerHead;
        int before = c.Events.Count;
        R11.EndTurn(c);
        Assert.Equal(4, c.HeadsOf(hydra));   // burned: no regrowth this turn
        int attacks = c.Events.Skip(before).OfType<CheckRolled>().Count(r => ReferenceEquals(r.Roller, hydra));
        Assert.Equal(bites ? 4 : 0, attacks);

        R11.EndTurn(c);
        Assert.Equal(5, c.HeadsOf(hydra));
    }

    [Fact]
    public void The_lich_acts_again_each_round_and_raises_the_dead_once()
    {
        var c = R11.Fight(15, new[] { "fighter" }, new[] { "lich" }, CombatKind.Boss);
        var fighter = c.Heroes[0];
        var lich = c.Enemies[0];
        Assert.NotNull(c.LegendaryIntentFor(lich));
        R11.EndTurn(c);
        Assert.Contains(c.Events, e => e is LegendaryTurn);

        c.DealDamage(fighter, lich, lich.Hp - lich.MaxHp / 2 + 1, "weapon");
        var summoned = Assert.Single(c.Events.OfType<EnemiesSummoned>());
        Assert.Equal(2, summoned.Summoned.Count);
        Assert.All(summoned.Summoned, z => Assert.Equal("zombie", z.Def.Id));
        Assert.Equal(3, c.Enemies.Count(e => e.IsAlive));
        Assert.All(summoned.Summoned, z => Assert.NotNull(c.IntentFor(z)));

        c.DealDamage(fighter, lich, 1, "weapon");
        Assert.Single(c.Events.OfType<EnemiesSummoned>());
    }

    [Fact]
    public void Summons_reuse_fallen_places_and_never_pass_four()
    {
        var c = R11.Fight(16, new[] { "fighter" }, new[] { "lich", "goblin", "goblin" }, CombatKind.Boss);
        var fighter = c.Heroes[0];
        c.DealDamage(fighter, c.Enemies[1], 999, "test");
        var lich = c.Enemies[0];
        c.DealDamage(fighter, lich, lich.Hp - lich.MaxHp / 2 + 1, "weapon");
        Assert.True(c.Enemies.Count <= CombatState.MaxEnemies);
        Assert.True(c.Enemies.Count(e => e.IsAlive) <= CombatState.MaxEnemies);
        Assert.Contains(c.AllEnemies, e => !e.IsAlive && e.Def.Id == "goblin");
        c.DebugWin();
        Assert.Equal(c.AllEnemies.Sum(e => R11.Db.Balance.XpOf(e.Def)), c.XpEarned);
    }

    [Fact]
    public void Deep_depths_use_the_new_roster_and_bosses()
    {
        var db = R11.Db;
        Assert.Equal(5, Run.FinalDepth);
        Assert.Equal(new[] { "hydra" }, Encounters.BossesFor(db, 4).Select(e => e.Id));
        Assert.Equal(new[] { "lich" }, Encounters.BossesFor(db, 5).Select(e => e.Id));
        var rng = new Rng(21);
        for (int i = 0; i < 100; i++)
        {
            foreach (int depth in new[] { 4, 5 })
            {
                var normal = Encounters.Roll(db, CombatKind.Normal, depth, rng);
                Assert.All(normal, e => Assert.InRange(depth, e.Def.MinDepth, e.Def.MaxDepth));
                Assert.DoesNotContain(normal, e => e.Def.Id is "kobold" or "giant_rat" or "goblin");
                var boss = Encounters.Roll(db, CombatKind.Boss, depth, rng);
                Assert.True(boss.Count <= 1 + (boss[0].Def.MaxAdds ?? 3));
                var elite = Encounters.Roll(db, CombatKind.Elite, depth, rng);
                Assert.True(elite.Count <= 4);
            }
            Assert.Equal("vampire", Encounters.Roll(db, CombatKind.Elite, 5, rng)[0].Def.Id);
        }
    }

    [Fact]
    public void Every_per_depth_list_covers_five_depths()
    {
        var b = R11.Db.Balance;
        Assert.Equal(5, b.AcCapByDepth.Count);
        Assert.Equal(5, b.NormalGroupByDepth.Count);
        Assert.Equal(5, b.EliteAddsByDepth.Count);
        Assert.Equal(5, b.BossAddsByDepth.Count);
        Assert.Equal(5, b.TorchMultiplierByDepth.Count);
        Assert.Equal(5, b.Gear.MimicChance.Count);
        foreach (var source in new[] { "treasure", "elite", "boss", "mimic", "merchant", "event" })
            Assert.Equal(5, b.Gear.Table(source).RarityByDepth.Count);
        Assert.Contains(R11.Db.Events.Values, e => e.MinDepth <= 5);
    }

    [Fact]
    public void A_run_can_be_saved_and_loaded_on_depth_five()
    {
        var run = new Run(R11.Db, 3, new[] { "bard", "druid", "paladin" }) { Depth = 5 };
        var back = Run.FromSave(R11.Db, run.ToSave());
        Assert.Equal(5, back.Depth);
        Assert.Equal(new[] { "bard", "druid", "paladin" }, back.Party.Heroes.Select(h => h.Class.Id));
    }
}

/// <summary>Round 11: icons, class feats, the Lich and Hydra trophies, and the Adventurer's Journal.</summary>
public class RoundElevenProgressTests
{
    [Fact]
    public void Achievements_have_icons_and_every_feat_maps_to_one()
    {
        Assert.Equal(35, AchievementList.All.Count);
        Assert.All(AchievementList.All, a => Assert.False(string.IsNullOrEmpty(a.Icon)));
        foreach (var id in AchievementList.FeatAchievements.Values)
            Assert.Contains(AchievementList.All, a => a.Id == id);
        Assert.Equal(8, AchievementList.Get("tour_guide").Target);
    }

    [Fact]
    public void A_feat_in_a_won_fight_unlocks_its_achievement()
    {
        var tracker = new AchievementTracker(new AchievementState());
        var run = new Run(R11.Db, 4, new[] { "paladin" }) { Timing = TimingMode.Off };
        run.Party.StartDepth(20);
        tracker.RunStarted(run);
        var fight = run.StartCombat(CombatKind.Normal);
        fight.LuckPrompts = false;
        fight.Start();
        R11.ToHeroTurn(fight);
        var target = fight.Enemies.First(e => e.IsAlive);
        target.MaxHp = target.Hp = 300;   // the crit mustn't kill it before the smite lands
        fight.PrimeSmite(fight.Heroes[0], DiceExpr.Parse("2d8"), DiceExpr.Parse("1d8"));
        fight.FixedNextDie = 20;
        fight.UseBasic(ContentDb.BasicStrikeId, target);
        fight.DebugWin();
        Assert.Contains(tracker.FightEnded(run, fight, mimic: false), a => a.Id == "feat_paladin");
    }

    [Fact]
    public void The_journal_notes_what_a_run_shows_and_what_falls()
    {
        var journal = new JournalTracker(new JournalState());
        var run = new Run(R11.Db, 5, new[] { "ranger", "bard", "cleric" }) { Timing = TimingMode.Off };
        run.Party.StartDepth(20);
        run.Noticed = journal.Note;

        journal.Observe(run);
        Assert.Contains("basic.hunters_mark", journal.State.Cards);
        Assert.Contains("longbow", journal.State.Gear);
        Assert.Contains(ContentDb.BasicStrikeId, journal.State.Cards);

        var draft = run.CreateDraft();
        Assert.All(draft.Offers, o => Assert.Contains(o.Card.Id, journal.State.Cards));

        var fight = run.StartCombat(CombatKind.Normal);
        Assert.All(fight.Enemies, e => Assert.Contains(e.Def.Id, journal.State.Enemies));
        fight.Start();
        fight.DebugWin();
        journal.FightEnded(run, fight);
        Assert.All(fight.Enemies, e => Assert.True(journal.DefeatedCount(e.Def.Id) > 0));
        Assert.True(journal.Dirty);
    }

    [Fact]
    public void Debug_runs_add_nothing_to_the_journal()
    {
        var journal = new JournalTracker(new JournalState());
        var run = new Run(R11.Db, 6, new[] { "druid" }) { Debugged = true };
        run.Noticed = journal.Note;
        journal.Observe(run);
        run.CreateDraft();
        Assert.Empty(journal.State.Cards);
        Assert.Empty(journal.State.Gear);
    }
}
