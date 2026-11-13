using Pixel8.Core.Cards;
using Pixel8.Core.Combat;
using Pixel8.Core.Content;
using Pixel8.Core.Heroes;
using Pixel8.Core.Randomness;
using Pixel8.Core.Rewards;
using Pixel8.Core.Runs;

namespace Pixel8.Tests;

internal static class TestContent
{
    private static readonly Lazy<ContentDb> _db = new(() => ContentDb.Load(ContentDb.FindDataDirectory()));

    public static ContentDb Db => _db.Value;

    public static Party MakeParty(ulong seed, params string[] classes)
    {
        var party = Party.Create(Db, classes, new Rng(seed).Fork("party"));
        party.StartDepth(20);
        return party;
    }

    public static CombatState MakeFight(ulong seed, string[] heroes, string[] enemies, CombatKind kind = CombatKind.Normal) =>
        new(MakeParty(seed, heroes), enemies.Select(id => Encounters.Spawn(Db, id)), Db, new Rng(seed).Fork("combat"), kind);
}

public class ContentTests
{
    [Fact]
    public void Repo_data_loads_and_validates()
    {
        var db = TestContent.Db;
        Assert.Empty(db.Validate());
        Assert.True(db.Cards.ContainsKey(ContentDb.BasicStrikeId));
        Assert.Equal(4, db.Heroes.Count);
        Assert.True(db.Enemies.ContainsKey("goblin"));
    }

    [Fact]
    public void Unknown_effect_type_is_reported()
    {
        var bad = new CardDef("x.bad", "Bad", "neutral", CardSpeed.Action, CheckKind.None, Rarity.Common, "",
            new[] { new EffectDef("Teleport") });
        var db = new ContentDb(TestContent.Db.Cards.Values.Append(bad), TestContent.Db.Heroes.Values, TestContent.Db.Enemies.Values);
        Assert.Contains(db.Validate(), e => e.Contains("Teleport"));
    }
}

public class CombatTests
{
    private static readonly string[] Trio = { "fighter", "rogue", "wizard" };

