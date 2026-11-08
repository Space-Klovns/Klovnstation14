# Designing an original window: tg's archetypes and numbers

Backing material for the *Designing an original window* section of `SKILL.md`. It is a survey of tg's interfaces, with counts,
so a rule can be checked against what tg does. Written in our own words: tg's interfaces are AGPL and are not copied.

- **Source:** tgstation `e6c9da8511c11d6bb696a306685885e972a079ca`, `tgui/packages/tgui/interfaces/` (651 `.tsx`, 312 of which
  hold a `<Window>`; 273 give a width and height). Paths below are relative to that folder.
- **Method:** scripted counts over every file, plus about 45 interfaces read in full or in skeleton: Apc, Smes, Canister, AirAlarm,
  AtmosPump, SpawnSearch, TextInputModal, ListInputWindow, NumberInputModal, AlertModal, KeyComboModal, CheckboxInput, Vending,
  Cargo, CommunicationsConsole, PersonalCrafting, Techweb, Uplink, CrewConsole, PowerMonitor, SecurityRecords, NtosMain,
  NtosCard, NtosNetDownloader, Fax, ChemDispenser, Cryo, Sleeper, Gps, Timer, Wires, Signaler, SolarControl, ThermoMachine,
  DisposalUnit, Radio, Teleporter, AnomalousWeatherTower, AntagInfoTraitor, AntagInfoGeneric.
- Counts are "uses in files" over all 651 files unless said otherwise. The MIT parts (component SCSS, tokens, `Window.tsx`) are
  in `reference/`.

## Window sizes

Of the 273 windows that give a size: median 500 x 450. Width: 57 are 350 or less, 104 are 351-500, 76 are 501-700, 36 are wider.
Commonest widths: 400 (27), 500 (27), 600 (16), 300 (14), 620 (13, the antagonist greeting windows), 450 (13), 350 (11).
Commonest heights: 500 (29), 600 (28), 400 (19), 300 (18). `Window.Content scrollable` is on 77 windows; the other 235 are
plain, and the tall ones put `<Stack fill vertical>` in them (45 begin that way) and scroll inside a `Section fill scrollable`.
Themes: `admin` 12, `hackerman` 5, `abductor` 4, `syndicate` 4, `ntos` 4, `retro` 4, `wizard` 3. The default Nanotrasen theme is
the rest, so a window sets none.

Small device windows are as short as 115 (AtmosPump, Timer): one Section, no title. Nothing is sized to fit its contents in
tg except the modals, which compute a height from the message length.

## Archetypes

### A. Single column of Sections (a device)

The commonest. 300-450 wide, 115-700 tall.

```
Window 340 x 350
  Window.Content            (scrollable when the sections can exceed the height)
    Section "Stored Energy"     ProgressBar with ranges
    Section "Input"   buttons=[mode toggle]
      LabeledList: "Charge Mode" text in a state colour; "Target Input": Stack { Button <<, Button <, Slider, Button >, Button >> }
    Section "Output"  buttons=[mode toggle]
      LabeledList: the same shape
```

- **Order:** what the machine is doing (status, level) first, then the controls, then any list or contents. Apc: "Power Status",
  "Power Channels", then the rest. Cryo: "Occupant", then "Cell". ThermoMachine: "Status", then "Controls". SolarControl:
  "Status", "Controls". Sleeper: occupant, then "Medicines".
- **The on/off switch is the first Button in the controls Section's `buttons` slot** (ThermoMachine, Canister, Smes), or the first
  row of a lone LabeledList ("Power", AtmosPump, DisposalUnit). It is `icon={on ? 'power-off' : 'times'}`, text `On`/`Off`,
  `selected={on}`.
- **A one-Section window has no title** when the window title already says what it is (AtmosPump 335x115, DisposalUnit,
  Radio, Signaler 280x128, Teleporter 360x130).
