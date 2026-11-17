# tgui reference (MIT files only)

A copy of the parts of /tg/station's tgui that are MIT licensed, so values can be read without the tg repo.

- **Source:** `tgstation` at commit `e6c9da8511c11d6bb696a306685885e972a079ca` (2026-10-16), `tgui/`.
- **Licence:** `LICENSE-tgui-core.txt` (MIT, "TG Station"). `tgui-core` is MIT as a package; the `packages/tgui` files here
  carry an `@license MIT` or `SPDX-License-Identifier: MIT` header.

| Folder | Source path | What |
| --- | --- | --- |
| `tgui-core/styles/` | `node_modules/tgui-core/styles/` | Everything: `vars-values.scss` (spacing, sizes), `vars-colors.scss`, `vars-components.scss` (per-component tokens), `colors.scss`, `base.scss`, `functions.scss`, `components/*.scss` (one per component), `atomic/*.scss` (candystripe, colour and text helpers), `themes/*.scss` (syndicate, wizard, ntos...) |
| `tgui-core/dist/components/*.d.ts` | `node_modules/tgui-core/dist/components/` | The props of every component. Types only, no JS bundles |
| `tgui-core/dist/common/format.*` | `node_modules/tgui-core/dist/common/` | `formatSiUnit`, `formatPower`, `formatEnergy`, `formatMoney`, `toFixed`-style helpers (small, readable) |
| `tgui/layouts/` | `packages/tgui/layouts/` | `Window.tsx`, `Layout.tsx`, `Pane.tsx`, `NtosWindow.tsx`. `TitleBar.tsx` has no licence header, so it is not here |
| `tgui/styles/` | `packages/tgui/styles/` | `base`, `colors`, `functions`, `main`, `reset` and `layouts/*.scss` (Window, TitleBar, Ntos...). `css-variables.scss` has no header, so it is not here |
| `tgui/styles/interfaces/` | `packages/tgui/styles/interfaces/` | The four interface stylesheets that carry their own MIT header: `AlertModal`, `ListInput`, `RequestManager` (bobbahbrown) and `ColorPicker` (Vlad Shilov). Their headers stay in the files |

## Deliberately not copied

- **Every interface** (`packages/tgui/interfaces/*.tsx`) and the rest of `styles/interfaces/*.scss`: AGPL-3.0 (tgui's README,
  "License"). `designing.md` in the parent folder describes the windows in words, with their tg paths.
- `tgui-panel`, `tgui-say` (AGPL), `dist/*.js` bundles (minified).

KS14 is MIT, so copying AGPL code or SCSS into the repo is not allowed. Read it, then write the value yourself.
