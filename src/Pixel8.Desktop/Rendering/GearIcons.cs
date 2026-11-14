using Pixel8.Core.Gear;

namespace Pixel8.Desktop.Rendering;

/// <summary>
/// Round 12: 8×8 gear icons (<c>gear.&lt;kind&gt;</c>), same format as <see cref="SpriteSheet"/>, and the rule that
/// picks one for a piece of gear from its slot, armor kind, tags and id. New gear needs no art: anything the
/// rules don't recognise falls back to its slot's icon.
/// </summary>
internal static class GearIcons
{
    public static readonly (string Name, string[] Rows)[] Defs =
    {
        // Weapons
        ("gear.sword", new[] { "......77", ".....767", "....767.", "a..767..", ".a767...", "..a6....", ".4.a....", "4......." }),
        ("gear.greatsword", new[] { ".....777", "....7767", "...7767.", "a.7767..", ".a767...", "..aa....", ".44.a...", "44......" }),
        ("gear.dagger", new[] { "........", "........", ".....77.", "....767.", "...767..", "..a7....", ".4.a....", "4......." }),
        ("gear.staff", new[] { "......c.", ".....cac", "......c4", ".....4..", "....4...", "...4....", "..4.....", ".4......" }),
        ("gear.mace", new[] { ".....66.", "....6556", "....6556", "....466.", "...4....", "..4.....", ".4......", "4......." }),
        ("gear.axe", new[] { "...66...", "..6664..", ".66664..", ".6664...", "..6.4...", "....4...", "....4...", "....4..." }),
        ("gear.bow", new[] { "..44....", ".4.6....", "4..6....", "4..6....", "4..6....", "4..6....", ".4.6....", "..44...." }),
        ("gear.crossbow", new[] { "........", "6......6", ".6....6.", "..6446..", "...44...", "...44...", "...44...", "........" }),
        // Armor
        ("gear.robe", new[] { "..2..2..", ".222222.", "22222222", "2.2222.2", "..2222..", "..2a22..", ".222222.", ".222222." }),
        ("gear.leather", new[] { "........", ".44..44.", ".444444.", "44444444", "4.4444.4", "..4444..", "..4444..", "........" }),
        ("gear.mail", new[] { "........", ".66..66.", ".656565.", "65656565", "6.5656.6", "..6565..", "..5656..", "........" }),
        ("gear.plate", new[] { "........", ".77..77.", ".766667.", "77666677", "7.7667.7", "..6776..", "..6666..", "........" }),
        // Off-hand
        ("gear.shield", new[] { ".666666.", "6cccccc6", "6cc66cc6", "6c6aa6c6", "6cc66cc6", ".6cccc6.", "..6cc6..", "...66..." }),
        ("gear.orb", new[] { "........", "..cccc..", ".c7ccdc.", ".cccdcc.", ".ccdccc.", "..cccc..", "..4444..", ".444444." }),
        ("gear.wand", new[] { "......a.", ".....a7a", "......a.", ".....4..", "....4...", "...4....", "..4.....", "........" }),
        ("gear.lute", new[] { "......5.", ".....45.", "....4...", ".994....", "9999....", "9049....", "9999....", ".99....." }),
        ("gear.harp", new[] { ".aaaa...", ".a7.a...", ".a7..a..", ".a7...a.", ".a7...a.", ".a7....a", ".aaaaaaa", "........" }),
        ("gear.totem", new[] { "....b...", "...bbb..", "..bb3bb.", "...b3b..", "....4...", "....4...", "...44...", "........" }),
        ("gear.holy", new[] { "...aa...", "..a..a..", "..a..a..", "...aa...", ".aaaaaa.", "...aa...", "...aa...", "...aa..." }),
        // Trinkets
        ("gear.ring", new[] { "...8....", "..a8a...", ".a...a..", ".a...a..", ".a...a..", "..aaa...", "........", "........" }),
        ("gear.amulet", new[] { ".a....a.", "..a..a..", "...aa...", "...88...", "..8ee8..", "..8ee8..", "...88...", "........" }),
        ("gear.cloak", new[] { "..2222..", ".2a22a2.", ".222222.", "22222222", "22222222", "2222.222", "2.22.2.2", "........" }),
        ("gear.gauntlets", new[] { "........", ".6.6.6..", ".666666.", ".666666.", ".6666666", ".666666.", "..4444..", "..4444.." }),
        ("gear.bracers", new[] { "........", ".444444.", ".4aaaa4.", ".444444.", ".444444.", ".4aaaa4.", ".444444.", "........" }),
        ("gear.gem", new[] { "........", "..cccc..", ".c77ccc.", "cc7ccccc", ".cccccc.", "..cccc..", "...cc...", "........" }),
        ("gear.pearl", new[] { "........", "..6666..", ".677766.", ".677666.", ".666666.", ".666665.", "..6555..", "........" }),
        ("gear.bone", new[] { "........", ".7....7.", "777..777", ".777777.", ".777777.", "777..777", ".7....7.", "........" }),
        ("gear.foot", new[] { "...f....", "..fff...", "..fff...", "..fff...", "..fff...", ".ffff4..", ".ffff...", "..44...." }),
    };

    /// <summary>The sprite name for a piece of gear (always one of <see cref="Defs"/>).</summary>
    public static string For(GearDef def)
    {
        string id = def.Id.ToLowerInvariant();
        bool Has(string word) => id.Contains(word);
        bool Tagged(string tag) => def.Tags?.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)) == true;

        switch (def.Slot)
        {
            case GearSlot.Weapon:
                if (Has("crossbow")) return "gear.crossbow";
                if (Has("bow") || def.Ranged) return "gear.bow";
                if (Has("dagger")) return "gear.dagger";
                if (Has("staff")) return "gear.staff";
                if (Has("mace") || Has("club")) return "gear.mace";
                if (Has("axe")) return "gear.axe";
                if (Has("greatsword") || def.TwoHanded) return "gear.greatsword";
                return "gear.sword";
            case GearSlot.Armor:
                return def.Armor switch
                {
                    ArmorKind.Cloth => "gear.robe",
                    ArmorKind.Light => "gear.leather",
                    ArmorKind.Medium => "gear.mail",
                    ArmorKind.Heavy => "gear.plate",
                    _ => Has("robe") ? "gear.robe" : "gear.leather"
                };
            case GearSlot.OffHand:
                if (def.IsShield || Tagged("shield")) return "gear.shield";
                if (Has("dagger")) return "gear.dagger";
                if (Has("wand")) return "gear.wand";
                if (Has("harp")) return "gear.harp";
                if (Has("lute") || Tagged("instrument")) return "gear.lute";
                if (Tagged("druidic")) return "gear.totem";
                if (Tagged("holy")) return "gear.holy";
                return "gear.orb";
            default:
                if (Has("ring")) return "gear.ring";
                if (Has("amulet") || Has("necklace")) return "gear.amulet";
                if (Has("cloak")) return "gear.cloak";
                if (Has("gauntlet")) return "gear.gauntlets";
                if (Has("bracer")) return "gear.bracers";
                if (Has("pearl")) return "gear.pearl";
                if (Has("bone")) return "gear.bone";
                if (Has("rabbit") || Has("foot")) return "gear.foot";
                return "gear.gem";
        }
    }
}
