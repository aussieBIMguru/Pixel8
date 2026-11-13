using Pixel8.Core.Randomness;

namespace Pixel8.Core.Dice;

/// <summary>A damage/effect dice expression such as "2d6+3", "1d8", "1d4-1" or a flat "5".</summary>
public readonly record struct DiceExpr(int Count, int Sides, int Bonus)
{
    public static DiceExpr Parse(string text)
    {
        string s = text.Replace(" ", "").ToLowerInvariant();
        int d = s.IndexOf('d');
        if (d < 0) return new DiceExpr(0, 0, int.Parse(s));

        int count = d == 0 ? 1 : int.Parse(s[..d]);
        int signPos = s.IndexOfAny(new[] { '+', '-' }, d);
        int sides;
        int bonus = 0;
        if (signPos < 0)
        {
            sides = int.Parse(s[(d + 1)..]);
        }
        else
        {
            sides = int.Parse(s[(d + 1)..signPos]);
            bonus = int.Parse(s[signPos..]);
        }
        return new DiceExpr(count, sides, bonus);
    }

    /// <summary>Rolls the expression. A critical doubles the dice, not the flat bonus (5e rule).</summary>
    public int Roll(Rng rng, bool critical = false) => Roll(rng, critical, out _);

    /// <summary>Same as <see cref="Roll(Rng, bool)"/> (identical RNG use), but also returns each die face for the roll strip.</summary>
    public int Roll(Rng rng, bool critical, out int[] faces)
    {
        int dice = critical ? Count * 2 : Count;
        faces = new int[dice];
        int total = Bonus;
        for (int i = 0; i < dice; i++)
        {
            faces[i] = rng.Roll(Sides);
            total += faces[i];
        }
        return Math.Max(0, total);
    }

    public double AverageDice => Count * (Sides + 1) / 2.0;

    public double Average => AverageDice + Bonus;

    public DiceExpr WithBonus(int extra) => this with { Bonus = Bonus + extra };

    public override string ToString()
    {
        if (Count == 0) return Bonus.ToString();
        string b = Bonus switch { > 0 => $"+{Bonus}", < 0 => Bonus.ToString(), _ => "" };
        return $"{Count}d{Sides}{b}";
    }
}