- Examples: `Smes.tsx` (340x350), `Canister.tsx` (350x335), `Apc.jsx` (450x445, scrollable), `Cryo.tsx` (400x550, scrollable),
  `ThermoMachine.jsx` (300x350), `SolarControl.tsx` (330x330), `Timer.tsx` (275x115), `Fax.tsx` (340x540, scrollable),
  `Gps.tsx` (470x700, scrollable).

### B. Sidebar of tabs/categories plus a content list

For catalogues. 400-800 wide, 500-750 tall. Nothing is scrollable at the window level; each column is a `Section fill`.

```
Window 700 x 720
  Window.Content
    Stack fill                         (horizontal)
      Stack.Item width=200px
        Section fill
          Stack fill vertical
            Input (search), fluid
            Tabs fluid                 (two or three tabs: "Category" / "Material")
            Stack.Item grow            Tabs vertical: the categories, with count badges
      Stack.Item grow
        Section fill scrollable        the selected category's items, one candystripe row or card each
```

- The sidebar is **160-200 px** wide (Uplink 160, PersonalCrafting 200, NtosNetDownloader 105, SecurityRecords about a third).
  The content takes the rest.
- Cargo's catalogue (`Cargo/CargoCatalog.tsx`) runs the same shape with `Stack fill g={0}`, a `Stack.Divider` between two
  `Section fill`s, the vertical Tabs (first tab is `search_results`) on the left and the items on the right.
- Search is the **first thing in the sidebar**, an `Input fluid` (SearchBar), or a `buttons` slot Input on the list's own Section
  (Vending, Gps).
- Examples: `PersonalCrafting/index.tsx` (700x720), `Cargo/CargoCatalog.tsx` (800x750), `Uplink/GenericUplink.tsx` (700x600,
  syndicate), `SecurityRecords/` (750x550), `NtosNetDownloader.tsx`.

### C. Top Tabs plus Sections

For consoles with a few distinct pages. 400-800 wide.

```
Window 800 x 750
  Window.Content
    Stack fill vertical
      Stack.Item   Section title=<page name> buttons=[X]
                     Tabs fluid, m={-1}      (4 tabs with icons: list, envelope, shopping-cart, question)
      Stack.Item grow, mt={-1}   the page, usually one Section fill scrollable
```

- The Tabs sit **inside a Section** with a -1 margin (Cargo `Cargo/index.tsx`), or in a `Section fitted` beside a lock Button
  (Uplink `Uplink/index.tsx`). They never float bare on the window background. `KsTguiTabs` already does this when it is
  a child of a `KsTguiSection`.
- Tabs are **fluid and have icons** at the top level (23 of 68 `Tabs` are fluid). Vertical tabs are the sidebar (20).
- `CommunicationsConsole/index.tsx` (400x650, scrollable) swaps the page below an "Authentication" Section instead.

### D. Status header plus a list or table

Monitoring consoles. 550-600 wide, 600-700 tall.

```
Window 550 x 700
  Window.Content
    Stack fill vertical
      Stack.Item   Section > LabeledList: "Supply" ProgressBar teal, "Draw" ProgressBar pink
      Stack.Item   Section height=3 : a row of Button.Checkbox filters
      Stack.Item grow  Section fill scrollable > Table
                         Table.Row header: Area | Charge (collapsing) | Load (right, width 7) | Eqp | Lgt | Env
                         Table.Row className="candystripe"
```

- Header cells are plain `Table.Cell`s with `collapsing` on the narrow columns; numbers `textAlign="right"`; the first
  column is the one that stretches.
- A sort or filter control sits in the list Section's `buttons` slot (CrewConsole: a sort-by Button, a direction Button, an
  Input), or in its own short Section above (PowerMonitor).
- Examples: `PowerMonitor.tsx` (550x700), `CrewConsole.tsx` (600x600, scrollable), `Gps.tsx`.

### E. Modal input dialogs

Small, one question. 240-425 wide; the height is **computed** from the message length, number of options and so on
(`TextInputModal.tsx`: a base of about 135 plus message-length terms).

