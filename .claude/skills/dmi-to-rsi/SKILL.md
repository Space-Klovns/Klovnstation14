---
name: dmi-to-rsi
description: Convert a BYOND DreamMaker Image (.dmi, from SS13 codebases like tgstation, Goonstation, Paradise) into an SS14 .rsi with a bundled script, a port of RSIEdit's DMI import - including its "Taken from <repo> at <url>" copyright autofill, licence, state-name cleanup, delays and rewind. Use when porting or borrowing a sprite, effect, icon or animation from an SS13 repo, when a .dmi needs to become an .rsi, or when filling in an RSI's license/copyright for something taken from another repo.
---

# DMI to RSI

`dmi_to_rsi.py` (beside this file) converts a `.dmi` into an `.rsi` directory. It ports the DMI import from
[space-wizards/RSIEdit](https://github.com/space-wizards/RSIEdit), most of which lives in its `RSI.NET` submodule
(`SpaceWizards.RsiLib/DMI/...`). Needs Python 3 and Pillow. Run it from the repo root:

```sh
python .claude/skills/dmi-to-rsi/dmi_to_rsi.py <input.dmi> <output-dir> [--license CC-BY-SA-3.0] [--copyright "..."]
                                               [--repo URL --commit HASH [--repo-path PATH]] [--keep-names] [--indent 2] [--force]
```

It writes `<output-dir>/<dmi stem>.rsi/`, containing `meta.json` and one PNG per state.

## Workflow

1. **Find the source.** The user usually has the SS13 repo cloned locally (tgstation lives under
   `C:/Space/SS13 Dev/Repo/tgstation` on the main dev machine). Ask if you can't find it. Don't download a
   `.dmi` from a raw URL unless asked: the copyright needs a commit hash anyway.
2. **Pin the commit.** `git -C <ss13 repo> rev-parse HEAD`, and the remote's web URL from
   `git -C <ss13 repo> remote get-url origin` (strip `.git`).
3. **Check the licence.** Most SS13 sprites are `CC-BY-SA-3.0` (the default), but not all: check the repo's licence
   file and any per-folder notes. Paradise, Goonstation and some others carry `CC-BY-NC-SA-3.0` for some or all of
   their art, which may not be usable here. Ask the user if it isn't CC-BY-SA.
4. **Convert into `_KS14`.** Output goes under `Resources/Textures/_KS14/<Feature>/` (CONTRIBUTING §2). No `Ks`
   prefix on the filename: the path already namespaces it.
   ```sh
   python .claude/skills/dmi-to-rsi/dmi_to_rsi.py "C:/Space/SS13 Dev/Repo/tgstation/icons/mob/telegraphing/telegraph.dmi" \
       "Resources/Textures/_KS14/Effects" \
       --repo https://github.com/tgstation/tgstation --commit e6c9da8511c11d6bb696a306685885e972a079ca
   ```
5. **Look at it.** Read a PNG or two to check the states are what you wanted (Read renders images). Delete states
   nothing will use, and fix them out of `meta.json` too.
6. **Validate.** `python Tools/_KS14/validate_rsis.py Resources/Textures/_KS14/<Feature>` checks the RSI against the
   schema and its PNG sizes.

## Options

- `--license`: the license to write. Defaults to `CC-BY-SA-3.0`, tgstation's sprite license.
- `--repo` and `--commit`: when given without `--copyright`, the copyright is filled in RSIEdit's format,
  `Taken from <repo name> at <repo URL>/blob/<commit>/<path in repo>`. For example:
  `Taken from tgstation at https://github.com/tgstation/tgstation/blob/e6c9da85.../icons/mob/telegraphing/telegraph.dmi`.
  The path in the repo is found by walking up from the DMI to the folder holding `.git`. Pass `--repo-path` to give it
  yourself.
- `--copyright`: any other text, verbatim. Add who modified it (`, modified by ...`) if you edit the PNGs afterwards.
- `--keep-names`: keeps state names as they are. By default the tool lowercases names and turns spaces into `_`, as
  RSIEdit's URL import does (`ReformatAllNames`). Characters that aren't valid in a filename always become `_`.
- `--indent`: the `meta.json` indentation. Defaults to 2, which matches SS14's existing files (RSIEdit's default is 4).
- `--force`: overwrite an existing `.rsi` at the destination.

## What it does, following RSIEdit

- **Reading the DMI.** DMI cells are read left to right, then top to bottom, and the count carries on from one state
  into the next. Within a state the cells are frame-major: every direction of frame 0, then every direction of frame 1.
  Directions come in the order S, N, E, W, SE, SW, NE, NW, which is also the RSI's order.
- **Writing the RSI.** A state's PNG is direction-major: every frame of the first direction, then every frame of the
  next. It is laid out on a grid `round(sqrt(n))` cells wide and `ceil(sqrt(n))` cells tall.
- **Delays.** Delays are converted from ticks to seconds (divided by 10). A state with several frames but no `delay`
  line gets 0.1 s per frame. Extra delays beyond the frame count are dropped.
- **What it leaves out of `meta.json`.** `directions` is left out when it's 1. `delays` is left out when the DMI gives
  none, which means a single-frame state, or when every direction's delays are exactly `[1]`.
- **`rewind`.** It is baked in as ping-pong frames: `0 1 2` becomes `0 1 2 1 0`, and the delays are mirrored to match.

## Where it deliberately differs from RSIEdit

- **Empty state names.** RSIEdit writes these as `.png`. This tool names them `empty`.
- **Duplicate state names.** RSIEdit overwrites the earlier state's PNG. This tool appends `_movement` to a movement
  state (BYOND's movement states share their twin's name), then `_2`, `_3`, and so on. Names are compared
  case-insensitively.
- **Fewer delays than frames.** RSIEdit throws. This tool pads the missing delays with 0.1 s and warns.
- **`rewind = 0`.** RSIEdit treats any `rewind` line as true. This tool treats `rewind = 0` as false.

## What an RSI can't represent

`movement`, `loop` (play count), `hotspot` and any other unknown keys are dropped, with a warning on stderr, as RSIEdit
drops them. SS14 animations always loop.
