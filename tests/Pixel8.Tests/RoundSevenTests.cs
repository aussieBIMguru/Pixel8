using System.Text.Json;
using Pixel8.Core.Cards;
using Pixel8.Core.Combat;
using Pixel8.Core.Content;
using Pixel8.Core.Randomness;
using Pixel8.Core.Rewards;
using Pixel8.Core.Runs;

namespace Pixel8.Tests;

/// <summary>Session 7: save/load, fx data, damage types for effects, faster levels.</summary>
public class SaveLoadTests
{
    private static readonly string[] Trio = { "fighter", "rogue", "cleric" };

    [Fact]
    public void Rng_state_round_trips()
    {
        var a = new Rng(42);
        for (int i = 0; i < 7; i++) a.Next(100);
        var b = new Rng(1);
        b.SetState(a.GetState());
        for (int i = 0; i < 20; i++) Assert.Equal(a.Next(1000), b.Next(1000));
    }

    private static Run PlayedRun()
    {
        var run = new Run(TestContent.Db, 77, Trio, "hard") { Timing = TimingMode.Off, Shop = ShopMode.Anytime };
        run.Party.StartDepth(15);
        for (int i = 0; i < 2; i++)
        {
            var c = run.StartCombat(CombatKind.Normal);
            if (AutoPlayer.RunToEnd(c) != CombatPhase.Victory) break;
            run.AwardXp(c.XpEarned);
        }
        run.AwardXp(run.NextLevelXp);   // leave level-ups pending
        run.Party.Bag.Add("potion_healing");
        run.Party.Gold = 123;
        run.Party.Heroes[1].Deck.AllCards.First(c => c.CanUpgrade).Upgraded = true;
        run.Depth = 2;
        return run;
    }

    private static Run RoundTrip(Run run)
    {
        string json = JsonSerializer.Serialize(run.ToSave(), ContentDb.JsonOptions);
        var save = JsonSerializer.Deserialize<RunSave>(json, ContentDb.JsonOptions)!;
        return Run.FromSave(TestContent.Db, save);
    }

    [Fact]
    public void A_run_survives_save_and_load()
    {
        var run = PlayedRun();
        var loaded = RoundTrip(run);

        Assert.Equal(run.Seed, loaded.Seed);
        Assert.Equal(run.DifficultyId, loaded.DifficultyId);
        Assert.Equal(2, loaded.Depth);
        Assert.Equal(TimingMode.Off, loaded.Timing);
        Assert.Equal(ShopMode.Anytime, loaded.Shop);
        Assert.Equal(123, loaded.Party.Gold);
        Assert.Equal(run.Party.Luck, loaded.Party.Luck);
        Assert.Equal(run.Party.Torch, loaded.Party.Torch);
        Assert.Equal(run.Party.Xp, loaded.Party.Xp);
        Assert.Equal(run.Party.Level, loaded.Party.Level);
        Assert.Equal(run.Party.Bag, loaded.Party.Bag);
        Assert.Equal(run.PendingLevelUps.Count, loaded.PendingLevelUps.Count);

        for (int i = 0; i < Trio.Length; i++)
        {
            var a = run.Party.Heroes[i];
            var b = loaded.Party.Heroes[i];
            Assert.Equal(a.Hp, b.Hp);
            Assert.Equal(a.MaxHp, b.MaxHp);
            Assert.Equal(a.IsDead, b.IsDead);
            Assert.Equal(a.Deck.Hand.Select(c => c.ToString()), b.Deck.Hand.Select(c => c.ToString()));
            Assert.Equal(a.Deck.DrawPile.Select(c => c.ToString()), b.Deck.DrawPile.Select(c => c.ToString()));
            Assert.Equal(a.Deck.Discard.Select(c => c.ToString()), b.Deck.Discard.Select(c => c.ToString()));
        }
        Assert.Contains(loaded.Party.Heroes[1].Deck.AllCards, c => c.Upgraded);
    }

    [Fact]
    public void Random_streams_continue_where_they_left_off()
    {
        var run = PlayedRun();
        var loaded = RoundTrip(run);
        for (int i = 0; i < 10; i++) Assert.Equal(run.LootRng.Next(1000), loaded.LootRng.Next(1000));

        // The next fight is identical too (encounter and combat streams).
        var a = run.StartCombat(CombatKind.Normal);
        var b = loaded.StartCombat(CombatKind.Normal);
        Assert.Equal(a.Enemies.Select(e => e.Name), b.Enemies.Select(e => e.Name));
        AutoPlayer.RunToEnd(a);
        AutoPlayer.RunToEnd(b);
        Assert.Equal(a.Events.Select(GameEventText.Describe), b.Events.Select(GameEventText.Describe));
    }