```
Window title=<the question's title> 325 x <computed>
  Window.Content onKeyDown  (Enter submits, Esc cancels)
    Section fill
      Stack fill vertical
        Stack.Item        Box color="label": the message
        Stack.Item grow   the input: TextArea / list of Buttons / NumberInput + helper Buttons
        Stack.Item        InputButtons: Cancel (bad) ... counter ... Submit (good), 6 wide each, space-around
```

- Message is `color="label"`. Buttons are `Cancel` (`bad`) and `Submit` (`good`), sentence case; in the "large buttons"
  preference they are fluid and upper case (`common/InputButtons.tsx`).
- A list dialog is a column of `Button fluid` with `selected` for the choice (`ListInputWindow/ListInputModal.tsx`, 325 wide).
- A number dialog is helper Buttons around the input: `angle-double-left` (min), `angle-down`, the NumberInput (fluid),
  `angle-up`, `angle-double-right` (max), `redo` (reset), each with a `tooltip` naming the value (`NumberInputModal.tsx`, 270 wide).
- A checkbox list is a candystripe `Table` of `Button.Checkbox fluid`, a search Input below, then the buttons
  (`CheckboxInput.tsx`, 425x300), under a `NoticeBox info textAlign="center"` giving the prompt.
- Examples: `TextInputModal.tsx`, `ListInputWindow/`, `NumberInputModal.tsx`, `KeyComboModal.tsx` (240), `AlertModal.tsx`.

### F. Drill-down screens in one Section

AirAlarm (`AirAlarm/index.tsx`, 475x650):

```
Stack fill vertical
  Stack.Item   Section "Air Status" > LabeledList (gas readings, each value coloured good/average/bad)
  Stack.Item grow
    Section fill scrollable title=<screen name> buttons=[Back: icon arrow-left, disabled on the home screen]
      the screen: Home is a vertical Stack of Buttons with icons, danger Button.Confirm last
```

The Back button is `icon="arrow-left"`, text `Back`, in the buttons slot of the Section whose title changes with the screen.

### G. NtOS programs

`NtosWindow width height` instead of `Window`; 12 are 400 wide and 12 are 600 tall, 7 are 600 wide. The content is the same
archetypes (A-D). `NtosMain.tsx` (400x500) is a Section of app Buttons then a "Details" Section; `NtosCard.tsx` is archetype B
with two ID slots as `Button fluid icon="eject"`. Program windows have no title of their own: the header bar names the program.

### H. Antagonist greetings

620 wide (13 windows), 170-450 tall. `Section fill scrollable title="Intro"` etc., with a `fontSize` of 20-25px intro line;
the only family where large text is routine (10 of the 43 files using a font of 24px or more). Syndicate/abductor/ntos-terminal
themes appear here. `AntagInfoTraitor.tsx`: Intro, Employer, "Uplink", "Codewords" Sections, `Stack.Divider` between the two
columns of a Section. Not a model for a functional window.

## Sections

- 895 `<Section>` in 402 files; 498 have a `title`, 235 have `buttons`, 316 are `fill`, 168 `scrollable`, 15 `fitted`.
- **Title wording** (417 literal titles): 202 are one word, 368 are one or two words. Commonest: Controls 10, Settings 9,
  Status 8, Actions 4, Powers 4, Categories 3, Target 3, Beaker 3, Filters 3. Two end in a colon.
- **Capitalisation** is mixed: 218 sentence case, 188 Title Case. In an older machine window it is Title Case ("Power Status",
  "Holding Tank"), in a newer one sentence case. Pick **sentence case** for new windows: `Power status`. Do not mix in one window.
- **Labels** (669 `LabeledList.Item label`): 379 sentence case, 246 Title Case, **0 end in a colon** (LabeledList adds it).
- **Buttons slot:** the Section's own actions: the power switch, refresh, eject, back, help `question`, a search Input. 235 uses.
- **`fill`** goes on a Section that takes the leftover height of a `Stack fill vertical`; **`scrollable`** goes with it
  (168 `scrollable`, 131 files). A Section that is neither shrinks to its content.
