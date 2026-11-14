using Pixel8.Core.Cards;

namespace Pixel8.Desktop.Rendering;

/// <summary>
/// Round 12: 8×8 card icons (<c>card.&lt;kind&gt;</c>, plus a few shared <c>gear.*</c> ones), and the rule that
/// picks one from a card's effects, hit effect (fx) and damage type. New cards need no art: anything the rules
/// don't recognise gets a sword (attacks), a swirl (saves) or a star.
/// </summary>
internal static class CardIcons
{
    public static readonly (string Name, string[] Rows)[] Defs =
    {
        ("card.arrow", new[] { "........", ".....777", "......77", ".....7.7", "....4...", "...4....", ".c4.....", "cc......" }),
        ("card.flame", new[] { "...8....", "...88...", "..8988..", ".889a8..", ".89aa98.", ".89aa98.", "..8998..", "...88..." }),
        ("card.sun", new[] { "a..a..a.", ".a.a.a..", "..aaa...", "aaa7aaa.", "..aaa...", ".a.a.a..", "a..a..a.", "........" }),
        ("card.bolt", new[] { "....aa..", "...aa...", "..aa....", ".aaaaa..", "...aa...", "..aa....", ".aa.....", ".a......" }),
        ("card.star", new[] { "...d....", "...d....", "..ddd...", "ddd7ddd.", "..ddd...", "...d....", "...d....", "........" }),
        ("card.swirl", new[] { "..eeee..", ".e....e.", "e..ee..e", "e.e..e.e", "e.e.ee.e", "e..e...e", ".e....e.", "..eeee.." }),
        ("card.drop", new[] { "...b....", "...b....", "..bbb...", ".bb3bb..", ".b3bbb..", ".bbbbb..", "..bbb...", "........" }),
        ("card.vine", new[] { "....3...", "...3.b..", "..3.bb..", "..3.....", "...3....", ".bb.3...", ".b..3...", "...3...." }),
        ("card.fang", new[] { "........", "88888888", "8.7..7.8", "..7..7..", "........", "..7..7..", "8.7..7.8", "88888888" }),
        ("card.note", new[] { "....ccc.", "....c.cc", "....c...", "....c...", "..ccc...", ".cccc...", ".ccc....", "........" }),
        ("card.heart", new[] { "........", ".88.88..", "8888888.", "8888888.", ".88888..", "..888...", "...8....", "........" }),
        ("card.paw", new[] { ".4..4...", "44..44..", "........", "4.444.4.", "..444...", ".44444..", ".44444..", "........" }),
        ("card.target", new[] { "..8888..", ".8....8.", "8..77..8", "8.7887.8", "8.7887.8", "8..77..8", ".8....8.", "..8888.." }),
        ("card.eye", new[] { "........", "..6666..", ".6.77.6.", "6.7007.6", ".6.77.6.", "..6666..", "........", "........" }),
        ("card.chain", new[] { "........", ".66.....", "6..6....", "6..666..", ".666..6.", "....6..6", "....6..6", ".....66." }),
        ("card.scroll", new[] { ".777777.", "76666667", ".655556.", ".666666.", ".655556.", ".666666.", "76666667", ".777777." }),
        ("card.die", new[] { "........", ".777777.", ".707707.", ".777777.", ".707707.", ".777777.", ".707707.", "........" }),
        ("card.halo", new[] { "..aaaa..", ".a....a.", "..aaaa..", "........", "...ff...", "..ffff..", "..ffff..", "...ff..." }),
        ("card.wing", new[] { "........", "7.......", "77......", "777.....", ".7777...", "..77777.", "...7777.", "....77.." }),
        ("card.shout", new[] { "...88...", "...88...", "...88...", "...88...", "...88...", "........", "...88...", "........" }),
    };

    /// <summary>The sprite name for a card (one of <see cref="Defs"/> or a shared gear icon).</summary>
    public static string For(CardDef card)
    {
        var fx = card.Effects;
        bool Has(string type) => fx.Any(e => e.Type.Equals(type, StringComparison.OrdinalIgnoreCase));
        string? Arg(EffectDef e, string key) => e.Args is not null && e.Args.TryGetValue(key, out var v) ? v : null;
        bool deals = Has("Damage") || Has("WeaponDamage");

        if (Has("WildShape")) return "card.paw";
        if (!deals && (Has("Heal") || Has("LayOnHands") || Has("Stabilize"))) return "card.heart";
        if (Has("Inspire")) return "card.note";
        if (Has("HuntersMark")) return "card.target";
        if (Has("Smite")) return "card.sun";
        if (Has("FixNextDie")) return "card.die";

        switch (card.Fx?.ToLowerInvariant())
        {
            case "radiant": return "card.sun";
            case "arrow": return "card.arrow";
            case "fire": return "card.flame";
            case "smash": return "gear.mace";
            case "shield": return "gear.shield";
            case "bolt": return "card.bolt";
            case "slash": return "gear.sword";
            case "stab": return "gear.dagger";
            case "poison": return "card.drop";
            case "vine": return "card.vine";
            case "bite": return "card.fang";
            case "heal": return "card.heart";
            case "missile": return "card.star";
            case "song": return "card.note";
        }

        foreach (var e in fx.Where(e => e.Type.Equals("Damage", StringComparison.OrdinalIgnoreCase)))
            switch (Arg(e, "type")?.ToLowerInvariant())
            {
                case "fire": return "card.flame";
                case "radiant": return "card.sun";
                case "thunder" or "lightning": return "card.bolt";
                case "force": return "card.star";
                case "psychic": return "card.swirl";
                case "poison": return "card.drop";
                case "piercing": return "card.arrow";
            }
        if (Has("Poison")) return "card.drop";

        foreach (var e in fx.Where(e => e.Type.Equals("Apply", StringComparison.OrdinalIgnoreCase)))
            switch (Arg(e, "cond")?.ToLowerInvariant())
            {
                case "marked": return "card.target";
                case "outlined": return "card.eye";
                case "restrained" or "prone" or "paralyzed": return "card.chain";
                case "charmed": return "card.swirl";
                case "blessed": return "card.halo";
                case "taunting": return "card.shout";
                case "cunning": return "card.wing";
            }

        if (Has("Block") || Has("TempHp")) return "gear.shield";
        if (Has("ModifyIncomingRoll")) return "card.wing";
        if (Has("WeaponDamage") || card.Check == CheckKind.Attack) return "gear.sword";
        if (Has("Draw")) return "card.scroll";
        if (card.Check == CheckKind.Save) return "card.swirl";
        return "card.star";
    }
}
