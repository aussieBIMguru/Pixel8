using System.Text.Json;
using Pixel8.Core.Achievements;
using Pixel8.Core.Cards;
using Pixel8.Core.Combat;
using Pixel8.Core.Content;
using Pixel8.Core.Dice;
using Pixel8.Core.Dungeon;
using Pixel8.Core.Gear;
using Pixel8.Core.Heroes;
using Pixel8.Core.Randomness;
using Pixel8.Core.Rewards;
using Pixel8.Core.Runs;

namespace Pixel8.Tests;

/// <summary>Round 10: gear, loot, mimics, the merchant's gear, gear events.</summary>
public class GearTests
{
    private static readonly string[] All4 = { "fighter", "rogue", "wizard", "cleric" };
    private static readonly string[] Trio = { "fighter", "rogue", "wizard" };

    private static Run MakeRun(ulong seed = 7, string[]? classes = null, int depth = 1)
    {
        var run = new Run(TestContent.Db, seed, classes ?? Trio) { Depth = depth, Timing = TimingMode.Off };
        run.Party.StartDepth(20);
        return run;
    }

    private static Hero HeroOf(Run run, string cls) => run.Party.Heroes.First(h => h.Class.Id == cls);

    [Fact]
    public void Gear_data_loads_and_every_class_can_use_its_starting_gear()
    {
        Assert.Empty(TestContent.Db.Validate());
        Assert.InRange(TestContent.Db.Gear.Count, 38, 60);
        foreach (var slot in Enum.GetValues<GearSlot>())
            Assert.Contains(TestContent.Db.Gear.Values, g => g.Slot == slot && g.Rarity == Rarity.Rare);
    }

    [Fact]
    public void Starting_gear_reproduces_the_old_fixed_stats()
    {
        // Round 11: all eight classes (the new four were built to the same rule).
        var party = Party.Create(TestContent.Db, TestContent.Db.Heroes.Keys, new Rng(1));
        Assert.Equal(8, party.Heroes.Count);
        foreach (var h in party.Heroes)
        {
            Assert.True(h.UsesGear);
            Assert.Equal(h.Class.Ac, h.Ac);
            Assert.Equal(h.Class.AttackMod, h.AttackMod);
            Assert.Equal(DiceExpr.Parse(h.Class.Weapon), h.Weapon);
            Assert.Equal(h.Class.SpellDc, h.SpellDc);
            Assert.Equal(h.Class.ConSave, h.ConSave);
            Assert.Equal(0, h.GearHp);
        }
    }

    [Fact]
    public void Finesse_uses_dex_and_proficiency_limits_who_can_equip()
    {
        var run = MakeRun();
        var rogue = HeroOf(run, "rogue");
        var wizard = HeroOf(run, "wizard");
        var rapier = run.NewGear("rapier");
        run.TakeLoot(rapier);
        run.Equip(rogue, rapier);
        Assert.Equal(new DiceExpr(1, 8, 3), rogue.Weapon);
        Assert.Contains(run.Party.Pack, g => g.Def.Id == "shortsword");

        var chain = run.NewGear("chain_mail");
        run.TakeLoot(chain);
        Assert.False(run.CanEquip(wizard, chain, out string why));
        Assert.Contains("can't use", why);
    }

    [Fact]
    public void Two_handed_weapons_free_the_off_hand_and_block_shields()
    {
        var run = MakeRun();
        var fighter = HeroOf(run, "fighter");
        var shield = run.NewGear("shield");
        run.TakeLoot(shield);
        run.Equip(fighter, shield);
        Assert.Equal(18, fighter.Ac);

        var greatsword = run.NewGear("greatsword");
        run.TakeLoot(greatsword);
        run.Equip(fighter, greatsword);
        Assert.Null(fighter.Loadout[GearSlot.OffHand]);
        Assert.Contains(shield, run.Party.Pack);
        Assert.Equal(16, fighter.Ac);
        Assert.False(run.CanEquip(fighter, shield, out _));
    }

