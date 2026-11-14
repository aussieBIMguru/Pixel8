# Third-party notices and credits

Pixel8 is an original game. This file lists everything it depends on, everything it borrows from, and the
games that inspired it, so credit is clear and any concern can be raised quickly. The project's own licence
is in [`LICENSE`](LICENSE).

**Spotted a problem?** If you think anything in this repository infringes your copyright, trademark or other
rights, or a credit below is missing or wrong, please open an issue on this GitHub repository or contact the
maintainer, Gavin Nicholls, through GitHub. Reports are taken seriously: material in question will be reviewed
promptly and removed or re-credited if needed.

---

## Software dependencies

These are pulled in through NuGet at build time. They are not stored in the repository.

| Package | Used by | Licence | Source |
|---|---|---|---|
| MonoGame.Framework.DesktopGL 3.8.2 | `Pixel8.Desktop` | Microsoft Public License (Ms-PL), with parts under MIT | https://github.com/MonoGame/MonoGame |
| ↳ native and managed libraries bundled with MonoGame DesktopGL (SDL2, OpenAL Soft, NVorbis) | `Pixel8.Desktop` runtime | SDL2: zlib · OpenAL Soft: LGPL 2.1 (dynamically linked, unmodified) · NVorbis: MIT | https://www.libsdl.org · https://openal-soft.org · https://github.com/NVorbis/NVorbis |
| xunit 2.9 | `Pixel8.Tests` | Apache 2.0 | https://github.com/xunit/xunit |
| xunit.runner.visualstudio 2.8 | `Pixel8.Tests` | Apache 2.0 | https://github.com/xunit/visualstudio.xunit |
| Microsoft.NET.Test.Sdk 17.11 (brings Newtonsoft.Json transitively) | `Pixel8.Tests` | MIT | https://github.com/microsoft/vstest |
| .NET 8 runtime and SDK (System.Text.Json and more) | everything | MIT | https://github.com/dotnet/runtime |

Only `Pixel8.Desktop` ships to players. The test packages are used only during development. Before any public
binary release, re-check the `runtimes/` folder of the build output against this table and copy each bundled
library's licence text into the release.

## Game content and rules

### 5e System Reference Document 5.1 (CC BY 4.0)

