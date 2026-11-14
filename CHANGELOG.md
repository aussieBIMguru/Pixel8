# Changelog

All notable changes to Pixel8. Versions follow `major.minor.patch`.

## 0.2.0 — unreleased

- **Big rebalance (round 12):** new difficulty targets (most players get through Depths 1–3, Depths 4–5 are a real
  test, Easy stays comfortable). Enemy HP and damage now scale per Depth, and each difficulty can reshape its own
  curve (Hard hits harder early, eases off late). Class tuning: Rogue and Cleric +2 HP, Paladin −2 HP and a smaller
  Lay on Hands pool, Bardic Inspiration 3 uses, lighter Wild Shape temp HP. Hydra and Lich retuned.
- **No endless fights:** from round 11 of a fight, enemy regeneration and Hydra regrowth stop ("TIRED!").
- **Gear and card icons:** every piece of gear has an 8×8 icon on the gear screen, loot and mimic picks, the merchant
  (buy and sell) and the combat gear tooltip; every card shows an icon for what it does (flame, radiant sun, arrow,
  heal, shield, poison, Wild Shape paw…) in your hand, the post-fight draft and the merchant.
- **Sim:** run mode catches stuck fights and stars results that hit the difficulty, boss and class targets; the
  auto-player no longer stalls by alternating Rearm and control cards.

- **Four new classes:** Bard, Ranger, Druid and Paladin (party select is now 3 of 8, in a 4×2 grid that describes
  the class action you point at). Each has a class action, a starting deck, 11 cards of its own (SRD 5.1 names where
  the SRD has them) and starting gear that gives its 5e level 1 numbers.
  - **Bard:** Bardic Inspiration (twice per fight, an ally gets a d6 Inspiration die, spent automatically to turn a
    failed roll into a success), Cutting Words, Faerie Fire, Hold Person, Hypnotic Pattern…
  - **Ranger:** Hunter's Mark (once per fight, +1d6 on your hits; it moves on when the target dies), bows that use
    DEX, Colossus Slayer, Volley, Twin Shot…
  - **Druid:** Produce Flame, Wild Shape (wolf, brown bear, dire wolf: temp HP and the beast's attack until it's
    gone), Entangle, Moonbeam, Call Lightning…
  - **Paladin:** Lay on Hands (a healing pool each fight), Divine Smite (the next hit adds 2d8 radiant, more against
    undead), heavy armor and a shield.
- **New conditions:** Restrained and Outlined (attacks against have advantage), Paralyzed and Charmed (lose a turn).
- **Five Depths** (was three), with every per-Depth table extended, Depth-by-Depth group sizes, retired weak foes in
  the deep, and two new map themes (Sunken Halls, The Cold Crypt). The map shows DEPTH n/5.
- **Depth 4–5 monsters (SRD 5.1):** Troll (regenerates unless burned), Wight (life drain), Gargoyle, the Basilisk
  elite (petrifying gaze); Wraith, Mummy (rotting fist), Vampire Spawn, and the Vampire elite (charm, regeneration).
- **New bosses:** the **Hydra** on Depth 4 bites once per head; a big hit severs a head, and heads grow back unless
  burned. The **Lich** on Depth 5 ends the dungeon: Paralyzing Touch, Cone of Cold, an extra legendary turn each
  round, and at half HP it raises the dead.
- **Achievements:** icons in the menu and the toast, roomier rows, a feat for every class, Head Count (Hydra) and
  Lichbane (Lich); Tour Guide now asks for all eight classes. 35 in all.
- **Adventurer's Journal** (title menu, or J on the map): every card, piece of gear and foe, with full details once
  you've seen it in any run (offered, owned, in a shop or loot, or met in a fight), and how many of each foe you've
  defeated. Saved in `%LocalAppData%/Pixel8/journal.json`.
- **Sim:** 12 mixed parties covering all eight classes, a per-class win table, and Depth 3 / full-run columns.
- **Events that start fights:** an event option can lead into a normal or elite fight (the usual rules, rewards and
  fleeing apply). Sneaking up first gives a **surprise**: heroes attack with advantage in round 1.
- **5 new events:** Toll Rope and Rusted Strongbox (any Depth); Weeping Altar, The Prisoner and Fallen Ceiling
  (Depth 2–3 only).
