using Pixel8.Core.Cards;
using Pixel8.Core.Combat;
using Pixel8.Core.Runs;

namespace Pixel8.Core.Achievements;

/// <summary>One achievement (round 10). <see cref="Target"/> &gt; 1 makes it cumulative across runs, with a progress bar.</summary>
/// <param name="Icon">Round 11: the 8×8 icon drawn in the menu and the toast (desktop sprite <c>ach.&lt;Icon&gt;</c>).</param>
public sealed record AchievementDef(string Id, string Name, string Description, int Target = 1, string? Counter = null, string Icon = "star");

/// <summary>Everything the player has unlocked, plus the cumulative counters. Saved by the desktop as JSON.</summary>
public sealed class AchievementState
{
    /// <summary>Unlock dates by achievement id (UTC).</summary>
    public Dictionary<string, DateTime> Unlocked { get; set; } = new();
    public Dictionary<string, int> Counters { get; set; } = new();
    /// <summary>Classes that have been in a winning party (Tour Guide).</summary>
    public List<string> ClassesWon { get; set; } = new();

    public bool Has(string id) => Unlocked.ContainsKey(id);
    public int Count(string counter) => Counters.TryGetValue(counter, out int n) ? n : 0;
}

/// <summary>The list, in menu order. All are shown from the start (Gavin, round 10).</summary>
public static class AchievementList
{
    public const string TreasureCounter = "chests", GoldCounter = "goldSpent", UpgradeCounter = "upgrades";

    public static readonly IReadOnlyList<AchievementDef> All = new[]
    {
        new AchievementDef("enter_dungeon", "Enter the Dungeon", "Begin your adventure.", Icon: "door"),
        new AchievementDef("first_win", "One Small Step", "Win your first battle.", Icon: "sword"),
        new AchievementDef("beat_easy", "Piece of Cake", "Beat the dungeon on Easy.", Icon: "cake"),
        new AchievementDef("beat_normal", "As Intended", "Beat the dungeon on Normal.", Icon: "crown"),
        new AchievementDef("beat_hard", "Up for a Challenge", "Beat the dungeon on Hard.", Icon: "skull"),
        new AchievementDef("ogre", "Ogre Toppler", "Defeat the Ogre.", Icon: "club"),
        new AchievementDef("owlbear", "Owlbear Tamer", "Defeat the Owlbear.", Icon: "feather"),
        new AchievementDef("dragon", "Dragonslayer", "Defeat the Young Green Dragon.", Icon: "dragon"),
        new AchievementDef("hydra", "Head Count", "Defeat the Hydra.", Icon: "hydra"),
        new AchievementDef("lich", "Lichbane", "Defeat the Lich and end the dungeon.", Icon: "lich"),
        new AchievementDef("mimic", "That's No Chest", "Beat a mimic.", Icon: "chest"),
        new AchievementDef("shiny", "Ooo Shiny", "Obtain a rare piece of gear.", Icon: "gem"),
        new AchievementDef("armed", "Armed to the Teeth", "Fill all four gear slots on every hero.", Icon: "armor"),
        new AchievementDef("moneybags", "Moneybags", "Finish a run with at least 1000 gold.", Icon: "coins"),
        new AchievementDef("nat20s", "Natural Talent", "Roll three natural 20s in one fight.", Icon: "d20"),
        new AchievementDef("untouchable", "Untouchable", "Perfectly dodge an attack that hits the whole party.", Icon: "shield"),
        new AchievementDef("close_call", "Close Call", "Win a fight after two of your heroes went down.", Icon: "heart"),
        new AchievementDef("ambusher", "Ambusher", "Win a fight you started with a surprise.", Icon: "eye"),
        new AchievementDef("lights_out", "Lights Out", "Win a fight in the dark.", Icon: "moon"),
        new AchievementDef("seasoned", "Seasoned", "Reach party level 8.", Icon: "star"),
        new AchievementDef("deathless", "Deathless", "Win a run without any hero dying.", Icon: "ankh"),
        new AchievementDef("less_is_more", "Less Is More", "Win a run with a hero holding 6 cards or fewer.", Icon: "card"),
        new AchievementDef("no_retreat", "No Retreat", "Win a run without fleeing.", Icon: "flag"),
        // Round 11: one feat per class (won fights only).
        new AchievementDef("feat_fighter", "Not Today", "Fighter: Second Wind at 5 HP or less, then win.", Icon: "sword"),
        new AchievementDef("feat_rogue", "Death by a Thousand Cuts", "Rogue: have three foes poisoned at once and win.", Icon: "dagger"),
        new AchievementDef("feat_wizard", "Chain Reaction", "Wizard: defeat three foes with one card.", Icon: "bolt"),
        new AchievementDef("feat_cleric", "Back on Your Feet", "Cleric: heal a downed ally back up and win.", Icon: "ankh"),
        new AchievementDef("feat_bard", "Encore", "Bard: an Inspiration die turns a failure to success.", Icon: "lute"),
        new AchievementDef("feat_ranger", "Marked for Death", "Ranger: slay three Hunter's Marked foes in a fight.", Icon: "bow"),
        new AchievementDef("feat_druid", "Call of the Wild", "Druid: win a fight while in Wild Shape.", Icon: "paw"),
        new AchievementDef("feat_paladin", "Holy Radiance", "Paladin: land a critical hit with a Divine Smite.", Icon: "sun"),
        new AchievementDef("tour_guide", "Tour Guide", "Win a run with each of the eight classes.", 8, "classes", Icon: "map"),
        new AchievementDef("treasure_hunter", "Treasure Hunter", "Open 50 treasure chests.", 50, TreasureCounter, Icon: "chest"),
        new AchievementDef("big_spender", "Big Spender", "Spend 2000 gold.", 2000, GoldCounter, Icon: "coins"),
        new AchievementDef("card_shark", "Card Shark", "Upgrade 25 cards.", 25, UpgradeCounter, Icon: "card"),
    };

