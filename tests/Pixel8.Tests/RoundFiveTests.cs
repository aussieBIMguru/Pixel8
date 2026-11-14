using Pixel8.Core.Cards;
using Pixel8.Core.Combat;
using Pixel8.Core.Content;
using Pixel8.Core.Dice;
using Pixel8.Core.Heroes;
using Pixel8.Core.Randomness;
using Pixel8.Core.Rewards;
using Pixel8.Core.Runs;

namespace Pixel8.Tests;

/// <summary>Session 5: roll strip events, Luck reroll, timed reactions, card upgrades, merchant.</summary>
public class RollStripTests
{
    private static readonly string[] Trio = { "fighter", "rogue", "wizard" };

    [Fact]
    public void Damage_faces_match_the_plain_roll()
    {
        var dice = DiceExpr.Parse("3d6+2");
        for (ulong seed = 1; seed <= 50; seed++)
        {
            int plain = dice.Roll(new Rng(seed), critical: seed % 2 == 0);
            int detailed = dice.Roll(new Rng(seed), seed % 2 == 0, out int[] faces);
            Assert.Equal(plain, detailed);
            Assert.Equal(seed % 2 == 0 ? 6 : 3, faces.Length);
            Assert.Equal(Math.Max(0, faces.Sum() + 2), detailed);
        }
    }

    [Fact]
    public void Numeric_roll_parts_add_up_to_the_modifier()
    {
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var c = TestContent.MakeFight(seed, Trio, new[] { "goblin", "orc" });
            c.LuckPrompts = false;
            AutoPlayer.RunToEnd(c);
            foreach (var roll in c.Events.OfType<CheckRolled>())
                Assert.Equal(roll.Result.Modifier, roll.Breakdown.Where(p => !p.Tag).Sum(p => p.Value));
        }
    }

    [Fact]
    public void Every_damage_event_is_preceded_by_its_roll()
    {
        var c = TestContent.MakeFight(3, Trio, new[] { "goblin", "goblin", "skeleton" });
        AutoPlayer.RunToEnd(c);
        var events = c.Events.ToList();
        for (int i = 0; i < events.Count; i++)
        {
            if (events[i] is not DamageDealt dealt) continue;
            Assert.Contains(events.Take(i), e => e is DamageRolled r && ReferenceEquals(r.Target, dealt.Target));
        }
        Assert.Contains(events, e => e is DamageRolled);
    }

    [Fact]
    public void Hard_shows_its_attack_bonus_as_a_part()
    {
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var run = new Run(TestContent.Db, seed, Trio, "hard");
            run.Party.StartDepth(20);
            var c = run.StartCombat(CombatKind.Normal);
            AutoPlayer.RunToEnd(c);
            var enemyRoll = c.Events.OfType<CheckRolled>().FirstOrDefault(r => r.Roller is Enemy && r.Versus == "AC");   // enemy attacks, not saves vs hero spells
            if (enemyRoll is null) continue;
            Assert.Contains(enemyRoll.Breakdown, p => p.Label == "HARD" && p.Value == 1);
            return;
        }
        Assert.Fail("No enemy attack in 20 fights.");
    }
}

