using Pixel8.Core.Dice;
using Pixel8.Core.Randomness;

namespace Pixel8.Tests;

public class DiceTests
{
    /// <summary>The displayed % must equal the true % — enumerate every face (and face pair for adv/dis).</summary>
    [Theory]
    [InlineData(5, 13, 20)]
    [InlineData(0, 25, 20)]   // only a natural 20 works
    [InlineData(15, 10, 20)]  // only a natural 1 fails
    [InlineData(4, 16, 19)]   // widened crit range
    [InlineData(-1, 12, 18)]
    public void Odds_match_exhaustive_enumeration(int mod, int target, int critMin)
    {
        foreach (var mode in Enum.GetValues<RollMode>())
        {
            int wins = 0, total = 0;
            for (int a = 1; a <= 20; a++)
            for (int b = 1; b <= 20; b++)
            {
                int kept = mode switch
                {
                    RollMode.Advantage => Math.Max(a, b),
                    RollMode.Disadvantage => Math.Min(a, b),
                    _ => a
                };
                if (D20.Evaluate(a, b, kept, mod, target, critMin).Success) wins++;
                total++;
            }

            double expected = (double)wins / total;
            Assert.Equal(expected, D20.Odds(mod, target, mode, critMin).Chance, 6);
        }
    }

    [Fact]
    public void Range_text_matches_needed_roll()
    {
        var odds = D20.Odds(modifier: 5, target: 15);
        Assert.Equal(10, odds.Needed);
        Assert.Equal("10-20", odds.RangeText);
        Assert.Equal(55, odds.Percent);
    }

    [Fact]
    public void Natural_one_always_fails_and_twenty_always_crits()
    {
        Assert.False(D20.Evaluate(1, 1, 1, 50, 10, 20).Success);
        var twenty = D20.Evaluate(20, 20, 20, -10, 30, 20);
        Assert.True(twenty.Success);
        Assert.True(twenty.Critical);
    }

    [Fact]
    public void Advantage_and_disadvantage_cancel()
    {
        Assert.Equal(RollMode.Normal, D20.Combine(true, true));
        Assert.Equal(RollMode.Advantage, D20.Combine(true, false));
        Assert.Equal(RollMode.Disadvantage, D20.Combine(false, true));
    }

    [Theory]
    [InlineData("2d6+3", 2, 6, 3)]
    [InlineData("1d8", 1, 8, 0)]
    [InlineData("d20", 1, 20, 0)]
    [InlineData("1d4-1", 1, 4, -1)]
    [InlineData("5", 0, 0, 5)]
    public void Dice_expressions_parse(string text, int count, int sides, int bonus)
    {
        var e = DiceExpr.Parse(text);
        Assert.Equal(new DiceExpr(count, sides, bonus), e);
    }

    [Fact]
    public void Crit_doubles_dice_not_bonus()
    {
        var rng = new Rng(42);
        var e = DiceExpr.Parse("1d1+3"); // a d1 always rolls 1, so totals are exact
        Assert.Equal(4, e.Roll(rng));
        Assert.Equal(5, e.Roll(rng, critical: true));
    }

    [Fact]
    public void Same_seed_same_sequence_and_forks_are_independent()
    {
        var a = new Rng(123);
        var b = new Rng(123);
        for (int i = 0; i < 100; i++) Assert.Equal(a.Roll(20), b.Roll(20));

        var f1 = new Rng(123).Fork("combat");
        var f2 = new Rng(123).Fork("loot");
        bool anyDifferent = false;
        for (int i = 0; i < 20; i++) anyDifferent |= f1.NextUInt() != f2.NextUInt();
        Assert.True(anyDifferent);
    }

    [Fact]
    public void D20_is_roughly_uniform()
    {
        var rng = new Rng(7);
        var counts = new int[21];
        const int n = 200_000;
        for (int i = 0; i < n; i++) counts[rng.Roll(20)]++;
        for (int face = 1; face <= 20; face++)
            Assert.InRange((double)counts[face], n / 20.0 * 0.95, n / 20.0 * 1.05);
    }
}
