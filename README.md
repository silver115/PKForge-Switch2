# PKForge for Switch 2

[![GPL-3.0 Licensed](https://img.shields.io/badge/license-GPL--3.0-brightgreen.svg?style=flat-square)](LICENSE)

This is [PKForge](https://github.com/sofianeelhor/PKForge), the Pokémon save editor built on PKHeX.Core, running as
Switch 2 homebrew under kaiHEN. It opens the Pokémon saves stored on the console itself, so there is nothing to export
first, and you edit them with the touch screen or a controller. In this early version, Legends: Z-A is the only game
it can save to; the [games table](#games) shows what has been tested.

> [!WARNING]
> Editing a save can go wrong, and using edited Pokémon online (trades, battles, Pokémon HOME) can get your Nintendo
> Account banned. PKForge refuses to save a Pokémon that PKHeX calls illegal unless you turn on HaX mode, but no save
> editor can promise that an account will never be banned. Back up your saves before you edit, and keep edited Pokémon offline.

## Requirements

- A Switch 2 running [kaiHEN](https://github.com/ouncebrew/kaihen), with the homebrew menu (hbmenu). Tested on
  firmware 23.0.1.
- About 300 MB free on the SD card: the app is 283 MB, and its save backups need room too.
- Touch works in handheld mode. Docked mode has not been tested.

## Installation

1. Download `PKForge.nro` from the latest [release](https://github.com/silver115/PKForge-Switch2/releases).
2. Copy it to the `switch` folder on the SD card, with a card reader or over FTP:

```text
sdmc:/switch/PKForge.nro
```

On first launch, PKForge creates `sdmc:/switch/PKForge/` for its settings, restore points, logs and save backups.

## Quick start

1. On the HOME Menu, hold R while you start any game, then pick PKForge in hbmenu.
2. Home lists the Pokémon games that have a save on the console, for every user on it. Tap one to open its boxes.
3. Put the cursor on a Pokémon, press **+** and choose **Edit**. Text fields, such as Nickname, open the console's
   keyboard.
4. Press **B** to leave the editor and choose **Save changes**.
5. To quit, press **+** on Home and choose **Quit PKForge**.

## Saving and backups

PKForge writes nothing if any of these steps fails:

- A restore point is made first. Each save keeps its newest 20, plus any you pin.
- Before the first save of each session, the whole original save is copied to `sdmc:/switch/PKForge/save-backups/`.
  PKForge never deletes these copies.
- The write is refused if the save changed on the console since PKForge opened it, if the new file is a different
  size or does not fit, or if a Pokémon you changed fails PKHeX's legality check. HaX mode in Settings overrides the
  legality check.

After writing, PKForge reads the save back from the console and compares it. If the two differ, it asks you to go back
to the restore point it made.

To undo a save, open the game's boxes, press **Y** (Save data) and choose **Restore points**.

PKForge only opens the saves of the Pokémon games below. kaiHEN gives homebrew full access to the console's storage,
and kaiHEN's own README warns that a bad homebrew can brick a console.

## Games

| Game | Reading | Writing |
| --- | --- | --- |
| Legends: Z-A | Tested on a console | Yes |
| Violet, Legends: Arceus, Let's Go Pikachu | Tested on a console | Not yet |
| Scarlet, Let's Go Eevee | Same save format as Violet and Let's Go Pikachu | Not yet |
| Brilliant Diamond, Shining Pearl | Tested on a PC with test saves | Not yet |
| Sword, Shield | Not tested | Not yet |

Writing is switched on for a game only after a console test shows that the game loads a save PKForge wrote. Until
then, saving in that game shows a message instead.

## Controls

| Where | Button | Action |
| --- | --- | --- |
| Home | **A** | Open the selected game |
| Home | **+** | Settings (HaX mode, restore points, quitting) |
| Boxes | D-pad | Move the cursor |
| Boxes | **L** / **R** | Previous or next box |
| Boxes | **A** | Grab or place a Pokémon |
| Boxes | **+** | Menu for the Pokémon under the cursor (Summary, Edit, Move, and more) |
| Boxes | **Y** | Save data, including restore points |
| Boxes | **−** | Select several Pokémon |
| Anywhere | **B** | Back |

## Limitations

- Home, Saves, the box browser, the Pokémon editor and restore points have been checked on Switch 2. PKForge's
  other screens open but have not been checked yet.
- No internet features yet: no update check, no downloads, no QR import, and no music.

## Reporting problems

Open an [issue](https://github.com/silver115/PKForge-Switch2/issues) and attach the logs from
`sdmc:/switch/PKForge/logs/`. The logs include your console's account IDs, so look through them before you post them
publicly.

## Credits

- [PKForge](https://github.com/sofianeelhor/PKForge) by sofianeelhor: this port runs its screens and engine.
- PKHeX.Core by Kaphotics, through PKForge's fork
  [PKForge-PKHeX](https://github.com/sofianeelhor/PKForge-PKHeX), and
  [PKHeX-Plugins](https://github.com/santacrab2/PKHeX-Plugins) for legality fixes.
- [mono-nx](https://github.com/exelix11/mono-nx) by exelix11, which runs .NET on the console.
- [kaiHEN](https://github.com/ouncebrew/kaihen) and the libounce toolchain by ouncebrew.
- SkiaSharp, which draws every screen.

## License

GPL-3.0, the same as PKForge and PKHeX. See [LICENSE](LICENSE). The app also contains other open-source software,
listed with its licences in [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES.md).
