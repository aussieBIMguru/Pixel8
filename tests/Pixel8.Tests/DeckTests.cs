using Pixel8.Core.Cards;
using Pixel8.Core.Randomness;

namespace Pixel8.Tests;

public class DeckTests
{
    private static HeroDeck NewDeck(int cards, out Rng rng)
    {
        rng = new Rng(1);
        var def = new CardDef("strike", "Strike", "fighter", CardSpeed.Action, CheckKind.Attack,
            Rarity.Common, "Deal weapon damage.", Array.Empty<EffectDef>());
        return new HeroDeck(Enumerable.Range(0, cards).Select(_ => new CardInstance(def)), rng);
    }

    [Fact]
    public void Draw_stops_at_max_hand()
    {
        var deck = NewDeck(12, out _);
        deck.Draw(20);
        Assert.Equal(5, deck.Hand.Count);
        Assert.Equal(7, deck.DrawPile.Count);
    }

    [Fact]
    public void Empty_draw_pile_does_not_reshuffle_until_restock()
    {
        var deck = NewDeck(6, out var rng);
        deck.DrawToFull();
        foreach (var c in deck.Hand.ToList()) deck.Play(c);
        deck.Draw(5);
        Assert.Single(deck.Hand);          // only one card was left in the pile
        Assert.Empty(deck.DrawPile);
        Assert.Equal(5, deck.Discard.Count);

        deck.Restock(rng);
        Assert.Equal(5, deck.Hand.Count);
        Assert.Empty(deck.Discard);
    }
}