- **Nesting:** a Section in a Section is allowed and is used for item cards inside a list (`Uplink/GenericUplink.tsx`: one
  `Section` per item in a scrolling Section). The inner one has no background and its title is smaller (13px, then 12px).
- **`Window.Content scrollable` vs a filling Section:** a window of 3 or more stacked Sections of unknown height scrolls
  at the window (Apc, Fax, Cryo, Gps, ChemDispenser). A window with **one list that grows** has that list's Section
  `fill scrollable`, and everything else keeps its height (PowerMonitor, Cargo, Vending). Not both.

## Data to control

| Datum | tg control | Count / example |
| --- | --- | --- |
| Name: value | `LabeledList` in a Section | 996 uses in 158 files. Value text coloured per item with `color`, not the Section |
| Level, charge, health | `ProgressBar` | 125 in 59 files; 75 have `ranges`. Most-used ranges (as fractions): good from .7 / average .3-.7 / bad below .3 (7 uses); good from .5 / average .25-.5 / bad below .25 (5); an inverted set for load, good below .2 (4). A fixed `color` instead of ranges: 32 (`teal`/`pink` for supply and draw in PowerMonitor, `bad` for damage bars in Sleeper/Cryo) |
| Pressure, temperature, with an alert | `RoundGauge` | 15 in 6 files. Canister, TankCompressor. The **pressure** gauge, with `ranges` and `alertAfter` |
| A number the player types or drags (rate, pressure, frequency) | `NumberInput` with `unit` and `width` | 128 in 66 files. `unit="kPa"`, `"L/s"`, `"kHz"`, `"K"`. Width 63-80px. A `Max` Button (`icon="plus"`, disabled at max) beside it: AtmosPump |
| A number with a wide range and presets | `Slider` between `fast-backward`/`backward` and `forward`/`fast-forward` Buttons | Smes. 26 `Slider`s in 15 files |
| A dial for one value in a small panel | `Knob` | 12 in 11 files. Canister's release pressure, `unit="kPa"`; the knob is the centrepiece of its Section |
| One of 3-6 modes | A row of `Button`s with `selected` on the current one | Cryo, SolarControl "Tracking": Off / Timed / Auto. APC channels: Auto / On / Off with `sync`/`power-off`/`times` icons |
| One of many (7+) | `Dropdown` | 94 in 61 files, mostly admin and debug windows. Player-facing windows prefer a list of Buttons or vertical Tabs |
| One of many, browsed | `Tabs vertical` (category), or candystripe transparent fluid Buttons (SpawnSearch) | 20 vertical Tabs; `VirtualList`/`Button color="transparent" fluid` in `SpawnSearch.tsx` |
| Table of records | `Table` + `Table.Row className="candystripe"` | 918 `Table` in 74 files; 102 `candystripe` in 60 |
| A flag | `Button.Checkbox` | 84 in 52 files. `fluid` + `color="transparent"` in filter lists (PersonalCrafting, CheckboxInput) |
| Free text | `Input` (147, 104 files), `TextArea` (24) | `fluid`; the search `Input` has a magnifier `Icon name="search"` in front (SpawnSearch) |
| Empty / not available / needs a login | `NoticeBox` | 200 in 128 files: 31 `info` (blue), 29 `danger` (red), 6 `success`, 22 centred, and about 100 plain (the amber default) |
| A section that exists but is locked | `Dimmer` over it | 34 in 28 files |

## Buttons

1739 `<Button>` in 436 files. 667 have an icon and text, 431 an icon only, 412 text only.