- Events can cost max HP (never below 1, never downing a hero) and can grant a card of a set rarity.
- **38 new cards:** 8 per class (5 uncommon, 3 rare) and 6 neutral, named after SRD 5.1 spells and features
  (Fireball, Guiding Bolt, Uncanny Dodge, Action Surge, Alchemist's Fire, …). Every card has an upgrade.
- **New card effects:** Poison (damage at the start of each of the target's turns, with a green bubble effect),
  hits on 2 random enemies, and card draw (rare cards only, since decks never reshuffle).
- **8 new SRD creatures:** Bandit, Zombie and Giant Wolf Spider (poison bite) from Depth 1; Gnoll, Hobgoblin and
  Ghoul from Depth 2. Toll Rope fights are bandits.
- **A boss per Depth:** Ogre (1), Owlbear (2) and the Young Green Dragon (3), whose poison bite adds 2d6 and whose
  **Poison Breath** (Recharge 5–6) makes every hero roll a CON save for half, with one timing press for the party.
- **Hit more often:** +1 hero to-hit on Easy and Normal (shown on the roll strip).
- **Less grind (anti-"strike fest"):**
  - **Rearm**, a basic Action for every hero: pick cards from your discard back into your hand (Easy 3, Normal 2,
    Hard 1).
  - **Graze:** a missed attack (not a natural 1) still deals the hero's damage bonus.
  - **Focus:** each miss in a row gives that hero +2 on their next attack, until they hit.
  - **Heroes grow:** +1 to-hit and spell DC at party levels 3, 5, 7 and 9, and +1 damage at levels 2, 4, 6 and 8.
  - **Enemy AC is capped by Depth** in play (14 / 15 / 16); published AC stays in the data. The dragon plays as AC 16.
- **Luck per fight:** every fight starts with Luck by difficulty (Easy 2, Normal 1, Hard 0), spent first. Run Luck
  now starts at 0 and only comes from events; there's no per-fight cap.
- **Debug mode** (hidden, developer only): press Down 20 times on the last menu item to reveal the toggle (from
  round 9: on Back in the Options menu);
  F9 in a run opens a panel to level up, change Depth, start any fight, win a fight, become unkillable, skip
  events or fights while walking, edit decks, rest the party and more.
  The title menu no longer wraps around.
- **Music:** generated chiptune in an NES style (two pulse channels, triangle bass, noise drums), with a title
  theme, a theme for each Depth, combat and boss themes, and victory/defeat stings. Tracks are text files in
  `data/music` that you can edit.
- **Options menu:** the title screen is now Continue / Start / Options / About / Quit. Options holds Difficulty,
  Shop, the new **Music and SFX volume sliders** (0–10; click the bar or use ←/→, replacing Sound on/off) and React
  events. The hidden debug toggle moved there too: press Down 20 times on Back.
- **New sound effects:** footsteps, fog reveals, stairs, campfire, low torch, trap spotted, card draw and play,
  Rearm, Luck, fumbles, Focus, graze, poison, the dragon's roar, recharge, timing cues, coins, items, a level-up
  jingle, buying and backing out.
- **Gear (round 10):** every hero has Weapon / Armor / Off-hand / Trinket slots. Starting gear matches the old
  stats exactly. About 45 pieces use SRD 5.1 names, in Common / Uncommon / Rare. Rares have passives (Flame Tongue
  adds fire, Dagger of Venom poisons, Ring of Regeneration heals each turn, and so on). Classes have light 5e
  proficiencies (Wizards wear robes, Rogues light armor and finesse weapons, Clerics medium armor and shields).
- **Loot:** treasure (40%), elites (50%), bosses (pick 1 of 2) and the odd normal fight drop gear. A shared
  8-slot pack holds what isn't equipped. G on the map opens the gear screen (stat changes previewed); loot can be
  equipped straight away; hover a hero in combat to see their gear.
- **Mimics:** treasure first spotted in the fog may be a mimic (15 / 20 / 25% by Depth; exposed treasure never is).
  Beat it for gold and a pick of 3 Uncommon-or-better pieces.
- **Merchant gear:** 3 pieces per visit (at least 2 your party can use), and a Sell Gear button (half price).
- **4 gear events:** Fallen Adventurer, Smith's Anvil (+1 to a weapon, armor or shield), Whispering Axe (a cursed
  Berserker Axe that only comes off at a campfire; Depth 2+), Armory Rack.
- **Achievements:** 25, tracked on this machine, in an Achievements menu on the title screen, all visible from the
  start. A toast and jingle play when one unlocks. Debug runs don't count; the debug panel can unlock or reset
  them, and can add a random Rare to the pack.
- Saves from before gear can't be continued (save version 2).
- **Sim:** `run` mode plays whole runs on real dungeons for every difficulty, and reports clear rates, fight length
  and how often heroes just Strike. `--sweep` tries several data values in one go.

## 0.1.0 — 2026-11

First playable build.

- **Run:** title screen (Continue, Difficulty, Shop placement, Sound, React events, About), party of 3 from
  Fighter / Rogue / Wizard / Cleric, 3 Depths of hidden 7×9 dungeon with fog of war and a Torch meter, a boss on each
  Depth.
- **Combat:** d20 vs AC/DC with the odds always shown, visible enemy intents, Action / Quick / Reaction slots,
  per-hero decks that never reshuffle, class basic actions, conditions, Downed and death saves, fleeing.
- **Dice roll strip:** every attack and damage roll animated with labelled modifiers; one Luck reroll per fight.
- **Timed reactions:** press in the zone to dodge or soften big attacks (can be turned off).
- **Progression:** card draft after fights, party XP and level-ups (+HP, upgrade or remove a card), campfires
  (rest or upgrade), merchants (cards, potions, removal, upgrades), potions in and out of combat, 7 map events.
- **Look and sound:** 8×8 pixel art on a 256×144 canvas, PICO-8 palette, chunky hit and spell effects, attack and
  cast frames, synthesised chiptune sound effects.
- **Saves:** the run auto-saves on the map; Continue from the title screen.
- **Difficulty:** Easy / Normal / Hard.
- **Installer:** per user with no admin rights, self-contained (no .NET install needed).
