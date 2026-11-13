using Pixel8.Core.Cards;
using Pixel8.Core.Content;
using Pixel8.Core.Heroes;
using Pixel8.Core.Randomness;

namespace Pixel8.Core.Rewards;

public enum ShopKind { Card, Item, RemoveCard, UpgradeCard }

/// <summary>One thing on the merchant's table. Cards are for a specific hero; services are used once per visit.</summary>
public sealed class ShopOffer
{
    public required ShopKind Kind { get; init; }
    public required int Price { get; init; }
    public Hero? Hero { get; init; }
    public CardDef? Card { get; init; }
    public ItemDef? Item { get; init; }
    public bool Sold { get; set; }

    public string Name => Kind switch
    {
        ShopKind.Card => Card!.Name,
        ShopKind.Item => Item!.Name,
        ShopKind.RemoveCard => "Remove a card",
        _ => "Upgrade a card"
    };
}

/// <summary>
/// The merchant (M4, locked 2026-11-13): one card per living hero (class pool + neutral, weighted by rarity),
/// consumable items, paid card removal (decks never reshuffle, so thinning matters) and a paid upgrade.
/// Prices live in balance.json "prices"; item prices in items.json. The stock is rolled once per merchant tile.
/// </summary>
public sealed class Merchant
{
    private readonly List<ShopOffer> _offers;

    private Merchant(List<ShopOffer> offers) => _offers = offers;

    public IReadOnlyList<ShopOffer> Offers => _offers;

    public static Merchant Create(Party party, ContentDb content, Rng rng)
    {
        var prices = content.Balance.Prices;
        var offers = new List<ShopOffer>();
        var taken = new HashSet<string>();

        foreach (var hero in party.Heroes.Where(h => !h.IsDead))
        {
            var card = CardDraft.RollCardFor(hero, content, rng, taken) ?? CardDraft.RollCardFor(hero, content, rng);
            if (card is null) continue;
            taken.Add(card.Id);
            offers.Add(new ShopOffer { Kind = ShopKind.Card, Hero = hero, Card = card, Price = prices.CardPrice(card.Rarity) });
        }

        foreach (var item in content.Items.Values.OrderBy(i => i.Id, StringComparer.Ordinal))
            for (int i = 0; i < Math.Max(0, prices.ItemStock); i++)
                offers.Add(new ShopOffer { Kind = ShopKind.Item, Item = item, Price = Math.Max(1, item.Price) });

        offers.Add(new ShopOffer { Kind = ShopKind.RemoveCard, Price = prices.RemoveCard });
        offers.Add(new ShopOffer { Kind = ShopKind.UpgradeCard, Price = prices.UpgradeCard });
        return new Merchant(offers);
    }

    /// <summary>Rebuilds a merchant from saved offers (see SaveGame).</summary>
    public static Merchant Restore(IEnumerable<ShopOffer> offers) => new(offers.ToList());

    public static bool CanAfford(Party party, ShopOffer offer) => !offer.Sold && party.Gold >= offer.Price;

    /// <summary>Buys a card (shuffled into that hero's draw pile) or an item (into the Bag).</summary>
    public void Buy(Party party, ShopOffer offer, Rng rng)
    {
        Require(party, offer);
        switch (offer.Kind)
        {
            case ShopKind.Card:
                if (offer.Hero!.IsDead) throw new InvalidOperationException($"{offer.Hero.Name} is dead.");
                offer.Hero.Deck.AddCard(new CardInstance(offer.Card!), rng);
                break;
            case ShopKind.Item:
                party.Bag.Add(offer.Item!.Id);
                break;
            default:
                throw new InvalidOperationException("Use RemoveCard / UpgradeCard for services.");
        }
        Pay(party, offer);
    }

    /// <summary>Paid removal: takes <paramref name="card"/> out of <paramref name="hero"/>'s deck for good.</summary>
    public void RemoveCard(Party party, ShopOffer offer, Hero hero, CardInstance card)
    {
        Require(party, offer, ShopKind.RemoveCard);
        if (!hero.Deck.RemoveCard(card)) throw new InvalidOperationException($"{card} is not in {hero.Name}'s deck.");
        Pay(party, offer);
    }

    /// <summary>Paid upgrade of one card.</summary>
    public void UpgradeCard(Party party, ShopOffer offer, CardInstance card)
    {
        Require(party, offer, ShopKind.UpgradeCard);
        if (!card.CanUpgrade) throw new InvalidOperationException($"{card} can't be upgraded.");
        card.Upgraded = true;
        Pay(party, offer);
    }

    private void Require(Party party, ShopOffer offer, ShopKind? kind = null)
    {
        if (!_offers.Contains(offer)) throw new ArgumentException("That isn't on this merchant's table.", nameof(offer));
        if (kind is not null && offer.Kind != kind) throw new ArgumentException($"Expected a {kind} offer.", nameof(offer));
        if (offer.Sold) throw new InvalidOperationException($"{offer.Name} is sold out.");
        if (party.Gold < offer.Price) throw new InvalidOperationException($"{offer.Name} costs {offer.Price} gold.");
    }

    private static void Pay(Party party, ShopOffer offer)
    {
        party.Gold -= offer.Price;
        offer.Sold = true;
    }
}