    /// <summary>Round 11: combat feats (CombatState.Feats) and the achievement each unlocks.</summary>
    public static readonly IReadOnlyDictionary<string, string> FeatAchievements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["fighter.last_stand"] = "feat_fighter",
        ["rogue.venom"] = "feat_rogue",
        ["wizard.triple"] = "feat_wizard",
        ["cleric.revive"] = "feat_cleric",
        ["bard.encore"] = "feat_bard",
        ["ranger.marked3"] = "feat_ranger",
        ["druid.wild_win"] = "feat_druid",
        ["paladin.smite_crit"] = "feat_paladin",
    };

    public static AchievementDef Get(string id) => All.First(a => a.Id == id);

    /// <summary>Progress toward a cumulative achievement (0..Target).</summary>
    public static int Progress(AchievementState state, AchievementDef def) =>
        def.Counter == "classes" ? state.ClassesWon.Count
        : def.Counter is null ? (state.Has(def.Id) ? 1 : 0)
        : Math.Min(def.Target, state.Count(def.Counter));
}

/// <summary>
/// Watches runs and unlocks achievements (round 10). The desktop calls <see cref="Check"/> while the map is idle,
/// <see cref="FightEnded"/> after each fight and <see cref="RunEnded"/> at the end. Every call returns the newly
/// unlocked achievements so the screen can show a toast. Runs that had debug mode on don't count.
/// </summary>
public sealed class AchievementTracker
{
    private Run? _run;
    private int _seenChests, _seenSpent, _seenUpgrades;

    public AchievementTracker(AchievementState state) => State = state;

    public AchievementState State { get; }

    /// <summary>Set when something changed, so the desktop knows to save.</summary>
    public bool Dirty { get; set; }

    /// <summary>A run just started (or was continued).</summary>
    public List<AchievementDef> RunStarted(Run run)
    {
        Attach(run);
        var news = new List<AchievementDef>();
        if (!run.Debugged) Unlock("enter_dungeon", news);
        return news;
    }

    /// <summary>State checks: gear, level, and the run's counters (chests, gold spent, upgrades).</summary>
    public List<AchievementDef> Check(Run run)
    {
        Attach(run);
        var news = new List<AchievementDef>();
        var p = run.Party;
        // Counters: add what happened since the last check (the party's counters only grow during a session).
        AddCount(AchievementList.TreasureCounter, p.TreasuresOpened - _seenChests, run, news);
        AddCount(AchievementList.GoldCounter, p.GoldSpent - _seenSpent, run, news);
        AddCount(AchievementList.UpgradeCounter, p.UpgradesMade - _seenUpgrades, run, news);
        _seenChests = p.TreasuresOpened;
        _seenSpent = p.GoldSpent;
        _seenUpgrades = p.UpgradesMade;
        if (run.Debugged) return news;

        if (run.PartyFullyArmed) Unlock("armed", news);
        if (p.AllGear.Any(g => g.EffectiveRarity >= Rarity.Rare)) Unlock("shiny", news);
        if (p.Level >= 8) Unlock("seasoned", news);
        return news;
    }