public class LuckTests
{
    [Fact]
    public void A_failed_attack_offers_luck_and_spending_rerolls()
    {
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var c = TestContent.MakeFight(seed, new[] { "fighter" }, new[] { "goblin" });
            c.Enemies[0].Ac = 40;   // only a natural 20 hits: the first Strike almost always fails
            c.Start();
            Assert.Equal(CombatPhase.HeroTurn, c.Phase);
            c.UseBasic(ContentDb.BasicStrikeId, c.Enemies[0]);
            if (c.Phase != CombatPhase.AwaitingLuck) continue;

            int luck = c.LuckLeft;
            Assert.False(c.PendingLuck!.Result.Success);
            c.ResolveLuck(true);

            Assert.Equal(luck - 1, c.LuckLeft);
            Assert.Equal(CombatPhase.HeroTurn, c.Phase);
            Assert.True(c.Events.OfType<CheckRolled>().Last().IsReroll);
            Assert.False(c.CanUseBasic());
            return;
        }
        Assert.Fail("No Luck prompt in 200 seeds.");
    }

    [Fact]
    public void Declining_keeps_luck_and_no_luck_means_no_prompt()
    {
        var c = TestContent.MakeFight(4, new[] { "fighter" }, new[] { "goblin" });
        c.Enemies[0].Ac = 40;
        c.Start();
        c.UseBasic(ContentDb.BasicStrikeId, c.Enemies[0]);
        if (c.Phase == CombatPhase.AwaitingLuck)
        {
            int luck = c.LuckLeft;
            c.ResolveLuck(false);
            Assert.Equal(luck, c.LuckLeft);
            Assert.Equal(CombatPhase.HeroTurn, c.Phase);
        }

        var broke = TestContent.MakeFight(4, new[] { "fighter" }, new[] { "goblin" });
        broke.Party.Luck = 0;
        broke.FightLuck = 0;
        broke.Enemies[0].Ac = 40;
        broke.Start();
        broke.UseBasic(ContentDb.BasicStrikeId, broke.Enemies[0]);
        Assert.NotEqual(CombatPhase.AwaitingLuck, broke.Phase);
    }
}

public class TimingTests
{
    [Fact]
    public void Repo_marks_the_big_attacks()
    {
        var db = TestContent.Db;
        Assert.True(db.Enemy("ogre").Intents.Single(i => i.Name == "Greatclub").Big);
        Assert.True(db.Enemy("bugbear").Intents.Single(i => i.Name == "Morningstar").Big);
        Assert.True(db.Enemy("orc").Intents.Single(i => i.Name == "Greataxe").Big);
        Assert.False(db.Enemy("goblin").Intents.Any(i => i.Big));
    }

    [Theory]
    [InlineData(0.50, TimingGrade.Perfect)]
    [InlineData(0.53, TimingGrade.Perfect)]
    [InlineData(0.58, TimingGrade.Good)]
    [InlineData(0.42, TimingGrade.Good)]
    [InlineData(0.67, TimingGrade.Ok)]
    [InlineData(0.75, TimingGrade.Miss)]
    [InlineData(0.00, TimingGrade.Miss)]
    public void Grades_follow_the_window(double marker, TimingGrade expected)
    {
        // Normal window 0.20: Perfect ±0.033, Good ±0.10, Ok ±0.20.
        Assert.Equal(expected, TimingRules.Grade(marker, 0.5, 0.20));
    }

    [Fact]
    public void Difficulty_sets_the_window()
    {
        var b = TestContent.Db.Balance;
        Assert.True(b.DifficultyOf("easy").TimingWindow > b.DifficultyOf("normal").TimingWindow);
        Assert.True(b.DifficultyOf("normal").TimingWindow > b.DifficultyOf("hard").TimingWindow);
    }

    /// <summary>Plays the ogre fight with the heroes only ending turns, and answers the first timing prompt.</summary>
    private static (CombatState Combat, int HpBefore, Hero Defender)? FirstTiming(ulong seed, TimingMode mode = TimingMode.On)
    {
        var c = TestContent.MakeFight(seed, new[] { "fighter", "cleric" }, new[] { "ogre" }, CombatKind.Boss);
        c.Timing = mode;
        foreach (var h in c.Heroes) h.MaxHp = h.Hp = 500;
        c.Start();
        for (int i = 0; i < 200 && !c.IsOver; i++)
        {
            switch (c.Phase)
            {
                case CombatPhase.HeroTurn: c.EndTurn(); break;
                case CombatPhase.AwaitingReaction: c.ResolveReaction(null); break;
                case CombatPhase.AwaitingTiming:
                    var d = c.PendingTiming!.Defender;
                    return (c, d.Hp, d);
            }
        }
        return null;
    }

