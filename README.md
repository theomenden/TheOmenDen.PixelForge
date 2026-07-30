# Pixel Forge

A Windows app for turning a big pile of character art into finished sprite sheets.

You point it at three art packs, pick which pieces you want, and it bakes every combination into
sheets a game can load. It also lets you build your own skin color sets and preview animations
before you commit to a long export.

This guide is for using the app. If you want to work on the code instead, read `CLAUDE.md`.

## Table of contents

1. [What you need first](#what-you-need-first)
2. [Starting the app](#starting-the-app)
3. [First run: point it at your art](#first-run-point-it-at-your-art)
4. [A tour of the five pages](#a-tour-of-the-five-pages)
5. [Your first export, step by step](#your-first-export-step-by-step)
6. [What ends up in the output folder](#what-ends-up-in-the-output-folder)
   - Making a set of characters: [Creating heroes, step by step](docs/creating-heroes.md)
7. [How to read a finished sheet](#how-to-read-a-finished-sheet)
8. [The two Roost buttons](#the-two-roost-buttons)
9. [Where the app keeps your settings](#where-the-app-keeps-your-settings)
10. [When something goes wrong](#when-something-goes-wrong)
11. [Keyboard and screen readers](#keyboard-and-screen-readers)

## What you need first

**Windows 11.** The app uses newer Windows features and will not run on Windows 10.

**The three Time Elements art packs.** These are:

- the main pack
- Character Expansion 1
- Character Expansion 2

They are not bundled with Pixel Forge. The license for that art does not let us hand it out, so
you need your own copy of each one. Baked output is yours to use, but the raw packs stay with you.

Each pack contains a folder called `assets`. Inside it you will see one folder per body part, with
names like `head`, `top`, `bottom`, `hair`, `hat`, and `shadow`. That `assets` folder is the one
you will point the app at. If you pick the folder above it, the app will not find anything.

## Starting the app

**If you were given an installer**, install it and start Pixel Forge from the Start menu like any
other app.

**If you are running it from the source code**, open a terminal in the project folder and run:

```powershell
dotnet run --project src/TheOmenDen.PixelForge
```

Do not double-click the built `.exe` in the output folder. Pixel Forge is a packaged app, and
launching the raw executable makes it close again immediately with no error. Use the installed
shortcut or `dotnet run`.

## First run: point it at your art

The first time you open the app, every page except Settings will be empty and will show a yellow
bar telling you why. That is expected. Do this once and it sticks:

1. Click **Settings** at the bottom of the left sidebar.
2. Under **Source packs**, click **Browse** next to **Core pack**.
3. Choose the `assets` folder inside your main Time Elements pack.
4. Do the same for **Character Expansion 1** and **Character Expansion 2**.

Each row shows the path once it is set. If a row reads **Not set**, the app has nothing for it. If
it reads a path followed by **(missing)**, the folder was set but has since been moved, renamed, or
deleted, so pick it again.

Once all three are set, the app scans them and the other pages fill in. You do not need to restart.

## A tour of the five pages

### Canvas

The start of a pixel drawing tool. **Drawing is not switched on yet.** The pencil, fill, and eraser
buttons are there, but clicking them does nothing so far. Everything useful today lives on the next
three pages.

### Assets

A browser for the art. Use it to see what a piece looks like before you export a few hundred files
built from it.

- **Component** picks which part of a character you are looking at, such as hair or top. The number
  beside each one is how many files that part has.
- The grid shows a still of each file. Click one to load it into the preview.
- **Clip** picks which animation plays. A play arrow means the clip actually moves. A pause icon
  means it is a single pose that other animations borrow frames from.
- **Facing** turns the character. North faces away from you. It previews fine, but the game-ready
  sheet leaves it out, and the app says so right in the list.
- **Composite** decides whether the piece is drawn over a plain body. Leave it on for hats, hair,
  and weapons, which are impossible to judge floating in space. Turn it off when you want to check
  that one piece lines up correctly on its own.
- **Playback** starts and stops the animation. Pause it when you want to study one frame.

The line under the preview tells you how many frames the clip has, how long each frame is held, and
how long one loop takes.

### Palette

Skin colors come in sets of five shades, from darkest to lightest. Recoloring a character swaps each
shade for the matching shade in the set you chose, so the order matters.

Seven sets come with the app. You cannot edit those, but you can copy them:

1. Pick a set in the list.
2. Click **Duplicate**. You now have an editable copy.
3. Rename it in the **Name** box.
4. Click a swatch to pick a shade, or type a hex code like `#DBA463` into the box beside it.
5. Watch the preview. It updates as you go, before you save anything.
6. Click **Save ramp**. Nothing is kept until you do.

**Import CSV** and **Export CSV** move your sets in and out as a plain spreadsheet file, which is
handy for backups or for sharing a set with someone else.

One thing to know: the skin tone list on the Pipeline page is built when the app starts. If you add
a new set here and want to export with it, restart the app first.

### Pipeline

This is the batch export page, and the reason the app exists. It is covered step by step in the next
section.

### Settings

Two things live here. **App theme** switches between light and dark, or follows whatever Windows is
set to. **Source packs** is where the three folder paths live. Both take effect right away.

## Your first export, step by step

**1. Choose where the files go.** Click **Browse** next to **Output folder**. A fresh empty folder is
the safest pick, because files with the same name get overwritten without asking.

**2. Choose a mode.**

- **Curated** is the trimmed, game-ready sheet. Pick this one if you are not sure.
- **Full** is the untrimmed sheet with every frame and every direction the original art has.
- **Both** writes one of each, which doubles your file count.

The line under the buttons explains whichever mode is selected.

**3. Name your characters.** The **Hero prefix** box names new character folders. A prefix of
`villager` gives you `villager_01`, `villager_02`, and so on. It is required, and the Export button
stays off until it holds something usable. Blank is refused, and so are `heroes`, `attachments`,
`loadouts`, and `curated`, because the export folder already uses those names.

The **Class name** box beside it is optional. It names the set of equipment you ticked, such as
`ranger`. Leave it empty and the run still writes every layer, it just does not name the set.

**4. Tick the parts you want.** Each section below the preview is one part of a character. Open one
and tick the files you want in it. Three things are worth knowing here:

- **Bodies multiply, equipment does not.** Three bottoms with one top and one head is three
  characters. Nine hairstyles stay nine files however many characters you have, because a hairstyle
  is drawn once and shared. Watch the label near the Export button as you go.
- **A section left empty is skipped**, so a character can go without a hat. The exceptions are
  bottom, top, and head, which go together: fill all three, or leave all three empty for equipment
  with no character.
- **The Filter box brings rows into view.** Long lists do not always load every row, and the Hat and
  Weapon sections are the most likely to be short. Typing part of a name shortens the list and the
  row appears. Clearing the box keeps your ticks.

The **Filter** box in each section shortens a long list. It only hides rows, so anything you ticked
stays ticked when you clear it.

The **Base only** and **All colours** switch decides whether a tick also brings in the ready-made
recolors of that file. On a hairstyle with eight color variants, flipping it turns one tick into
eight files.

**5. Tick your skin tones.** More than one tone means one sheet per character per tone. This only
affects parts that show skin, so hair and clothes keep the colors they were drawn in, and the
equipment count does not change.

**6. Check the count.** The label near the Export button describes the run as a sentence, like
`2 characters x 8 skin tones + 9 equipment layers = 25 files`. If it reads **Nothing to export yet**,
the Export button stays turned off, and usually a body has only one or two of its three parts
ticked. Over a thousand files, the app lets you start but warns you that it will take a while.

**7. Click Export.** The bar fills as sheets land, and the number beside it counts up. Each row you
ticked gets a note showing its file size, or the reason it failed.

You can leave the page or use the rest of the app while a run goes. **Cancel** stops it. Sheets
already written stay on disk, and the app still writes their list files so nothing is left
unaccounted for.

## What ends up in the output folder

A run writes sheets into folders by what they are, and leaves small files beside them that describe
what it made:

```
heroes/villager_01/     one character, one sheet per skin tone
attachments/hair/       one sheet per hairstyle, shared by every character
loadouts/ranger.json    which gear a named class offers
```

| File | What it is |
| --- | --- |
| `heroes/<name>/*.webp` | A character's sheets. The one with no tone in its name is the tone the art was drawn in. |
| `attachments/<part>/*.webp` | One sheet per piece of equipment, drawn once and shared. Hair, hats, and weapons live here. |
| `loadouts/<class>.json` | Which pieces a named class offers. Written only when you name a class. |
| `heroes.json` | Which clothes each character folder holds. A folder called `villager_01` does not say on its own, so this is the only place that is written down. Keep it. |
| `heroes.csv` | The same list, for a spreadsheet. |
| `classes.csv` | One row per class, listing the gear it offers. |
| `index.csv` | Which row of a game-ready sheet holds which animation and facing. Written when the run produced game-ready sheets. |
| `clips.csv` | The same idea for the full, untrimmed sheets. Written when the run produced those. |
| `sheets.csv` | One row per sheet: what went into it and what tone it used. |
| `manifest.json` | The same run described as JSON, for anything that would rather read that. |
| `pixelforge-*.json` | The rules each of the JSON files above follows, copied in so you can check a file without this app. |

Equipment is not copied into each character folder. A hairstyle looks the same on everybody, so it
is written once and your game draws it over whichever character you like. **[Creating heroes, step
by step](docs/creating-heroes.md)** walks through a full run and shows how a game reads the result.

Every sheet is read back after it is written and compared against what was meant to be there. That
is what "no quality loss" means here: it is checked, not assumed. A sheet that fails the check is
reported instead of quietly shipped.

## How to read a finished sheet

Every frame is a 48 by 48 pixel cell.

**Game-ready sheets** are 240 by 1152 pixels: 5 cells across and 24 down. There are eight
animations, each drawn facing three ways, and the three facings sit on consecutive rows. In order,
the animations are walk, idle, arms up, crouch, jump, attack, heavy attack, and sleep or knocked
out. Within each animation, the facings run south, then west, then east. So walk facing south is
row 0, walk facing west is row 1, walk facing east is row 2, and idle facing south starts at row 3.

**Full sheets** are 1104 by 192 pixels: 23 cells across and 4 down, one row per facing, in the order
south, west, east, north. This is the shape of the original art, kept as-is.

Frames are meant to be held for 300 milliseconds each, which is the pace this art was drawn for.
`index.csv` and `manifest.json` both carry that number so a game does not have to guess.

The away-facing direction is dropped from game-ready sheets on purpose. Nothing in the game ever
walks away from the camera, so those frames would just be wasted space.

## The two Roost buttons

These two sit next to each other and do different jobs, which trips people up.

**Roost set (079)** does not export anything. It ticks the exact art the Roost avatar set is built
from and turns on every skin tone. Use it when you want to see that selection, or change one piece
of it and export your own version.

**Export 079 set** writes the finished deliverable: sixteen sheets, seven bodies and nine
hairstyles, under the exact names the game looks for. It ignores your ticks and the mode buttons on
purpose, because those names and that layout are a promise to whatever loads them. If it followed
your picks, it could ship files the game could not use.

So: use the first one to explore, and the second one to hand something over.

## Where the app keeps your settings

Your folder paths, your color sets, and the app's logs live together in one place:

```
%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalState\
```

- `packs.json` holds the three folder paths.
- `ramps.csv` holds the color sets you made.
- `logs\` holds a log file per day, keeping the last 14.

You can copy `ramps.csv` somewhere safe as a backup, or use **Export CSV** on the Palette page,
which does the same thing without hunting for the folder.

One note about the logs: they are written in batches for speed, and the last few lines are flushed
when the window closes. If you need a complete log, close the app window instead of killing the
process from Task Manager.

## When something goes wrong

**Every page is empty and there is a yellow bar.** One or more of the three pack folders is not set.
Go to Settings and set them.

**A pack path says (missing).** The folder moved, was renamed, or was deleted. Click Browse on that
row and pick it again.

**The pack path is set but nothing shows up.** You most likely picked the folder above `assets`
instead of `assets` itself. The right folder is the one containing `head`, `top`, `bottom`, and the
other part folders.

**Export is greyed out.** Three things have to be true: all three packs set, an output folder
chosen, and the file count above zero. The count sits right next to the button, so check it first.
Bottom, top, and head each need at least one tick.

**A section is missing from the Pipeline page.** Sections only appear when at least one of your
packs has art for that part. Character Expansion 2 has no front extra art, for example, so if that
is the only pack you set, that section will not appear.

**Some sheets failed.** The message tells you how many were written and how many failed, and each
ticked row shows its own reason. A run that fails partway still writes the list files for the sheets
that did land.

**A new color set is not in the skin tone list.** That list is built when the app starts. Restart it.

## Keyboard and screen readers

You can reach every control with `Tab`, and `Space` or `Enter` activates whatever is focused.
`Shift+Tab` goes back.

Every button, box, and switch has a spoken name, so a screen reader announces what it is rather
than reading out a raw file name. Hovering over most controls shows a short note about what that
control does, which is often faster than looking it up here.

The app follows your Windows light or dark setting by default, and works in high contrast mode. If
you find something hard to read in any of those, that is a bug worth reporting.

---

© 2026 The Omen Den
