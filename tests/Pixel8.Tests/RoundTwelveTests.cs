using Pixel8.Core.Combat;
using Pixel8.Core.Content;

namespace Pixel8.Tests;

/// <summary>Round 12: regeneration tires, so no fight can go on forever.</summary>
public class RegenTiresTests
{
    private static void ToRound(CombatState c, int round)
    {
        for (int i = 0; i < 100 && !c.IsOver && c.Round < round; i++) R11.EndTurn(c);
        Assert.False(c.IsOver);
        Assert.Equal(round, c.Round);
    }

    [Fact]
    public void Regeneration_stops_after_the_cutoff_round()
    {
        int tires = R11.Db.Balance.RegenTiresAfterRound;
        Assert.True(tires > 0);

        var c = R11.Fight(21, new[] { "fighter" }, new[] { "troll" });
        var fighter = c.Heroes[0];
        var troll = c.Enemies[0];

        // Still regenerating on the cutoff round itself.
        ToRound(c, tires);
        c.DealDamage(fighter, troll, 12, "weapon");
        int hp = troll.Hp;
        R11.EndTurn(c);
        Assert.Equal(hp + troll.Def.Regen, troll.Hp);
        Assert.DoesNotContain(c.Events, e => e is RegenTired);

        // The round after: the wounds stop closing, for good.
        c.DealDamage(fighter, troll, 12, "weapon");
        hp = troll.Hp;
        R11.EndTurn(c);
        Assert.Equal(hp, troll.Hp);
        Assert.Single(c.Events.OfType<RegenTired>());
        R11.EndTurn(c);
        Assert.Equal(hp, troll.Hp);
        Assert.Single(c.Events.OfType<RegenTired>());
    }

    [Fact]
    public void Hydra_heads_stop_regrowing_after_the_cutoff_round()
    {
        int tires = R11.Db.Balance.RegenTiresAfterRound;
        var c = R11.Fight(22, new[] { "fighter" }, new[] { "hydra" }, CombatKind.Boss);
        var fighter = c.Heroes[0];
        var hydra = c.Enemies[0];

        ToRound(c, tires + 1);
        // Hit through any Block from Coil so the full SeverAt lands.
        c.DealDamage(fighter, hydra, hydra.Def.SeverAt + hydra.Block, "weapon");
        int heads = c.HeadsOf(hydra);
        Assert.True(heads < hydra.Def.Heads);
        R11.EndTurn(c);
        R11.EndTurn(c);
        Assert.Equal(heads, c.HeadsOf(hydra));
    }
}

/// <summary>Round 12: per-Depth enemy multipliers.</summary>
public class DepthScalingTests
{
    [Fact]
    public void Per_depth_multipliers_default_to_one_and_clamp_to_the_last_entry()
    {
        var b = new BalanceDef();
        Assert.Equal(1, b.EnemyHpAt(3));
        b.EnemyDamageByDepth = new() { 1.5, 1.2 };
        Assert.Equal(1.5, b.EnemyDamageAt(1));
        Assert.Equal(1.2, b.EnemyDamageAt(5));
        Assert.Equal(1, b.EnemyDamageAt(0));
        Assert.Equal(5, R11.Db.Balance.EnemyHpByDepth.Count);
        Assert.Equal(5, R11.Db.Balance.EnemyDamageByDepth.Count);
    }

    [Fact]
    public void A_difficulty_can_reshape_its_own_depth_curve()
    {
        var b = new BalanceDef { EnemyDamageByDepth = new() { 1.4, 1.0 }, EnemyHpByDepth = new() { 1.0, 0.9 } };
        var hard = new DifficultyDef { EnemyDamageByDepth = new() { 1.5, 0.8 }, EnemyHpByDepth = new() { 2.0 } };
        Assert.Equal(1.4 * 1.5, b.EnemyDamageAt(1, hard), 6);
        Assert.Equal(1.0 * 0.8, b.EnemyDamageAt(4, hard), 6);
        Assert.Equal(0.9 * 2.0, b.EnemyHpAt(2, hard), 6);
        Assert.Equal(1.4, b.EnemyDamageAt(1, null), 6);
        Assert.Equal(1.4, b.EnemyDamageAt(1, new DifficultyDef()), 6);
    }

    [Fact]
    public void Spawned_enemies_use_the_difficultys_depth_hp()
    {
        var plain = new DifficultyDef();
        var tough = new DifficultyDef { EnemyHpByDepth = new() { 1, 1, 2 } };
        var a = Pixel8.Core.Runs.Encounters.Spawn(R11.Db, "ogre", difficulty: plain, depth: 3);
        var b = Pixel8.Core.Runs.Encounters.Spawn(R11.Db, "ogre", difficulty: tough, depth: 3);
        Assert.InRange(b.MaxHp, a.MaxHp * 2 - 1, a.MaxHp * 2 + 1);
    }
}