    [Fact]
    public void Max_hp_gear_heals_on_equip_and_cursed_gear_sticks_until_a_campfire()
    {
        var run = MakeRun();
        var fighter = HeroOf(run, "fighter");
        int max = fighter.MaxHp;
        var amulet = run.NewGear("amulet_of_health");
        run.TakeLoot(amulet);
        run.Equip(fighter, amulet);
        Assert.Equal(max + 8, fighter.MaxHp);
        Assert.Equal(fighter.MaxHp, fighter.Hp);

        var axe = run.NewGear("berserker_axe");
        run.TakeLoot(axe);
        run.Equip(fighter, axe);
        Assert.Equal(max + 4, fighter.MaxHp);
        Assert.False(run.CanUnequip(fighter, GearSlot.Weapon, out string why));
        Assert.Contains("cursed", why);
        run.AtCampfire = true;
        run.Unequip(fighter, GearSlot.Weapon);
        Assert.Equal(max + 8, fighter.MaxHp);
    }

    [Fact]
    public void A_full_pack_refuses_loot_and_unequipping()
    {
        var run = MakeRun();
        while (!run.Party.PackFull) run.TakeLoot(run.NewGear("dagger"));
        Assert.False(run.TakeLoot(run.NewGear("dagger")));
        Assert.False(run.CanUnequip(HeroOf(run, "fighter"), GearSlot.Armor, out _));
    }