    [Fact]
    public void Perfect_dodges_and_miss_takes_full_damage()
    {
        var perfect = FirstTiming(1)!.Value;
        perfect.Combat.ResolveTiming(TimingGrade.Perfect);
        var roll = perfect.Combat.Events.OfType<DamageRolled>().Last();
        Assert.Equal(0, roll.Total);
        Assert.Contains("DODGED", roll.Notes);

        var miss = FirstTiming(1)!.Value;
        miss.Combat.ResolveTiming(TimingGrade.Miss);
        var full = miss.Combat.Events.OfType<DamageRolled>().Last();
        Assert.Equal(full.Raw, full.Total);
        Assert.Equal(full.Total, miss.HpBefore - miss.Defender.Hp);
    }

    [Fact]
    public void Good_halves_the_damage()
    {
        var good = FirstTiming(2)!.Value;
        good.Combat.ResolveTiming(TimingGrade.Good);
        var roll = good.Combat.Events.OfType<DamageRolled>().Last();
        Assert.Equal(Math.Max(1, (int)Math.Round(roll.Raw * 0.5)), roll.Total);
    }

    [Fact]
    public void Off_and_auto_never_pause()
    {
        Assert.Null(FirstTiming(1, TimingMode.Off));
        Assert.Null(FirstTiming(1, TimingMode.Auto));
    }

    [Fact]
    public void Same_seed_and_grades_give_the_same_fight()
    {
        static List<string> Play()
        {
            var c = TestContent.MakeFight(77, new[] { "fighter", "rogue", "cleric" }, new[] { "ogre" }, CombatKind.Boss);
            AutoPlayer.RunToEnd(c);
            return c.Events.Select(GameEventText.Describe).ToList();
        }
        Assert.Equal(Play(), Play());
    }
}

public class UpgradeTests
{
    [Fact]
    public void Every_class_card_has_an_upgrade()
    {
        foreach (var card in TestContent.Db.Cards.Values.Where(c => c.ClassId != "basic"))
            Assert.True(card.CanUpgrade, $"{card.Id} has no upgrade block");
    }

    [Fact]
    public void Upgrading_swaps_text_and_effects_but_keeps_the_name()
    {
        var def = TestContent.Db.Card("wizard.magic_missile");
        var card = new CardInstance(def);
        Assert.Same(def, card.Def);

        card.Upgraded = true;
        Assert.Equal("Magic Missile+", card.DisplayName);
        Assert.Equal(def.Name, card.Def.Name);
        Assert.Equal("4d4+4", card.Def.Effects[0].Args!["dice"]);
        Assert.False(card.CanUpgrade);
    }

    [Fact]
    public void Run_upgrade_lists_only_upgradable_cards()
    {
        var run = new Run(TestContent.Db, 3, new[] { "fighter", "rogue", "wizard" });
        run.Party.StartDepth(20);
        int before = run.UpgradableCards().Count();
        var (_, card) = run.UpgradableCards().First();
        Assert.True(run.UpgradeCard(card));
        Assert.False(run.UpgradeCard(card));
        Assert.Equal(before - 1, run.UpgradableCards().Count());
    }

    [Fact]
    public void Upgraded_cleave_hits_harder_on_average()
    {
        double Average(bool upgraded)
        {
            double total = 0;
            for (ulong seed = 1; seed <= 300; seed++)
            {
                var c = TestContent.MakeFight(seed, new[] { "fighter" }, new[] { "ogre" }, CombatKind.Boss);
                c.LuckPrompts = false;
                c.Enemies[0].Ac = 1;   // always hits
                c.Start();
                var card = new CardInstance(TestContent.Db.Card("fighter.cleave"), upgraded);
                c.Heroes[0].Deck.AddCard(card, new Rng(seed));
                // Put it in hand: draw until it arrives.
                while (!c.Heroes[0].Deck.Hand.Contains(card) && c.Heroes[0].Deck.DrawPile.Count > 0)
                {
                    if (c.Heroes[0].Deck.Hand.Count >= c.Heroes[0].Deck.MaxHand) c.Heroes[0].Deck.Play(c.Heroes[0].Deck.Hand[0]);
                    c.Heroes[0].Deck.Draw(1);
                }
                int hp = c.Enemies[0].Hp;
                c.PlayCard(card, c.Enemies[0]);
                total += hp - c.Enemies[0].Hp;
            }
            return total / 300;
        }
        Assert.True(Average(true) > Average(false) + 1);
    }
}

