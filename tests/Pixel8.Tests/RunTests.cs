using Pixel8.Core.Combat;
using Pixel8.Core.Randomness;
using Pixel8.Core.Runs;

namespace Pixel8.Tests;

public class RunTests
{
    private static readonly string[] Trio = { "fighter", "rogue", "wizard" };

    [Fact]
    public void Encounters_use_the_right_tiers()
    {
        var db = TestContent.Db;
        var rng = new Rng(3);
        for (int depth = 1; depth <= Run.FinalDepth; depth++)
        {
            var normal = Encounters.Roll(db, CombatKind.Normal, depth, rng);
            Assert.InRange(normal.Count, 1, 4);
            Assert.All(normal, e => Assert.Equal(1, e.Def.Tier));
            Assert.All(normal, e => Assert.True(e.Def.MinDepth <= depth));

            var elite = Encounters.Roll(db, CombatKind.Elite, depth, rng);
            Assert.Equal(2, elite[0].Def.Tier);

            var boss = Encounters.Roll(db, CombatKind.Boss, depth, rng);
            Assert.Equal(3, boss[0].Def.Tier);
        }
    }

    [Fact]
    public void Duplicate_enemies_get_letters()
    {
        var db = TestContent.Db;
        var rng = new Rng(1);
        for (int i = 0; i < 200; i++)
        {
            var group = Encounters.Roll(db, CombatKind.Normal, 3, rng);
            Assert.Equal(group.Count, group.Select(e => e.Name).Distinct().Count());
        }
    }

    [Fact]
    public void Rest_heals_a_third_and_restocks()
    {
        var run = new Run(TestContent.Db, 5, Trio);
        run.Party.StartDepth(20);
        var fighter = run.Party.Heroes[0];
        fighter.Hp = 1;
        foreach (var card in fighter.Deck.Hand.ToList()) fighter.Deck.Play(card);

        run.Rest();

        Assert.Equal(1 + fighter.MaxHp / 3, fighter.Hp);
        Assert.Equal(fighter.Deck.MaxHand, fighter.Deck.Hand.Count);
        Assert.Empty(fighter.Deck.Discard);
    }

    [Fact]
    public void A_run_can_chain_fights_with_persistent_hp()
    {
        var run = new Run(TestContent.Db, 9, Trio);
        run.Party.StartDepth(20);
        for (int i = 0; i < 3 && !run.Party.IsWiped; i++)
        {
            var combat = run.StartCombat(CombatKind.Normal);
            var outcome = AutoPlayer.RunToEnd(combat);
            Assert.True(outcome is CombatPhase.Victory or CombatPhase.Defeat);
            if (outcome == CombatPhase.Victory)
            {
                var draft = run.CreateDraft();
                draft.Choose(draft.Offers[0], run.LootRng);
            }
        }
        Assert.Contains(run.Party.Heroes, h => h.Hp < h.MaxHp || h.IsDead);
    }
}
