using Pixel8.Core.Cards;
using Pixel8.Core.Combat;
using Pixel8.Core.Content;
using Pixel8.Core.Dungeon;
using Pixel8.Core.Gear;
using Pixel8.Core.Heroes;
using Pixel8.Core.Rewards;

namespace Pixel8.Core.Runs;

/// <summary>
/// A saved run (session 7), written as JSON between map moves. Fights aren't saved mid-way: quitting during a fight
/// resumes from the last map save, before the party stepped onto that tile. Bump <see cref="CurrentVersion"/> when
/// the format changes; older saves are then refused rather than half-loaded.
/// </summary>
public sealed class RunSave
{
    /// <summary>2 = round 10 (gear, pack, mimics).</summary>
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;
    public ulong Seed { get; set; }
    public string DifficultyId { get; set; } = "normal";
    public int Depth { get; set; } = 1;
    public TimingMode Timing { get; set; }
    public ShopMode Shop { get; set; }
    public List<string> Classes { get; set; } = new();

    public int Gold { get; set; }
    public int Luck { get; set; }
    public int Torch { get; set; }
    public int Xp { get; set; }
    public int Level { get; set; } = 1;
    public List<string> Bag { get; set; } = new();
    public List<GearSave> Pack { get; set; } = new();
    /// <summary>Debug mode was on at some point: achievements don't count for this run.</summary>
    public bool Debugged { get; set; }
    public int Flees { get; set; }
    public List<HeroSave> Heroes { get; set; } = new();
    /// <summary>Hero indices still to pick a level-up reward.</summary>
    public List<int> PendingLevelUps { get; set; } = new();

    public uint[] CombatRng { get; set; } = Array.Empty<uint>();
    public uint[] EncounterRng { get; set; } = Array.Empty<uint>();
    public uint[] LootRng { get; set; } = Array.Empty<uint>();

    /// <summary>Filled in by the map screen.</summary>
    public MapSave? Map { get; set; }
}

public sealed class HeroSave
{
    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public bool Dead { get; set; }
    public List<CardSave> Hand { get; set; } = new();
    public List<CardSave> Draw { get; set; } = new();
    public List<CardSave> Discard { get; set; } = new();
    public List<CardSave> Exhausted { get; set; } = new();
    /// <summary>Equipped gear by slot (round 10).</summary>
    public Dictionary<GearSlot, GearSave> Gear { get; set; } = new();
    /// <summary>Max HP from gear, already inside MaxHp.</summary>
    public int GearHp { get; set; }
}

public sealed class GearSave
{
    public string Id { get; set; } = "";
    public int Bonus { get; set; }
}

public sealed class CardSave
{
    public string Id { get; set; } = "";
    public bool Up { get; set; }
}

/// <summary>Map state on top of the regenerated dungeon (same seed + reroll): tile changes, position, shops.</summary>
public sealed class MapSave
{
    public int Reroll { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int PrevX { get; set; } = -1;
    public int PrevY { get; set; } = -1;
    public List<TileSave> Tiles { get; set; } = new();
    public List<int[]> UsedCampfires { get; set; } = new();
    public List<MerchantSave> Merchants { get; set; } = new();
    public MerchantSave? DepthMerchant { get; set; }
    public string Message { get; set; } = "";
}

public sealed class TileSave
{
    public int X { get; set; }
    public int Y { get; set; }
    public TileKind Kind { get; set; }
    public bool Visited { get; set; }
    public bool Exposed { get; set; }
    public bool Mimic { get; set; }
}

public sealed class MerchantSave
{
    public int X { get; set; }
    public int Y { get; set; }
    public List<OfferSave> Offers { get; set; } = new();
}

public sealed class OfferSave
{
    public ShopKind Kind { get; set; }
    public int Price { get; set; }
    public int Hero { get; set; } = -1;
    public string? Card { get; set; }
    public string? Item { get; set; }
    public GearSave? Gear { get; set; }
    public bool Sold { get; set; }
}

/// <summary>Capture and restore helpers that need access to Core internals.</summary>
public static class SaveGame
{
    public static List<CardSave> Cards(IEnumerable<CardInstance> cards) =>
        cards.Select(c => new CardSave { Id = c.BaseDef.Id, Up = c.Upgraded }).ToList();

