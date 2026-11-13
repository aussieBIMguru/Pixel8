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
ogre), the d20-versus-AC/DC resolution, advantage/disadvantage, death saves and the spell names used on cards
(Burning Hands, Magic Missile, Shield, Bless, Cure Wounds, Sacred Flame) come from the SRD 5.1.

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
- There is no audio yet. When sound or a third-party font or sprite sheet is added, its source and licence
  must be recorded here in the same change.

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