Creature names and statistics in `data/enemies.json` (kobold, giant rat, goblin, skeleton, wolf, orc, bugbear,
ogre, bandit, zombie, giant wolf spider, gnoll, hobgoblin, ghoul, owlbear, young green dragon, mimic, and from round 11
troll, wight, gargoyle, basilisk, wraith, mummy, vampire spawn, vampire, hydra, lich), the
d20-versus-AC/DC resolution, advantage/disadvantage, death saves, "Recharge 5–6" abilities, and the spell, feature
and item names used on cards (Burning Hands, Magic Missile, Shield, Bless, Cure Wounds, Sacred Flame, Chromatic Orb,
Scorching Ray, Thunderwave, Ray of Sickness, Mage Armor, Fireball, Lightning Bolt, Arcane Recovery, Guiding Bolt,
Healing Word, Shield of Faith, Spiritual Weapon, Prayer of Healing, Spirit Guardians, Mass Healing Word,
Channel Divinity, Action Surge, Extra Attack, Uncanny Dodge, Fast Hands, Alchemist's Fire, Basic Poison,
Potion of Heroism) come from the SRD 5.1. Some numbers are scaled in game (enemy HP, the dragon's breath damage)
and some abilities are simplified; the published values are kept in the data files.

Gear in `data/gear.json` (round 10) uses SRD 5.1 equipment and magic item names: the weapons and armor (dagger,
quarterstaff, mace, shortsword, rapier, longsword, battleaxe, greatsword, leather, studded leather, scale mail,
breastplate, half plate, chain mail, plate, shield), +1/+2 weapons, armor and shields, Flame Tongue, Dagger of
Venom, Vicious Weapon, Staff of Withering, Mace of Disruption, Berserker Axe, Mithral Armor, Elven Chain, Robe of
Eyes, Sentinel Shield, Wand of the War Mage, Cloak of Protection, Gauntlets of Ogre Power, Stone of Good Luck,
Pearl of Power, Ring of Protection, Amulet of Health, Ring of Regeneration and Bracers of Defense. Their game
effects are simplified and rebalanced. The other gear names (Robes, Off-hand Dagger, Crystal Orb, Bone Charm, Lucky
Rabbit's Foot) are generic descriptions. The gear event names and texts are original.

Round 11 adds, from the SRD 5.1: the Bard, Ranger, Druid and Paladin classes and the features and spells named on
their cards (Bardic Inspiration, Cutting Words, Jack of All Trades, Vicious Mockery, Healing Word, Faerie Fire,
Heroism, Thunderwave, Hold Person, Hypnotic Pattern, Hunter's Mark, Colossus Slayer, Volley, Horde Breaker, Escape
the Horde, Natural Explorer, Goodberry, Produce Flame, Wild Shape, Entangle, Cure Wounds, Moonbeam, Barkskin,
Flaming Sphere, Call Lightning, Mass Cure Wounds, Lay on Hands, Divine Smite, Branding Smite, Divine Favor, Bless,
Aura of Protection, Sacred Weapon); the Wild Shape beasts (wolf, brown bear, dire wolf); the creatures troll,
wight, gargoyle, basilisk, wraith, mummy, vampire spawn, vampire, hydra and lich, with their abilities (Petrifying
Gaze, Life Drain, Rotting Fist, Regeneration, Paralyzing Touch, Cone of Cold, Ray of Frost, Frightening Gaze); and
the longbow, shortbow, light crossbow and Oathbow. The other new card names (Rallying Tune, Discordant Note, Barbed
Arrow, Snaring Shot, Storm Arrow, Arrow Storm, Twin Shot, Thorn Lash, Thunder Strike, Holy Wrath, Sworn Challenge,
Radiant Wave) and gear names (Lute, Fine Lute, Silver Harp, Druidic Focus, Woodland Totem, Holy Symbol, Reliquary)
are original or generic. The Beholder is Wizards of the Coast product identity and not in the SRD, so it isn't
used.

> This work includes material taken from the System Reference Document 5.1 ("SRD 5.1") by Wizards of the
> Coast LLC and available at https://dnd.wizards.com/resources/systems-reference-document. The SRD 5.1 is
> licensed under the Creative Commons Attribution 4.0 International License available at
> https://creativecommons.org/licenses/by/4.0/legalcode.

Pixel8 uses no Wizards of the Coast product identity (no trademarks, logos, settings, named characters or
monsters outside the SRD) and is not affiliated with, endorsed or sponsored by Wizards of the Coast.
"Dungeons & Dragons" and "D&D" are trademarks of Wizards of the Coast LLC and are named here only to describe
the source of the SRD.

### Colour palette

The 16-colour palette (`Rendering/Palette.cs`) uses the colour values of the PICO-8 palette by Lexaloffle Games
(https://www.lexaloffle.com/pico-8.php). No PICO-8 code, carts, fonts or other assets are used. Pixel8 is not
affiliated with Lexaloffle.

### Art, font and audio

- All sprites are original 8×8 pixel art, defined in code in `src/Pixel8.Desktop/Rendering/Sprites.cs`.
- The 3×5 pixel font in `src/Pixel8.Desktop/Rendering/TinyFont.cs` is original.
- All sound is synthesised in code; there are no audio files. The sound effects (`src/Pixel8.Desktop/Fx/Sfx.cs`)
  and the music (`data/music/*.mml`, rendered by `src/Pixel8.Core/Audio`) are original compositions, written
  for Pixel8 with Claude's assistance. The synth imitates the general sound of 1980s console chips (pulse,
  triangle and noise channels); it uses no code, samples or music from any console, game or other work.
- `tools/music/` is a developer-only Python preview script. It uses NumPy and SciPy (BSD licences) at
  development time, and it doesn't ship with the game.
- When a third-party font, sprite sheet or sound is added, its source and licence must be recorded here in the
  same change.

## Inspirations

Pixel8 is inspired by the games below. It borrows broad, general mechanics (genre conventions, which are not
protected) and copies no text, art, card names, characters, rules text or other expression from any of them.
None of their owners is affiliated with or endorses Pixel8.

| Work | Owner | What inspired us |
|---|---|---|
| Munchkin | Steve Jackson Games | Kicking open unknown dungeon rooms; light-hearted, swingy encounters |
| Slay the Spire | Mega Crit | Deck-building run structure, visible enemy intents, card draft after fights, campfire rest/upgrade, merchant with card removal |
| Dungeons & Dragons 5th edition | Wizards of the Coast | d20 checks, advantage/disadvantage, conditions, death saves (used through the SRD 5.1 above) |
| Undertale | Toby Fox | Timed defence: pressing at the right moment to reduce incoming damage |
| PICO-8 | Lexaloffle Games | The 8-pixel aesthetic and palette (see above) |

## Development assistance

Much of Pixel8's design documentation and source code was written with help from **Claude**, an AI assistant
by Anthropic (https://www.anthropic.com), working with and directed by the maintainer. All AI-assisted work is
reviewed, built and play-tested by the maintainer, who is responsible for the project. The assistant was told
to use only original or properly licensed material, as listed above. If you believe any generated content
resembles your work too closely, please use the contact route at the top of this file.