- **Icons by verb** (909 literal `icon=`): `times` 50, `eject` 48, `plus` 44, `trash` 31, `sync` 28, `power-off` 21, `arrow-left` 20,
  `question` 18, `minus` 16, `arrow-right` 15, `undo` 12, `upload` 12, `arrow-up` 10, `info` 9, `check` 9, `chevron-left` 9, `print` 9,
  `download` 9, `cog` 8, `wrench` 8, `fast-forward` 8, `arrow-down` 8, `search` 8, `dice` 8, `pen` 8, `home` 7, `play` 6, `lock` 6,
  `lock-open` 5.
- **Verb to icon pairs** that repeat: Eject = `eject` (11 of the `Eject` buttons; `Insert` also uses `eject`, 5); Clear =
  `times` (5); Reset = `sync` (4) or `undo` (2); Print = `print`; Back = `arrow-left` (6) or `chevron-left` (5); Home / Main Menu =
  `home`; Log Out = `lock`, Login = `lock-open`; Max = `plus` (6); Repair = `wrench`; Search = `search`; Refresh = `sync`;
  Delete / Purge = `trash` (also the icon of 10 of the `Button.Confirm`s); Save = `save`/`floppy-disk`; Send = `paper-plane`.
- **Toggle a state:** `icon={on ? 'power-off' : 'times'}`, `content={on ? 'On' : 'Off'}`, `selected={on}`. The pair appears
  in at least 20 windows. The text names the **current state**, not the action. `selected` is the on-state (208 uses, 118 files).
  For a checkbox-looking option use `Button.Checkbox` (the box icon is automatic).
- **Colours** (506 `color=`): `transparent` 137 (rows in lists, icon-only helpers), `bad` 60, `red` 44, `good` 43, `green` 23,
  purple/blue/yellow a handful, `danger` 3-5, `caution` 2. So: no colour for ordinary actions; `good`/`bad` for the pair that
  confirms or abandons (Submit/Cancel, Log in/out, Authorize); `transparent` when a row of buttons is a list.
  `caution` and `danger` exist (alarm-state buttons in `AirAlarm/screens/Home.tsx`) but are rare.
- **`Button.Confirm`:** 62 in 45 files. Anything destructive or irreversible: purge records, terminate employment, recall the
  shuttle, shred. `icon="trash"` 10 of them, `color="bad"` usually, `confirmContent` is a short question ("Confirm?",
  "Fire Employee?", "Really Flee?").
- **`disabled` + `tooltip`:** 420 disabled uses; 324 buttons carry a tooltip. The reason goes in the tooltip, the way the button
  stays visible: `tooltip={sendable ? '' : `Shuttle is at ${location}`}` (`Cargo/CargoCart.tsx`), the shuttle buttons in
  `CommunicationsConsole/Main.tsx`. Do not hide a button to say "not now".
- **`compact`:** 10 uses only. tg's buttons are one size; the compact one is for dense tables.
- **`fluid`:** 275 uses, nearly always in a list, a sidebar or a stack of menu entries, and for the single main action of a
  small window (Signaler "Send Signal").
- **Text:** sentence case, one or two words ("Cancel" 14, "Back" 13, "Eject" 12, "Clear" 9, "Reset" 9, "Print" 7, "Insert" 5).
  Upper case appears in a few "status" buttons (ESTOP, FAULT, COMMS, DOORS, BUSY in the mech console; SUBMIT/CANCEL in the large
  buttons preference). Icon-only buttons (431) always have a `tooltip` or sit beside a label.

## Colour

- `Box color="label"` 169 uses in 76 files: the grey-blue for captions and prompts (modal messages, "Frequency:" before an input).
- `good` / `average` / `bad` as the text colour of a state: 64 / 18 / 88 `color=` uses. Always a state word (`Charging`,
  `Optimal`, `No power`), never decoration.
- Named palette colours (`green`, `red`, `yellow`, `orange`, `blue`, `purple`) appear as notice boxes, button colours and
  quick text. They mean the same as good/bad (`red` 20, `green` 8 on Box).
