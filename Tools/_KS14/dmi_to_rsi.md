# dmi-to-rsi

Converts a BYOND DreamMaker Image (`.dmi`, SS13/tgstation) into a Robust Station Image (`.rsi` directory, SS14).
It ports the DMI import from [space-wizards/RSIEdit](https://github.com/space-wizards/RSIEdit). Most of that import
lives in its `RSI.NET` submodule (`SpaceWizards.RsiLib/DMI/...`).

Requires Python 3 and Pillow.

```sh
python Tools/_KS14/dmi_to_rsi.py <input.dmi> <output-dir> [--license CC-BY-SA-3.0] [--copyright "..."]
                                  [--repo URL --commit HASH [--repo-path PATH]] [--keep-names] [--indent 2] [--force]
```

It writes `<output-dir>/<dmi stem>.rsi/`, containing `meta.json` and one PNG per state.

- `--license`: the license to write. Defaults to `CC-BY-SA-3.0`, tgstation's sprite license.
- `--repo` and `--commit`: when given without `--copyright`, the copyright is filled in RSIEdit's format,
  `Taken from <repo name> at <repo URL>/blob/<commit>/<path in repo>`. For example:
  `Taken from tgstation at https://github.com/tgstation/tgstation/blob/e6c9da85.../icons/mob/telegraphing/telegraph.dmi`.
  The path in the repo is found by walking up from the DMI to the folder holding `.git`. Pass `--repo-path` to give it
  yourself.
- `--keep-names`: keeps state names as they are. By default the tool lowercases names and turns spaces into `_`, as
  RSIEdit's URL import does (`ReformatAllNames`). Characters that aren't valid in a filename always become `_`.
- `--indent`: the `meta.json` indentation. Defaults to 2, which matches SS14's existing files (RSIEdit's default is 4).

Example:

```sh
python Tools/_KS14/dmi_to_rsi.py "../tgstation/icons/mob/telegraphing/telegraph.dmi" \
    "Resources/Textures/_KS14/Effects" \
    --repo https://github.com/tgstation/tgstation --commit e6c9da8511c11d6bb696a306685885e972a079ca
```

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