    [Fact]
    public void Boss_and_mimic_loot_offer_choices_of_uncommon_or_better()
    {
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var run = MakeRun(seed);
            var boss = run.RollLoot(LootSource.Boss)!;
            Assert.Equal(2, boss.Choices.Count);
            Assert.All(boss.Choices, g => Assert.True(g.Def.Rarity >= Rarity.Uncommon));
            Assert.Equal(boss.Choices.Count, boss.Choices.Select(g => g.Def.Id).Distinct().Count());
            Assert.Contains(boss.Choices, g => run.IsUsableByParty(g.Def));

            int gold = run.Party.Gold;
            var mimic = run.RollLoot(LootSource.Mimic)!;
            Assert.Equal(3, mimic.Choices.Count);
            Assert.Equal(40, mimic.Gold);
            Assert.Equal(gold + 40, run.Party.Gold);
            Assert.DoesNotContain(mimic.Choices, g => !g.Def.Loot);
        }
    }

    [Fact]
    public void Mimics_hide_only_in_unexposed_treasure_and_never_move_the_layout()
    {
        var plain = DungeonGenerator.Generate(99, new DungeonParams { Depth = 2 });
        var all = DungeonGenerator.Generate(99, new DungeonParams { Depth = 2, MimicChance = 1 });
        Assert.Equal(plain.Rooms.Select(t => (t.X, t.Y, t.Kind, t.Exposed)), all.Rooms.Select(t => (t.X, t.Y, t.Kind, t.Exposed)));
        Assert.All(all.Rooms.Where(t => t.Kind == TileKind.Treasure), t => Assert.Equal(!t.Exposed, t.Mimic));
        Assert.DoesNotContain(plain.Rooms, t => t.Mimic);
        Assert.False(TestContent.Db.Enemy("mimic").Random);
    }

    [Fact]
    public void The_merchant_sells_usable_gear_and_buys_it_back_at_half_price()
    {
        var run = MakeRun();
        var m = run.CreateMerchant();
        var gear = m.Offers.Where(o => o.Kind == ShopKind.Gear).ToList();
        Assert.Equal(3, gear.Count);
        Assert.True(gear.Count(o => run.IsUsableByParty(o.Gear!.Def)) >= 2);

        run.Party.Gold = 1000;
        m.Buy(run.Party, gear[0], run.LootRng);
        var bought = run.Party.Pack.Single();
        int price = run.SellPrice(bought);
        Assert.Equal((int)Math.Round(gear[0].Price * 0.5), price);
        Merchant.Sell(run.Party, bought, price);
        Assert.Empty(run.Party.Pack);
        Assert.Equal(1000 - gear[0].Price + price, run.Party.Gold);
        Assert.Equal(gear[0].Price, run.Party.GoldSpent);
    }

    [Fact]
    public void Gear_and_pack_survive_save_and_load()
    {
        var run = MakeRun();
        var fighter = HeroOf(run, "fighter");
        var amulet = run.NewGear("amulet_of_health");
        run.TakeLoot(amulet);
        run.Equip(fighter, amulet);
        run.TakeLoot(run.NewGear("rapier", bonus: 1));
        run.Debugged = true;

        string json = JsonSerializer.Serialize(run.ToSave(), ContentDb.JsonOptions);
        var back = Run.FromSave(TestContent.Db, JsonSerializer.Deserialize<RunSave>(json, ContentDb.JsonOptions)!);
        var f2 = HeroOf(back, "fighter");
        Assert.Equal(fighter.MaxHp, f2.MaxHp);
        Assert.Equal("amulet_of_health", f2.Loadout[GearSlot.Trinket]!.Def.Id);
        Assert.Equal(fighter.Ac, f2.Ac);
        Assert.Equal("Rapier +1", back.Party.Pack.Single().Name);
        Assert.True(back.Debugged);
    }

    [Fact]
    public void The_smith_improves_the_weakest_weapon_up_to_plus_two()
    {
        var run = MakeRun(classes: new[] { "fighter" });
        var fighter = run.Party.Heroes[0];
        run.ImproveBestGear(out var first);
        Assert.Equal(GearSlot.Weapon, first!.Def.Slot);
        Assert.Equal(new DiceExpr(1, 8, 4), fighter.Weapon);
        Assert.Equal(6, fighter.AttackMod);
        run.ImproveBestGear(out var second);
        Assert.Equal(GearSlot.Armor, second!.Def.Slot);
        Assert.Equal(17, fighter.Ac);
    }

    [Fact]
    public void Gear_events_hand_out_loot()
    {
        var run = MakeRun();
        var rack = TestContent.Db.Events["armory_rack"];
        var result = run.ResolveEvent(rack.Options[0]);
        Assert.NotNull(result.Loot);
        Assert.Equal(3, result.Loot!.Choices.Count);
        Assert.All(result.Loot.Choices, g => Assert.Equal(Rarity.Common, g.Def.Rarity));

        var axe = run.ResolveEvent(TestContent.Db.Events["cursed_blade"].Options[0]);
        Assert.True(axe.Loot!.Choices.Single().Def.Cursed);
    }

    [Fact]
    public void Gear_bonuses_reach_combat()
    {
        var party = TestContent.MakeParty(3, "wizard");
        var wizard = party.Heroes[0];
        wizard.SetSlotForTest(GearSlot.OffHand, new GearInstance(TestContent.Db.GearItem("crystal_orb")));
        wizard.SetSlotForTest(GearSlot.Trinket, new GearInstance(TestContent.Db.GearItem("stone_of_good_luck")));
        var c = new CombatState(party, new[] { Encounters.Spawn(TestContent.Db, "goblin") }, TestContent.Db, new Rng(3), CombatKind.Normal,
            TestContent.Db.Balance.DifficultyOf("normal"));
        c.Start();
        Assert.Equal(2, c.FightLuck);   // Normal 1 + Stone of Good Luck
        Assert.Contains(c.Events, e => e is BlockGained bg && ReferenceEquals(bg.Target, wizard) && bg.Amount == 2);
    }

    [Fact]
    public void Flame_tongue_adds_fire_on_every_hit()
    {
        bool sawFire = false;
        for (ulong seed = 1; seed <= 30 && !sawFire; seed++)
        {
            var party = TestContent.MakeParty(seed, "fighter");
            party.Heroes[0].SetSlotForTest(GearSlot.Weapon, new GearInstance(TestContent.Db.GearItem("flame_tongue")));
            var c = new CombatState(party, new[] { Encounters.Spawn(TestContent.Db, "ogre") }, TestContent.Db, new Rng(seed), CombatKind.Boss);
            c.Enemies[0].Ac = 1;
            c.Start();
            c.UseBasic(ContentDb.BasicStrikeId, c.Enemies[0]);
            sawFire = c.Events.OfType<DamageDealt>().Any(d => d.Type == "fire");
        }
        Assert.True(sawFire);
    }
}

/// <summary>Round 10: locally tracked achievements.</summary>
public class AchievementTests
{
    private static Run MakeRun(string difficulty = "normal", ulong seed = 4)
    {
        var run = new Run(TestContent.Db, seed, new[] { "fighter", "rogue", "wizard" }, difficulty) { Timing = TimingMode.Off };
        run.Party.StartDepth(20);
        return run;
    }

    [Fact]
    public void Every_achievement_has_a_unique_id_and_the_requested_ones_exist()
    {
        Assert.Equal(AchievementList.All.Count, AchievementList.All.Select(a => a.Id).Distinct().Count());
        foreach (var name in new[] { "Enter the Dungeon", "One Small Step", "Piece of Cake", "As Intended", "Up for a Challenge",
                     "Moneybags", "Armed to the Teeth", "Ooo Shiny", "That's No Chest" })
            Assert.Contains(AchievementList.All, a => a.Name == name);
    }