- **Raw hex or rgba:** 31 `color:` strings in 11 files and 18 `color="#..."` in 11 files, among 72 files that contain any hex
  at all. They encode **data**: a reagent's colour, a damage type (CrewConsole's `COLORS.damageType`), a job department, a
  wire's colour (Wires). Not chrome. `backgroundColor=` appears 99 times in 63 files, mostly colour swatches, previews and
  antagonist-window splashes; panels in functional windows do not set one.
- Gold for money, `green` for the player's own balance (Vending), `teal`/`pink` for two lines in one chart (PowerMonitor).

## Text

- Titles and labels as above. Units follow the value, a space between: `101 kPa`, `5 L/s`; as the `unit` of a NumberInput/Knob/Slider.
- **Numbers:** `toFixed(n)` 80 uses in 36 files (1-2 decimals is typical), `formatSiUnit` 23 / `formatPower` 13 / `formatMoney` 27,
  `AnimatedNumber` 38 in 20 files, for a figure that changes while open (money, charge, a timer). `formatPower(value, 1)` is
  the Slider `format` in Smes. Percentages are the ProgressBar's own text.
- **Casing of state text:** `On`/`Off`, `Open`/`Closed`, `Charging`, `Not Charging`, `Fully Charged`. Bracketed state `[ External Power ]`
  is an old Apc habit, not a pattern.
- Text is left aligned. `textAlign="center"` is on 351 elements in 151 files, but on **buttons in modals, NoticeBoxes (22 of 200),
  empty-state messages and header cells of a numeric column**; 37 on a `Section`, almost all a single big status message. Body text
  and LabeledList values are never centred.
- `fontSize`: 362 uses; 65 are 12px or less, 120 are 13-16, 114 are 17-24, 50 are over 24. The large ones sit in 43 files:
  10 AntagInfo greetings and then single emphasised readouts (Gps current location 18px, a big error icon with `size={6}`).

## Tooltips

148 `Tooltip`/`Button tooltip` uses plus 324 button tooltips. `tooltipPosition` appears 149 times: `top` 42, `bottom-start` 30,
`left` 21, `bottom` 19, `right` 14. Use `bottom-start` for a button at the left edge of its row and `top` for a button that
sits under a header, `left` for a column at the right edge of the window, so the tooltip never leaves the window. Tooltips
**explain**: why disabled, what an icon-only button does, the unit or limit of a number. They do not repeat the label.

## Hazard stripes

See the table in `SKILL.md` ("The hazard stripe effect") and the catalogue below.

### Catalogue of every striped background in tg at this commit

`grep repeating-linear-gradient` over `tgui/packages` and `node_modules/tgui-core`: 5 hits, 4 of them stripes.

| # | Where | Licence | Angle | Stripe widths | Colours | Animation |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `tgui-core/styles/components/NoticeBox.scss` (`reference/`) | MIT | `-45deg` (starts at the bottom-right corner) | transparent `0 - 10px`, stripe `10px - 20px`, in `em` (so 10px at 12px) | The box colour is `oklch(from var(--noticebox-background) l*0.825 c*0.75 h)`. Stripe is `hsla(0,0%,0%,.1)` (`--notice-box-stripes`, `vars-components.scss`), `hsla(0,0%,100%,.1)` on the `black` box. Text is bold italic, white; black on the low-contrast colours | none |
| 2 | `styles/interfaces/AnomalyTower.scss`, `.AnomalyTower__HazardBg` (`AnomalousWeatherTower.tsx` line 135) | AGPL | `45deg` (starts at the bottom-left) | `#ffc107` `0 - 15px`, `#000` `15px - 30px`; `background-size: 42.42px` so the period tiles on the diagonal | yellow `#ffc107` and opaque black; `border-style: outset; border-color: #333` | none |
| 3 | The button inside it, `.AnomalyTower__HazardButton` | AGPL | n/a | n/a | 100x100 circle (`border-radius: 50px`), red text, `border: outset #bebebe`, `box-shadow: 0 0 20px #0c0c0c`, with a red `Button color="red" m={1}` | none |
| 4 | `tgui-panel/styles/tgchat/chat-dark.scss` and `chat-light.scss`, `.chat_alert_<colour>` | AGPL | `-45deg`, as a `border-image` (slice with `fill`) | `10px` / `10px` | Two dark tints of one hue, opaque: e.g. blue `hsl(198.6,100%,11.4%)` and `hsl(198.3,100%,13.5%)`; red `hsl(0,100%,12.9%)` / `hsl(0,100%,16.1%)`; 9 colours (default, green, blue, pink, yellow, orange, red, purple, grey) | none |
| 5 | `styles/interfaces/DetectiveBoard.scss` | AGPL | `0deg` | a 1px line every 22px | `rgba(0,0,0,.06)` | none (ruled paper, not hazard) |
| 6 | `tgui-core/styles/themes/ntos_terminal.scss` (`reference/`) | MIT | `0deg` | 1px of 5px (scanlines) | `hsla(0,0%,0%,.15)` over a radial gradient | none (CRT effect, not hazard) |