    public List<AchievementDef> FightEnded(Run run, CombatState combat, bool mimic)
    {
        var news = Check(run);
        if (run.Debugged || combat.Phase != CombatPhase.Victory) return news;

        Unlock("first_win", news);
        if (mimic) Unlock("mimic", news);
        if (combat.Surprise) Unlock("ambusher", news);
        if (combat.Dark) Unlock("lights_out", news);

        int nat20s = combat.Events.OfType<CheckRolled>().Count(c => c.Roller.IsHero && c.Versus == "AC" && c.Result.Kept == 20);
        if (nat20s >= 3) Unlock("nat20s", news);

        int downed = combat.Events.OfType<HeroDowned>().Select(d => d.Hero).Distinct().Count();
        if (downed >= 2) Unlock("close_call", news);

        // A perfect timing press on an area attack (the dragon's breath).
        bool areaPending = false;
        foreach (var e in combat.Events)
        {
            if (e is TimingOffered t) areaPending = t.Intent.Area;
            else if (e is TimingResolved r && areaPending && r.Grade == TimingGrade.Perfect) Unlock("untouchable", news);
        }

        if (combat.Kind == CombatKind.Boss)
            foreach (var e in combat.Enemies.Where(e => !e.IsAlive))
                switch (e.Def.Id)
                {
                    case "ogre": Unlock("ogre", news); break;
                    case "owlbear": Unlock("owlbear", news); break;
                    case "young_green_dragon": Unlock("dragon", news); break;
                    case "hydra": Unlock("hydra", news); break;
                    case "lich": Unlock("lich", news); break;
                }

        foreach (string feat in combat.Feats)
            if (AchievementList.FeatAchievements.TryGetValue(feat, out var id)) Unlock(id, news);
        return news;
    }

    public List<AchievementDef> RunEnded(Run run, bool won)
    {
        var news = Check(run);
        if (run.Debugged) return news;
        if (run.Party.Gold >= 1000) Unlock("moneybags", news);
        if (!won) return news;

        Unlock(run.DifficultyId switch { "easy" => "beat_easy", "hard" => "beat_hard", _ => "beat_normal" }, news);
        var heroes = run.Party.Heroes;
        if (heroes.All(h => !h.IsDead)) Unlock("deathless", news);
        if (heroes.Any(h => !h.IsDead && h.Deck.AllCards.Count() <= 6)) Unlock("less_is_more", news);
        if (run.Flees == 0) Unlock("no_retreat", news);

        // Tour Guide: every class in the winning party counts, fallen heroes included.
        foreach (var h in heroes)
            if (!State.ClassesWon.Contains(h.Class.Id, StringComparer.OrdinalIgnoreCase))
            {
                State.ClassesWon.Add(h.Class.Id);
                Dirty = true;
            }
        if (State.ClassesWon.Count >= AchievementList.Get("tour_guide").Target) Unlock("tour_guide", news);
        return news;
    }

    /// <summary>Debug: unlock everything (to test the menu and toasts).</summary>
    public void UnlockAll()
    {
        foreach (var a in AchievementList.All) State.Unlocked.TryAdd(a.Id, DateTime.UtcNow);
        Dirty = true;
    }

    /// <summary>Debug: forget every unlock and counter.</summary>
    public void Reset()
    {
        State.Unlocked.Clear();
        State.Counters.Clear();
        State.ClassesWon.Clear();
        Dirty = true;
    }

    private void Attach(Run run)
    {
        if (ReferenceEquals(run, _run)) return;
        _run = run;
        _seenChests = run.Party.TreasuresOpened;
        _seenSpent = run.Party.GoldSpent;
        _seenUpgrades = run.Party.UpgradesMade;
    }

    private void AddCount(string counter, int delta, Run run, List<AchievementDef> news)
    {
        if (delta <= 0 || run.Debugged) return;
        State.Counters[counter] = State.Count(counter) + delta;
        Dirty = true;
        foreach (var a in AchievementList.All.Where(a => a.Counter == counter && State.Count(counter) >= a.Target))
            Unlock(a.Id, news);
    }

    private void Unlock(string id, List<AchievementDef> news)
    {
        if (State.Unlocked.ContainsKey(id)) return;
        State.Unlocked[id] = DateTime.UtcNow;
        Dirty = true;
        news.Add(AchievementList.Get(id));
    }
}