    [Fact]
    public void Starting_and_winning_unlock_the_simple_ones()
    {
        var tracker = new AchievementTracker(new AchievementState());
        var run = MakeRun();
        Assert.Contains(tracker.RunStarted(run), a => a.Id == "enter_dungeon");
        Assert.Empty(tracker.RunStarted(run));   // only once

        var fight = run.StartCombat(CombatKind.Normal);
        fight.Start();
        fight.DebugWin();
        Assert.Contains(tracker.FightEnded(run, fight, mimic: true), a => a.Id == "first_win");
        Assert.True(tracker.State.Has("mimic"));
    }

    [Fact]
    public void Debug_runs_never_count()
    {
        var tracker = new AchievementTracker(new AchievementState());
        var run = MakeRun();
        run.Debugged = true;
        Assert.Empty(tracker.RunStarted(run));
        run.Party.TreasuresOpened = 5;
        Assert.Empty(tracker.RunEnded(run, won: true));
        Assert.Equal(0, tracker.State.Count(AchievementList.TreasureCounter));
    }

    [Fact]
    public void Counters_add_up_without_double_counting()
    {
        var tracker = new AchievementTracker(new AchievementState());
        var run = MakeRun();
        tracker.RunStarted(run);
        run.Party.TreasuresOpened = 3;
        tracker.Check(run);
        tracker.Check(run);
        Assert.Equal(3, tracker.State.Count(AchievementList.TreasureCounter));

        var next = MakeRun(seed: 5);
        tracker.RunStarted(next);
        next.Party.TreasuresOpened = 47;
        Assert.Contains(tracker.Check(next), a => a.Id == "treasure_hunter");
    }

    [Fact]
    public void Winning_unlocks_the_difficulty_and_counts_classes()
    {
        var tracker = new AchievementTracker(new AchievementState());
        var run = MakeRun("easy");
        tracker.RunStarted(run);
        run.Party.Gold = 1200;
        var news = tracker.RunEnded(run, won: true).Select(a => a.Id).ToList();
        Assert.Contains("beat_easy", news);
        Assert.Contains("moneybags", news);
        Assert.Contains("deathless", news);
        Assert.Contains("no_retreat", news);
        Assert.Equal(3, tracker.State.ClassesWon.Count);
        Assert.DoesNotContain("tour_guide", news);

        // Round 11: Tour Guide needs all eight classes.
        var clericRun = new Run(TestContent.Db, 9, new[] { "cleric", "rogue", "wizard" }, "hard");
        tracker.RunStarted(clericRun);
        Assert.DoesNotContain(tracker.RunEnded(clericRun, won: true), a => a.Id == "tour_guide");
        Assert.True(tracker.State.Has("beat_hard"));
        var newRun = new Run(TestContent.Db, 10, new[] { "bard", "ranger", "druid" });
        tracker.RunStarted(newRun);
        Assert.DoesNotContain(tracker.RunEnded(newRun, won: true), a => a.Id == "tour_guide");
        var paladinRun = new Run(TestContent.Db, 11, new[] { "paladin", "fighter", "bard" });
        tracker.RunStarted(paladinRun);
        Assert.Contains(tracker.RunEnded(paladinRun, won: true), a => a.Id == "tour_guide");
        Assert.Equal(8, tracker.State.ClassesWon.Count);
    }

    [Fact]
    public void A_fully_armed_party_and_rare_gear_unlock()
    {
        var tracker = new AchievementTracker(new AchievementState());
        var run = new Run(TestContent.Db, 2, new[] { "fighter" });
        tracker.RunStarted(run);
        var f = run.Party.Heroes[0];
        foreach (var id in new[] { "shield", "ring_of_protection" })
        {
            var g = run.NewGear(id);
            run.TakeLoot(g);
            run.Equip(f, g);
        }
        var news = tracker.Check(run).Select(a => a.Id).ToList();
        Assert.Contains("armed", news);
        Assert.Contains("shiny", news);
    }
}

internal static class HeroTestExtensions
{
    /// <summary>Equips straight into a slot for combat tests (Run.Equip is the checked path).</summary>
    public static void SetSlotForTest(this Hero hero, GearSlot slot, GearInstance gear)
    {
        typeof(Hero).GetMethod("SetSlot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(hero, new object?[] { slot, gear });
        hero.RecalculateGear();
    }
}
