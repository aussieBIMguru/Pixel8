using Pixel8.Core.Randomness;

namespace Pixel8.Core.Cards;

public enum CardSpeed { Action, Quick, Reaction }

public enum Rarity { Common, Uncommon, Rare, Legendary }

public enum CheckKind { None, Attack, Save, Skill }

/// <summary>
/// One effect in a card's effect list, loaded from JSON, e.g.
/// { "type": "Attack", "when": "always", "args": { "dice": "1d8", "stat": "STR" } }.
/// "type" is looked up in the EffectRegistry (M1); "when" is always | onHit | onCrit | onMiss.
/// </summary>
public sealed record EffectDef(string Type, string When = "always", Dictionary<string, string>? Args = null);

/// <summary>
/// The "upgrade" block of a card in cards.json: what changes when a copy is upgraded (campfire or merchant).
/// Anything left out keeps the base card's value. The upgraded copy keeps the base name and shows a "+".
/// </summary>
public sealed record CardUpgradeDef(string? Text = null, IReadOnlyList<EffectDef>? Effects = null);

/// <summary>Immutable card data. Lives in data/cards.json.</summary>
public sealed record CardDef(
    string Id,
    string Name,
    string ClassId,
    CardSpeed Speed,
    CheckKind Check,
    Rarity Rarity,
    string Text,
    IReadOnlyList<EffectDef> Effects,
    bool Exhaust = false,
    CardUpgradeDef? Upgrade = null,
    bool Spell = false,
    string? Short = null,
    string? Fx = null)
{
    // Fx: the hit effect the screen draws (slash, stab, fire, missile, radiant, bolt, ...). See ContentDb.KnownFx.
    // Spell: an Attack card rolls the hero's spell attack (SpellDc − 8) instead of their weapon attack.
    // Short: a label of up to 6 characters for small buttons (class basic actions).

    public bool CanUpgrade => Upgrade is not null;

    /// <summary>The card as it plays once upgraded (the same card if it has no upgrade block).</summary>
    public CardDef Upgraded() => Upgrade is null
        ? this
        : this with { Text = Upgrade.Text ?? Text, Effects = Upgrade.Effects ?? Effects, Upgrade = null };
}

/// <summary>A specific copy of a card in a hero's deck (can be upgraded independently).</summary>
public sealed class CardInstance
{
    private static int _nextUid;
    private CardDef? _upgradedDef;

    public CardInstance(CardDef def, bool upgraded = false)
    {
        BaseDef = def;
        Upgraded = upgraded;
        Uid = Interlocked.Increment(ref _nextUid);
    }

    public int Uid { get; }

    /// <summary>The card as printed in cards.json.</summary>
    public CardDef BaseDef { get; }

    /// <summary>The card as it plays: the upgraded version once <see cref="Upgraded"/> is set.</summary>
    public CardDef Def => Upgraded && BaseDef.Upgrade is not null ? (_upgradedDef ??= BaseDef.Upgraded()) : BaseDef;

    public bool Upgraded { get; set; }

    /// <summary>Not yet upgraded, and the card has an upgrade block.</summary>
    public bool CanUpgrade => !Upgraded && BaseDef.CanUpgrade;

    public string DisplayName => Upgraded ? BaseDef.Name + "+" : BaseDef.Name;

    public override string ToString() => DisplayName;
}

/// <summary>
/// A hero's personal deck / hand / discard.
/// Key design rule: the draw pile does NOT reshuffle itself when empty.
/// Only a campfire Restock (or specific cards) recycles the discard pile.
/// </summary>
public sealed class HeroDeck
{
    private readonly List<CardInstance> _draw = new();
    private readonly List<CardInstance> _hand = new();
    private readonly List<CardInstance> _discard = new();
    private readonly List<CardInstance> _exhausted = new();

    public HeroDeck(IEnumerable<CardInstance> cards, Rng rng, int maxHand = 5)
    {
        MaxHand = maxHand;
        _draw.AddRange(cards);
        rng.Shuffle(_draw);
    }

    public int MaxHand { get; set; }

    public IReadOnlyList<CardInstance> DrawPile => _draw;
    public IReadOnlyList<CardInstance> Hand => _hand;
    public IReadOnlyList<CardInstance> Discard => _discard;
    public IReadOnlyList<CardInstance> Exhausted => _exhausted;

    /// <summary>Replaces all four piles (loading a saved run). Order is kept: the last card of the draw pile is drawn next.</summary>
    internal void Restore(IEnumerable<CardInstance> hand, IEnumerable<CardInstance> draw,
        IEnumerable<CardInstance> discard, IEnumerable<CardInstance> exhausted)
    {
        _hand.Clear(); _hand.AddRange(hand);
        _draw.Clear(); _draw.AddRange(draw);
        _discard.Clear(); _discard.AddRange(discard);
        _exhausted.Clear(); _exhausted.AddRange(exhausted);
    }

    /// <summary>Every card the hero owns that isn't exhausted: hand, then draw pile, then discard.</summary>
    public IEnumerable<CardInstance> AllCards => _hand.Concat(_draw).Concat(_discard);

    /// <summary>Draws up to n cards, stopping when the hand is full or the draw pile is empty.</summary>
    public IReadOnlyList<CardInstance> Draw(int n)
    {
        var drawn = new List<CardInstance>();
        while (n-- > 0 && _draw.Count > 0 && _hand.Count < MaxHand)
        {
            var card = _draw[^1];
            _draw.RemoveAt(_draw.Count - 1);
            _hand.Add(card);
            drawn.Add(card);
        }
        return drawn;
    }

    public IReadOnlyList<CardInstance> DrawToFull() => Draw(MaxHand - _hand.Count);

    public void Play(CardInstance card)
    {
        if (!_hand.Remove(card))
            throw new InvalidOperationException($"{card} is not in hand.");
        (card.Def.Exhaust ? _exhausted : _discard).Add(card);
    }

    /// <summary>Campfire "Restock": discard back into the deck, shuffle, draw to full.</summary>
    public IReadOnlyList<CardInstance> Restock(Rng rng)
    {
        _draw.AddRange(_discard);
        _discard.Clear();
        rng.Shuffle(_draw);
        return DrawToFull();
    }

    /// <summary>New cards from rewards are shuffled into the draw pile at a random position.</summary>
    public void AddCard(CardInstance card, Rng rng) => _draw.Insert(rng.Next(_draw.Count + 1), card);

    public bool RemoveCard(CardInstance card) =>
        _draw.Remove(card) || _discard.Remove(card) || _hand.Remove(card);
}