- **No hazard stripe exists on a ProgressBar.** `ProgressBar.scss` has no stripes; neither does the lathe: its "Building items..." is
  a `Dimmer` with a spinner `Icon` (`Fabrication/DesignBrowser.tsx` line 331), not a striped bar.
- The `candystripe` class is a **flat** colour, `linear-gradient(var(--candystripe-odd))`, not a pattern.
- **Only #1 is in the default look.** #2 is one window, one button. KS14 uses it on the nuclear reactor's SILENCE
  button, by the user's choice. When a window you are working on has a control that is genuinely alarming in the same
  way (an emergency stop, a purge, an alarm silence, a self-destruct), suggest the stripes to the user rather than adding
  them unasked or leaving them unmentioned.
- No stripe animates. The only animations in tgui-core's SCSS are the RoundGauge alert pulse and shake; in `packages/tgui`, a
  blink in `NtosHeader.scss` and one-offs in CivCargoHoldTerminal, Fabricator (`materialdock-open`), SlotMachine, DetectiveBoard.

### What KS14 has

- `Content.Client/_KS14/Tgui/KsTguiNoticeStyleBox.cs` (`: StyleBox`): stripe #1, built as clipped triangles, not a texture. `-45deg`
  from the **bottom-right** corner, 10px bands (`StripeWidth`, at UI scale), first band bare background, second the stripe, repeat.
  Constructor `(Color backgroundColor, bool whiteStripes = false)`; the stripe colour is always `Translucent(Black|White, .1,
  backgroundColor)`. Geometry is `KsTguiGeometry.ClipStripe` (distance from the bottom-right: `low <= x + y <= high`).
- `KsTguiSheetlet.Controls.cs` `AddNoticeBoxRules` makes one per `KsTguiNoticeColor` (Default, Info, Success, Warning, Danger,
  Yellow, Olive, Teal, Violet, Purple, Pink, Brown, Gold, Black, White, Grey, LightGrey, Primary, Good, Average, Bad, ...), with
  content margins 4 vertical, 6 horizontal, and `whiteStripes` for `Black`. `KsTguiNoticeBox` picks it by class.
- `KsTguiSheetlet.cs` (about line 510-538): stock **`StripeBack`** gets `KsTguiNoticeStyleBox(_palette.Notice)` (the default amber
  notice) as its `background`, with 4/6 margins, and `StripeBack.KsStylePropertyFlat = true` (no edge lines). Its labels become
  12px bold italic. `KsTguiSheetlet.Controls.cs` `AddNoticeBoxRules` adds, per `KsTguiNoticeColor`, a `StripeBack` rule with the
  same style box as the `KsTguiNoticeBox` of that colour; `StripeBack.KsNoticeColor` (nullable, null = default notice, Baron
  ignores it) picks it: `<StripeBack KsNoticeColor="Danger">`. Test: `KsTguiStripeBackTest`. The panel classes `StyleClass.Positive`, `Negative`, `Highlight` are green, red and blue notice boxes.
  Under Baron `StripeBack` is unchanged (`StripeBack.cs`, `StripeBack.Klovn.Tgui.cs`).