    [Fact]
    public void Fights_always_end_over_200_seeds()
    {
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var c = TestContent.MakeFight(seed, Trio, new[] { "goblin", "goblin", "orc" });
            var outcome = AutoPlayer.RunToEnd(c);
            Assert.True(outcome is CombatPhase.Victory or CombatPhase.Defeat, $"seed {seed}: {outcome}");
        }
    }

    [Fact]
    public void Same_seed_gives_the_same_fight()
    {
        static List<string> Run()
        {
            var c = TestContent.MakeFight(42, Trio, new[] { "goblin", "skeleton" });
            AutoPlayer.RunToEnd(c);
            return c.Events.Select(GameEventText.Describe).ToList();
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void Heroes_act_before_any_enemy_in_round_one()
    {
        for (ulong seed = 1; seed <= 50; seed++)
        {
            var c = TestContent.MakeFight(seed, Trio, new[] { "wolf", "goblin", "skeleton" });
            c.Start();
            Assert.Equal(CombatPhase.HeroTurn, c.Phase);
            Assert.DoesNotContain(c.Events, e => e is CheckRolled { Roller: Enemy });
            Assert.All(c.TurnOrder.Take(Trio.Length), t => Assert.True(t.IsHero));
        }
    }

    [Fact]
    public void Enemy_hp_is_scaled_by_balance()
    {
        var goblin = Encounters.Spawn(TestContent.Db, "goblin");
        Assert.Equal((int)Math.Round(7 * TestContent.Db.Balance.EnemyHpMultiplier), goblin.MaxHp);
    }

    [Fact]
    public void Block_absorbs_damage_first()
    {
        var c = TestContent.MakeFight(1, new[] { "fighter" }, new[] { "goblin" });
        var hero = c.Heroes[0];
        hero.Block = 5;
        c.DealDamage(c.Enemies[0], hero, 3, "test");
        Assert.Equal(hero.MaxHp, hero.Hp);
        Assert.Equal(2, hero.Block);

        c.DealDamage(c.Enemies[0], hero, 6, "test");
        Assert.Equal(hero.MaxHp - 4, hero.Hp);
        Assert.Equal(0, hero.Block);
    }

    [Fact]
    public void Dropping_to_zero_downs_a_hero_and_healing_revives()
    {
        var c = TestContent.MakeFight(1, new[] { "fighter", "cleric" }, new[] { "goblin" });
        var hero = c.Heroes[0];
        c.DealDamage(c.Enemies[0], hero, 999, "test");
        Assert.True(hero.IsDowned);
        Assert.False(hero.CanAct);
        Assert.Equal(0, hero.Hp);

        c.Heal(hero, 4);
        Assert.False(hero.IsDowned);
        Assert.Equal(4, hero.Hp);
    }

    [Fact]
    public void Shield_reaction_turns_a_hit_into_a_miss()
    {
        var db = TestContent.Db;
        bool seen = false;
        for (ulong seed = 1; seed <= 500 && !seen; seed++)
        {
            var rng = new Rng(seed);
            var wizard = new Hero(db.HeroClass("wizard"), "Wizard", Enumerable.Repeat(db.Card("wizard.shield"), 6), rng);
            wizard.Deck.DrawToFull();
            var c = new CombatState(new Party(new[] { wizard }), new[] { new Enemy(db.Enemy("goblin")) }, db, rng.Fork("combat"));
            c.Start();

            while (!c.IsOver && !seen)
            {
                if (c.Phase == CombatPhase.HeroTurn)
                {
                    c.EndTurn();   // the wizard just waits
                }
                else if (c.Phase == CombatPhase.AwaitingReaction)
                {
                    int hp = wizard.Hp;
                    var pending = c.Pending!;
                    Assert.True(pending.Result.Success);
                    c.ResolveReaction(pending.Options[0]);

                    var modified = c.Events.OfType<RollModified>().Last();
                    Assert.False(modified.Result.Success);
                    Assert.Equal(pending.Result.Target + 5, modified.Result.Target);
                    Assert.Equal(hp, wizard.Hp);
                    seen = true;
                }
            }
        }
        Assert.True(seen, "No reaction prompt appeared in 500 seeds.");
    }

    [Fact]
    public void Marked_widens_the_crit_range_in_the_preview()
    {
        var c = TestContent.MakeFight(1, new[] { "fighter" }, new[] { "goblin" });
        var hero = c.Heroes[0];
        var goblin = c.Enemies[0];
        goblin.Ac = 40;   // only a crit can hit

        Assert.Equal(5, c.PreviewAttack(hero, goblin).Percent);
        c.ApplyCondition(goblin, Cond.Marked, 2);
        Assert.Equal(10, c.PreviewAttack(hero, goblin).Percent);
    }

    [Fact]
    public void Taunt_redirects_enemy_intents()
    {
        var c = TestContent.MakeFight(3, new[] { "fighter", "wizard" }, new[] { "orc" });
        var fighter = c.Heroes[0];
        fighter.MaxHp = fighter.Hp = 200;   // make sure the orc can't drop them before we look
        c.Start();
        while (c.Phase == CombatPhase.AwaitingReaction) c.ResolveReaction(null);
        c.ApplyCondition(fighter, Cond.Taunting, 1);

        var intent = c.IntentFor(c.Enemies[0]);
        Assert.NotNull(intent);
        Assert.Same(fighter, intent!.Target);
    }

    [Fact]
    public void Flee_costs_torch_and_gold_and_is_blocked_in_boss_fights()
    {
        var c = TestContent.MakeFight(5, Trio, new[] { "goblin" });
        c.Party.Gold = 25;
        c.Party.Torch = 10;
        c.Start();
        while (c.Phase == CombatPhase.AwaitingReaction) c.ResolveReaction(null);
        Assert.Equal(CombatPhase.HeroTurn, c.Phase);

        c.Flee();
        Assert.Equal(CombatPhase.Fled, c.Phase);
        Assert.Equal(10 - c.Content.Balance.FleeTorchCost, c.Party.Torch);
        Assert.Equal(25 - c.Content.Balance.FleeGoldCost, c.Party.Gold);

        var boss = TestContent.MakeFight(5, Trio, new[] { "ogre" }, CombatKind.Boss);
        boss.Party.Torch = 10;
        boss.Start();
        while (boss.Phase == CombatPhase.AwaitingReaction) boss.ResolveReaction(null);
        Assert.False(boss.CanFlee(out _));
    }

    [Fact]
    public void Action_slot_is_spent_once_per_turn()
    {
        var c = TestContent.MakeFight(7, Trio, new[] { "ogre" }, CombatKind.Boss);
        c.Start();
        while (c.Phase == CombatPhase.AwaitingReaction) c.ResolveReaction(null);
        Assert.Equal(CombatPhase.HeroTurn, c.Phase);

        c.UseBasic(ContentDb.BasicStrikeId, c.Enemies[0]);
        Assert.False(c.CanUseBasic());
        Assert.Throws<InvalidOperationException>(() => c.UseBasic(ContentDb.BasicGuardId));
    }
}

public class DraftTests
{
    [Fact]
    public void Draft_offers_one_class_card_per_living_hero()
    {
        var party = TestContent.MakeParty(9, "fighter", "rogue", "wizard");
        party.Heroes[2].IsDead = true;
        var draft = CardDraft.Create(party, TestContent.Db, new Rng(9));

        Assert.Equal(2, draft.Offers.Count);
        foreach (var offer in draft.Offers)
            Assert.True(offer.Card.ClassId == offer.Hero.Class.Id || offer.Card.ClassId == "neutral");
    }

    [Fact]
    public void Choosing_adds_the_card_and_skipping_pays_gold()
    {
        var rng = new Rng(11);
        var party = TestContent.MakeParty(11, "fighter", "rogue", "wizard");
        var draft = CardDraft.Create(party, TestContent.Db, rng);
        var offer = draft.Offers[0];
        int before = offer.Hero.Deck.DrawPile.Count;
        draft.Choose(offer, rng);
        Assert.Equal(before + 1, offer.Hero.Deck.DrawPile.Count);
        Assert.Throws<InvalidOperationException>(() => draft.Skip(party, TestContent.Db.Balance));

        var second = CardDraft.Create(party, TestContent.Db, rng);
        int gold = party.Gold;
        second.Skip(party, TestContent.Db.Balance);
        Assert.Equal(gold + TestContent.Db.Balance.SkipDraftGold, party.Gold);
    }
}
