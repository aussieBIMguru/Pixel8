using Pixel8.Core.Combat;
using Pixel8.Core.Runs;

namespace Pixel8.Tests;

public class DifficultyTests
{
    private static readonly string[] Trio = { "fighter", "rogue", "wizard" };

    [Fact]
    public void Repo_defines_easy_normal_hard_in_order()
    {
        Assert.Equal(new[] { "easy", "normal", "hard" }, TestContent.Db.Balance.Difficulties.Keys);
    }

    [Fact]
    public void Unknown_difficulty_falls_back_to_normal()
    {
        var d = TestContent.Db.Balance.DifficultyOf("nightmare");
        Assert.Equal("Normal", d.Name);
    }

    [Fact]
    public void Easy_heroes_have_more_hp_and_hard_enemies_more()
    {
        var easy = new Run(TestContent.Db, 1, Trio, "easy");
        var normal = new Run(TestContent.Db, 1, Trio, "normal");
        var hard = new Run(TestContent.Db, 1, Trio, "hard");

        Assert.True(easy.Party.Heroes[0].MaxHp > normal.Party.Heroes[0].MaxHp);
        Assert.Equal(normal.Party.Heroes[0].MaxHp, hard.Party.Heroes[0].MaxHp);

        var db = TestContent.Db;
        int easyOgre = Encounters.Spawn(db, "ogre", null, easy.Difficulty).MaxHp;
        int normalOgre = Encounters.Spawn(db, "ogre", null, normal.Difficulty).MaxHp;
        int hardOgre = Encounters.Spawn(db, "ogre", null, hard.Difficulty).MaxHp;
        Assert.True(easyOgre <= normalOgre && normalOgre < hardOgre);   // round 12: Easy enemy HP is x1.0 (was x0.9)
    }

    [Fact]
    public void Hard_raises_the_odds_shown_on_enemy_intents()
    {
        double Odds(string id)
        {
            var run = new Run(TestContent.Db, 4, Trio, id);
            run.Party.StartDepth(20);
            var c = run.StartCombat(CombatKind.Boss);
            c.Start();
            var intent = c.IntentFor(c.Enemies[0])!;
            Assert.NotNull(intent.Target);
            return intent.Odds!.Value.Chance * 20 + intent.Target!.Ac;
        }
        // For unclamped rolls, chance × 20 = 21 − AC + bonus, so this is 21 + bonus: hard is exactly one higher.
        Assert.Equal(Odds("normal") + 1, Odds("hard"), 3);
    }

    [Fact]
    public void Easy_heals_after_a_win()
    {
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var run = new Run(TestContent.Db, seed, Trio, "easy");
            run.Party.StartDepth(20);
            var c = run.StartCombat(CombatKind.Normal);
            if (AutoPlayer.RunToEnd(c) != CombatPhase.Victory) continue;

            int end = c.Events.ToList().FindIndex(e => e is CombatEnded);
            bool hurt = c.Events.OfType<DamageDealt>().Any(d => d.Target is Pixel8.Core.Heroes.Hero && d.Amount > 0);
            if (!hurt) continue;
            Assert.Contains(c.Events.Skip(end - 3).Take(3), e => e is Healed);
            return;
        }
    }
}