### Mapping

| tg variant | KS14 |
| --- | --- |
| #1 NoticeBox, any colour | `KsTguiNoticeBox { NoticeColor = ... }`. Exact, including the white stripes on Black |
| #1 behind arbitrary children (a header row with buttons) | `StripeBack` in XAML, `KsNoticeColor="Danger"` etc. for any of tg's notice colours (identical style box to `KsTguiNoticeBox`), or a `PanelContainer` whose style rule gives `KsTguiNoticeStyleBox(color)` (needs the 4/6 margins set as the sheetlet does) |
| #4 chat alert, hue-matched dark opaque stripes | Needs a **stripe colour parameter**: `KsTguiNoticeStyleBox` always derives it from black or white at .1. Everything else (-45deg, 10px) already matches |
| #2 yellow / black hazard, 45deg, 15px | `KsTguiHazardPanel` (`KsTguiHazardStyleBox`), border included. The `outset` sides are `#333` top and left, `#222` bottom and right (the CSS spec's darken by a third), not checked against a tg screenshot |
| #3 round hazard button | Missing: a circular bevelled button; not in the library |
| ProgressBar stripes / animation | Does not exist in tg either. Nothing to port |

### Which to use

- **A message about state** (error, empty, warning, needs login): `KsTguiNoticeBox` with a colour that means it
  (Info / Success / Warning / Danger, or the default). It carries the text, wraps and has the margins.
- **`StripeBack`**: when retrofitting an XAML window that already has one, or when the striped panel must hold other
  controls (a header with buttons). It looks like the default notice under tgui and is the stock panel under Baron, so
  existing windows need no change; `KsNoticeColor` gives it any notice colour under tgui (Baron ignores it). In a
  code-built window whose striped part is only text, use `KsTguiNoticeBox`.
- **A `PanelContainer` with a `KsTguiNoticeStyleBox`** (from a style rule, never `PanelOverride`, see *Retrofitting*): a coloured
  striped panel that holds controls and is not amber. Rare.
- **Anything hazard yellow/black**: a `KsTguiHazardPanel` around the one control that should look alarming (the reactor's
  SILENCE button). Never a texture. Suggest it to the user for a control like that; don't add it unasked.

## Anti-patterns (what tg windows do not do)

Each backed by the survey.

- **No custom panel colours in a functional window.** 99 `backgroundColor=` in 63 of 651 files; they are swatches, previews and
  the antagonist splash pages. Every functional window's surfaces are Sections, Stack.Dividers or the Window background.
- **No borders around things.** 86 `border`/`borderRadius` strings in all of the interfaces, in the bespoke windows (Canvas, Slot
  machine, Detective board). Separation is a Section background, a `Divider`, or whitespace.
- **No centred body text.** See *Text*. Centring is for modal buttons, a NoticeBox, an empty-state message.
- **No window-sized headers.** A window is named by its title bar. The top of the window is a Section or Tabs.
  Text over 24px is in 43 of 651 files, as a single emphasised line.
- **No wall of unlabelled controls.** A row of Buttons is in a Section with a title or a label in a LabeledList.
- **No stock-looking spacing.** All gaps are tgui's multiples (6px); a control library control already has its margins.
- **No hiding of unavailable things.** `disabled` with a `tooltip` (420 disabled, 324 tooltips), a `NoticeBox` for an empty
  list, a `Dimmer` for a locked section.
- **No text inside a ProgressBar that repeats the label.** The bar shows its value, a number or `AnimatedNumber`; the label
  sits in the LabeledList.
- **No colon after a label** (0 of 669).
- **No second layer of tabs inside a Tab page.** Drill down with a Section whose title changes and a Back button (AirAlarm).