    [Fact]
    public void Merchant_stock_survives_save_and_load()
    {
        var run = PlayedRun();
        run.Party.Gold = 500;
        var m = run.CreateMerchant();
        var sold = m.Offers.First(o => o.Kind == ShopKind.Card);
        m.Buy(run.Party, sold, run.LootRng);

        var save = SaveGame.Merchant(m, run.Party, 3, 4);
        string json = JsonSerializer.Serialize(save, ContentDb.JsonOptions);
        var back = SaveGame.Merchant(JsonSerializer.Deserialize<MerchantSave>(json, ContentDb.JsonOptions)!, run.Party, TestContent.Db);

        Assert.Equal(m.Offers.Count, back.Offers.Count);
        for (int i = 0; i < m.Offers.Count; i++)
        {
            Assert.Equal(m.Offers[i].Kind, back.Offers[i].Kind);
            Assert.Equal(m.Offers[i].Price, back.Offers[i].Price);
            Assert.Equal(m.Offers[i].Sold, back.Offers[i].Sold);
            Assert.Equal(m.Offers[i].Card?.Id, back.Offers[i].Card?.Id);
            Assert.Same(m.Offers[i].Hero, back.Offers[i].Hero);
        }
    }

    [Fact]
    public void Unknown_save_versions_are_refused()
    {
        var save = PlayedRun().ToSave();
        save.Version = 999;
        Assert.Throws<InvalidDataException>(() => Run.FromSave(TestContent.Db, save));
    }
}

public class FxDataTests
{
    [Fact]
    public void Every_attack_has_a_known_effect()
    {
        var db = TestContent.Db;
        foreach (var e in db.Enemies.Values)
            foreach (var i in e.Intents.Where(i => i.Kind == IntentKind.Attack))
                Assert.Contains(i.Fx, ContentDb.KnownFx);
        foreach (var h in db.Heroes.Values)
            Assert.Contains(h.WeaponFx, ContentDb.KnownFx);
    }

    [Fact]
    public void Unknown_fx_is_reported()
    {
        var bad = new CardDef("x.fx", "Fx", "neutral", CardSpeed.Action, CheckKind.None, Rarity.Common, "",
            new[] { new EffectDef("Block", Args: new Dictionary<string, string> { ["amount"] = "1" }) }, Fx: "lazer");
        var db = new ContentDb(TestContent.Db.Cards.Values.Append(bad), TestContent.Db.Heroes.Values, TestContent.Db.Enemies.Values);
        Assert.Contains(db.Validate(), e => e.Contains("lazer"));
    }

    [Fact]
    public void Damage_rolls_carry_their_type()
    {
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var c = TestContent.MakeFight(seed, new[] { "fighter", "wizard" }, new[] { "goblin", "goblin" });
            AutoPlayer.RunToEnd(c);
            var rolls = c.Events.OfType<DamageRolled>().ToList();
            Assert.All(rolls.Where(r => r.Source is Enemy), r =>
                Assert.Contains(((Enemy)r.Source).Def.Intents, i => i.Name == r.Type));
            Assert.All(rolls.Where(r => r.Source is not Enemy), r => Assert.False(string.IsNullOrEmpty(r.Type)));
            if (rolls.Any(r => r.Source is Enemy)) return;
        }
    }
}

public class LevelPaceTests
{
    [Fact]
    public void Level_two_comes_a_little_sooner()
    {
        Assert.Equal(35, TestContent.Db.Balance.XpForLevel(2));
        Assert.True(TestContent.Db.Balance.XpForLevel(3) < 100);
    }
}

public class LuckPerFightTests
{
    [Fact]
    public void Only_one_luck_reroll_per_fight()
    {
        Assert.Equal(1, TestContent.Db.Balance.LuckRerollsPerFight);
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var c = TestContent.MakeFight(seed, new[] { "fighter", "rogue" }, new[] { "ogre" }, CombatKind.Boss);
            c.Enemies[0].Ac = 40;   // only natural 20s hit, so attacks keep failing
            c.Start();
            if (c.ActiveHero is null) continue;
            c.UseBasic(ContentDb.BasicStrikeId, c.Enemies[0]);
            if (c.Phase != CombatPhase.AwaitingLuck) continue;
            c.ResolveLuck(true);
            Assert.False(c.LuckAvailable);
            Assert.True(c.Party.Luck > 0);

            // The next failed attack in this fight is not offered a reroll.
            if (c.Phase == CombatPhase.HeroTurn) c.EndTurn();
            for (int i = 0; i < 50 && !c.IsOver; i++)
            {
                if (c.Phase == CombatPhase.HeroTurn)
                {
                    if (c.CanUseBasic()) c.UseBasic(ContentDb.BasicStrikeId, c.Enemies[0]);
                    Assert.NotEqual(CombatPhase.AwaitingLuck, c.Phase);
                    if (c.Phase == CombatPhase.HeroTurn) c.EndTurn();
                }
                else if (c.Phase == CombatPhase.AwaitingReaction) c.ResolveReaction(null);
                else if (c.Phase == CombatPhase.AwaitingTiming) c.ResolveTiming(TimingGrade.Miss);
            }
            return;
        }
        Assert.Fail("No Luck prompt in 300 seeds.");
    }
}