public class MerchantTests
{
    private static Run MakeRun(ulong seed)
    {
        var run = new Run(TestContent.Db, seed, new[] { "fighter", "rogue", "wizard" });
        run.Party.StartDepth(20);
        run.Party.Gold = 500;
        return run;
    }

    [Fact]
    public void Stock_has_a_card_per_hero_items_and_both_services()
    {
        var m = MakeRun(1).CreateMerchant();
        Assert.Equal(3, m.Offers.Count(o => o.Kind == ShopKind.Card));
        Assert.Contains(m.Offers, o => o.Kind == ShopKind.Item);
        Assert.Single(m.Offers, o => o.Kind == ShopKind.RemoveCard);
        Assert.Single(m.Offers, o => o.Kind == ShopKind.UpgradeCard);
        Assert.All(m.Offers, o => Assert.True(o.Price > 0));
    }

    [Fact]
    public void Buying_pays_gold_and_delivers()
    {
        var run = MakeRun(2);
        var m = run.CreateMerchant();
        var cardOffer = m.Offers.First(o => o.Kind == ShopKind.Card);
        int deck = cardOffer.Hero!.Deck.DrawPile.Count;
        m.Buy(run.Party, cardOffer, run.LootRng);
        Assert.Equal(500 - cardOffer.Price, run.Party.Gold);
        Assert.Equal(deck + 1, cardOffer.Hero.Deck.DrawPile.Count);
        Assert.True(cardOffer.Sold);
        Assert.Throws<InvalidOperationException>(() => m.Buy(run.Party, cardOffer, run.LootRng));

        var potion = m.Offers.First(o => o.Kind == ShopKind.Item);
        m.Buy(run.Party, potion, run.LootRng);
        Assert.Contains(potion.Item!.Id, run.Party.Bag);
    }

    [Fact]
    public void Removal_and_upgrade_work_once()
    {
        var run = MakeRun(3);
        var m = run.CreateMerchant();
        var fighter = run.Party.Heroes[0];
        var victim = fighter.Deck.Hand[0];
        int count = fighter.Deck.AllCards.Count();

        var remove = m.Offers.Single(o => o.Kind == ShopKind.RemoveCard);
        m.RemoveCard(run.Party, remove, fighter, victim);
        Assert.Equal(count - 1, fighter.Deck.AllCards.Count());
        Assert.Throws<InvalidOperationException>(() => m.RemoveCard(run.Party, remove, fighter, fighter.Deck.Hand[0]));

        var upgrade = m.Offers.Single(o => o.Kind == ShopKind.UpgradeCard);
        var target = fighter.Deck.AllCards.First(c => c.CanUpgrade);
        m.UpgradeCard(run.Party, upgrade, target);
        Assert.True(target.Upgraded);
    }

    [Fact]
    public void Not_enough_gold_is_refused()
    {
        var run = MakeRun(4);
        run.Party.Gold = 0;
        var m = run.CreateMerchant();
        var offer = m.Offers.First();
        Assert.False(Merchant.CanAfford(run.Party, offer));
        Assert.Throws<InvalidOperationException>(() => m.Buy(run.Party, offer, run.LootRng));
    }

    [Fact]
    public void A_potion_heals_out_of_combat()
    {
        var run = MakeRun(5);
        var hero = run.Party.Heroes[0];
        hero.Hp = 1;
        run.Party.Bag.Add("potion_healing");
        int healed = run.UseItemOutOfCombat("potion_healing", hero);
        Assert.InRange(healed, 4, 10);
        Assert.Empty(run.Party.Bag);
        Assert.Equal(-1, run.UseItemOutOfCombat("potion_healing", hero));
    }
}