    public static IEnumerable<CardInstance> Cards(ContentDb content, IEnumerable<CardSave> cards) =>
        cards.Where(c => content.Cards.ContainsKey(c.Id)).Select(c => new CardInstance(content.Card(c.Id), c.Up));

    public static HeroSave Hero(Hero h) => new()
    {
        Hp = h.Hp,
        MaxHp = h.MaxHp,
        Dead = h.IsDead,
        Hand = Cards(h.Deck.Hand),
        Draw = Cards(h.Deck.DrawPile),
        Discard = Cards(h.Deck.Discard),
        Exhausted = Cards(h.Deck.Exhausted),
        Gear = Enum.GetValues<GearSlot>().Where(s => h.Loadout[s] is not null).ToDictionary(s => s, s => Gear(h.Loadout[s]!)),
        GearHp = h.GearHp
    };

    public static GearSave Gear(GearInstance g) => new() { Id = g.Def.Id, Bonus = g.Bonus };

    public static GearInstance? Gear(ContentDb content, GearSave? s) =>
        s is not null && content.Gear.TryGetValue(s.Id, out var def) ? new GearInstance(def, s.Bonus) : null;

    public static void Restore(Hero h, HeroSave s, ContentDb content)
    {
        h.MaxHp = Math.Max(1, s.MaxHp);
        h.IsDead = s.Dead;
        h.IsDowned = false;
        h.IsStable = false;
        h.Hp = s.Dead ? 0 : Math.Clamp(s.Hp, 1, h.MaxHp);
        h.Block = 0;
        h.Conditions.Clear();
        h.Deck.Restore(Cards(content, s.Hand), Cards(content, s.Draw), Cards(content, s.Discard), Cards(content, s.Exhausted));
        // Gear: MaxHp above already includes the gear's max HP, so restore without adjusting it.
        foreach (var slot in Enum.GetValues<GearSlot>())
            h.SetSlot(slot, s.Gear.TryGetValue(slot, out var g) ? Gear(content, g) : null);
        h.RestoreGearHp(s.GearHp);
        h.RecalculateGear(adjustHp: false);
    }

    public static MerchantSave Merchant(Merchant m, Party party, int x = -1, int y = -1) => new()
    {
        X = x,
        Y = y,
        Offers = m.Offers.Select(o => new OfferSave
        {
            Kind = o.Kind,
            Price = o.Price,
            Hero = o.Hero is null ? -1 : IndexOf(party, o.Hero),
            Card = o.Card?.Id,
            Item = o.Item?.Id,
            Gear = o.Gear is null ? null : Gear(o.Gear),
            Sold = o.Sold
        }).ToList()
    };

    public static Merchant Merchant(MerchantSave s, Party party, ContentDb content) =>
        Rewards.Merchant.Restore(s.Offers
            .Where(o => o.Kind is not ShopKind.Card || (o.Card is not null && content.Cards.ContainsKey(o.Card) && o.Hero >= 0 && o.Hero < party.Heroes.Count))
            .Where(o => o.Kind is not ShopKind.Item || (o.Item is not null && content.Items.ContainsKey(o.Item)))
            .Where(o => o.Kind is not ShopKind.Gear || (o.Gear is not null && content.Gear.ContainsKey(o.Gear.Id)))
            .Select(o => new ShopOffer
            {
                Kind = o.Kind,
                Price = o.Price,
                Hero = o.Kind == ShopKind.Card ? party.Heroes[o.Hero] : null,
                Card = o.Kind == ShopKind.Card ? content.Card(o.Card!) : null,
                Item = o.Kind == ShopKind.Item ? content.Items[o.Item!] : null,
                Gear = o.Kind == ShopKind.Gear ? Gear(content, o.Gear) : null,
                Sold = o.Sold
            }));

    private static int IndexOf(Party party, Hero hero)
    {
        for (int i = 0; i < party.Heroes.Count; i++)
            if (ReferenceEquals(party.Heroes[i], hero)) return i;
        return -1;
    }
}
