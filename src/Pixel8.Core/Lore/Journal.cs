using Pixel8.Core.Combat;
using Pixel8.Core.Runs;

namespace Pixel8.Core.Lore;

/// <summary>
/// The Adventurer's Journal (round 11): every card, piece of gear and foe the player has seen, across all runs.
/// Seen means offered, owned, shown in a shop or loot choice, or met in a fight. The desktop saves it as JSON.
/// (The namespace is "Lore" so the class can be called Journal-something without clashing.)
/// </summary>
public sealed class JournalState
{
    public HashSet<string> Cards { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Gear { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Enemies { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Times each enemy was defeated (by enemy id).</summary>
    public Dictionary<string, int> Defeated { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Notes what the player sees. Runs that had debug mode on don't add entries (like achievements).</summary>
public sealed class JournalTracker
{
    public JournalTracker(JournalState state) => State = state;

    public JournalState State { get; }

    /// <summary>Set when something new was added, so the desktop knows to save.</summary>
    public bool Dirty { get; set; }

    public const string CardKind = "card", GearKind = "gear", EnemyKind = "enemy";

    /// <summary>Hook for <see cref="Run.Noticed"/>: a card, gear or enemy id the run just showed the player.</summary>
    public void Note(string kind, string id)
    {
        var set = kind switch
        {
            CardKind => State.Cards,
            GearKind => State.Gear,
            EnemyKind => State.Enemies,
            _ => null
        };
        if (set is not null && set.Add(id)) Dirty = true;
    }

    /// <summary>Everything the party owns right now: deck cards (exhausted ones too) and gear.</summary>
    public void Observe(Run run)
    {
        if (run.Debugged) return;
        Note(CardKind, Content.ContentDb.BasicStrikeId);
        Note(CardKind, Content.ContentDb.BasicGuardId);
        foreach (var h in run.Party.Heroes)
        {
            foreach (var c in h.Deck.AllCards.Concat(h.Deck.Exhausted)) Note(CardKind, c.BaseDef.Id);
            if (h.Class.BasicAction is { Length: > 0 } basic) Note(CardKind, basic);
            foreach (var g in h.Loadout.Equipped) Note(GearKind, g.Def.Id);
        }
        foreach (var g in run.Party.Pack) Note(GearKind, g.Def.Id);
    }

    /// <summary>After a fight: every foe in it is seen; the fallen ones count as defeated on a win.</summary>
    public void FightEnded(Run run, CombatState combat)
    {
        if (run.Debugged) return;
        foreach (var e in combat.AllEnemies)
        {
            Note(EnemyKind, e.Def.Id);
            if (combat.Phase == CombatPhase.Victory && !e.IsAlive)
            {
                State.Defeated[e.Def.Id] = State.Defeated.GetValueOrDefault(e.Def.Id) + 1;
                Dirty = true;
            }
        }
    }

    public int DefeatedCount(string enemyId) => State.Defeated.GetValueOrDefault(enemyId);

    /// <summary>Debug: reveal everything in the content (to check the journal screens).</summary>
    public void RevealAll(Content.ContentDb content)
    {
        foreach (var id in content.Cards.Keys) State.Cards.Add(id);
        foreach (var id in content.Gear.Keys) State.Gear.Add(id);
        foreach (var id in content.Enemies.Keys) State.Enemies.Add(id);
        Dirty = true;
    }

    public void Reset()
    {
        State.Cards.Clear();
        State.Gear.Clear();
        State.Enemies.Clear();
        State.Defeated.Clear();
        Dirty = true;
    }
}
