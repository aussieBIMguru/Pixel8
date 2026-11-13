using Pixel8.Core.Cards;
using Pixel8.Core.Content;
using Pixel8.Core.Heroes;
using Pixel8.Core.Randomness;

namespace Pixel8.Core.Rewards;

public sealed record DraftOffer(Hero Hero, CardDef Card);

/// <summary>
/// Post-fight card reward (locked 2026-11-13): three cards, one per living hero, drawn from that hero's
/// class pool plus neutral cards, weighted by rarity. Pick one (it goes into that hero's deck), or skip for gold.
/// </summary>
public sealed class CardDraft
{
    private CardDraft(IReadOnlyList<DraftOffer> offers) => Offers = offers;

    public IReadOnlyList<DraftOffer> Offers { get; }
    public bool IsResolved { get; private set; }

    public static CardDraft Create(Party party, ContentDb content, Rng rng)
    {
        var offers = new List<DraftOffer>();
        foreach (var hero in party.Heroes.Where(h => !h.IsDead))
        {
            var card = RollCardFor(hero, content, rng);
            if (card is not null)
                offers.Add(new DraftOffer(hero, card));
        }
        return new CardDraft(offers);
    }

    /// <summary>One card from the hero's class pool plus neutral cards, weighted by rarity (draft and merchant).</summary>
    public static CardDef? RollCardFor(Hero hero, ContentDb content, Rng rng, ISet<string>? exclude = null)
    {
        var table = content.Cards.Values
            .Where(c => c.ClassId.Equals(hero.Class.Id, StringComparison.OrdinalIgnoreCase)
                     || c.ClassId.Equals("neutral", StringComparison.OrdinalIgnoreCase))
            .Where(c => exclude is null || !exclude.Contains(c.Id))
            .OrderBy(c => c.Id, StringComparer.Ordinal)
            .Select(c => (c, content.Balance.WeightOf(c.Rarity)))
            .Where(t => t.Item2 > 0)
            .ToList();
        return table.Count > 0 ? rng.Weighted(table) : null;
    }

    public CardInstance Choose(DraftOffer offer, Rng rng)
    {
        if (IsResolved) throw new InvalidOperationException("The draft is already resolved.");
        if (!Offers.Contains(offer)) throw new ArgumentException("That card isn't on offer.", nameof(offer));
        var card = new CardInstance(offer.Card);
        offer.Hero.Deck.AddCard(card, rng);
        IsResolved = true;
        return card;
    }

    /// <summary>Skip the draft for gold. Returns the gold gained.</summary>
    public int Skip(Party party, BalanceDef balance)
    {
        if (IsResolved) throw new InvalidOperationException("The draft is already resolved.");
        party.Gold += balance.SkipDraftGold;
        IsResolved = true;
        return balance.SkipDraftGold;
    }
}
