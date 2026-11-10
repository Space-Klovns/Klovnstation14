---
name: tgui
description: How to style and lay out client UI so it resembles /tg/station's tgui - reading tgui's React and SCSS sources, converting its units, the KsTgui control library (Section, LabeledList, NoticeBox, Button, Tabs, ProgressBar, Icon), the palette tokens, porting a window 1:1 while keeping the stock window for the Baron theme, and retrofitting tgui Sections into existing XAML without changing Baron. Use when adding or restyling a window that should look like tgui, porting a tgstation interface, choosing tgui colours or spacing, or when a window looks unlike tg. For XAML, sheetlet and layout mechanics use the ui skill; for BUI wiring use the bui skill.
---

# Making UI look like /tg/station

KS14's default theme is a port of tgui's `nanotrasen` theme, and Baron is what `klovn.ui.tgui` turns it back to. The `ui`
skill's *The tgui theme* section covers the machinery: palettes, fonts, window chrome, fades. This skill is about making
a given window look right.

## Where the truth is

Never guess a tgui value. tgstation is at `C:/Space/SS13 Dev/Repo/tgstation`. **If that repo is missing**, the MIT parts are copied
into [reference/](reference/README.md) (same folder structure: `tgui-core/styles`, `tgui-core/dist/components/*.d.ts`, `tgui/layouts`,
`tgui/styles`), and [designing.md](designing.md) describes the AGPL interfaces in words with their tg paths:

| What | Where |
| --- | --- |
| A window's layout | `tgui/packages/tgui/interfaces/<Name>.tsx`, or a folder of that name |
| A component's structure and CSS | `tgui/node_modules/tgui-core/dist/components/`, `.../styles/components/<Name>.scss` |
| Spacing and colour tokens | `tgui/node_modules/tgui-core/styles/vars-values.scss` |
| Component props | `tgui/node_modules/tgui-core/dist/components/*.d.ts`; copy: `reference/tgui-core/dist/components/` |
| Window frame | `tgui/packages/tgui/layouts/Window.tsx`, `styles/layouts/*.scss`; copy: `reference/tgui/` |
| Chat | `tgui/packages/tgui-panel/`, `tgui/packages/tgui-say/` (AGPL, not in `reference/`) |

## Reading a .tsx

The window size is on `<Window width={} height={}>`: use it for `SetSize`. Base font is 12px, so 1rem = 12px.

| tgui | KS14 |
| --- | --- |
| `width={5}`, `height`, `fontSize` (plain units) | number × 12: 60px |
| `mt={1}`, `mb`, `m`, `p` (half units) | number × 6: 6px |
| `<Stack>` / `<Stack vertical>` | `BoxContainer` with `SeparationOverride="6"` (gap is 0.5em) |
| `<Stack.Item grow>`, `<Stack fill>` | `HorizontalExpand` / `VerticalExpand` |
| `<Stack.Divider>`, `<Divider>` | `HSeparator`, or the `LowDivider` class |
| `<Box color="label">` | `KsTguiPalettes.Label` |
| `<Box color="good|average|bad">` | `KsTguiPalettes.Good|Average|Bad` |
| `<Box bold>`, `italic` | `sheet.BaseFont` kinds, or `FontKind` on a label class |
| `<Window.Content scrollable>` | `ScrollContainer` around the root box, or `KsTguiSection Scrollable` |
| `<Dimmer>` | `KsTguiDimmer`, see the table below |

**The guidebook `?` is automatic.** `KsBoundUserInterfaceExt` takes the place of the engine's `CreateWindow` helpers for all of `Content.Client.*`, and gives any window whose entity has `GuideHelpComponent` its button. A window made with `new` in a BUI needs `this.WithGuide(window)`; `KsGuideHelpWindowTest` walks the prototypes and fails if one is missed. A `DefaultWindow` gets its button from the tgui chrome, so only under tgui. A bare `BaseWindow` has no title bar to put one in.

Window contents sit 6px in from the window edge: `Margin = new Thickness(6f)` on the root box. Stacked sections use
`SeparationOverride = KsTguiSection.Gap` in their parent.

## Control library

`Content.Client._KS14.Tgui.Controls`. Parameterless constructors, so they work from code and from XAML with
`xmlns:tg="clr-namespace:Content.Client._KS14.Tgui.Controls"`. Read the class for the current API.

| tgui | KS14 | Notes |
| --- | --- | --- |
| `Section` | `KsTguiSection` | `Title`, `Buttons` slot, `Scrollable`, `ScrollableHorizontal`, `Fill`, `Fitted`, `NoTopPadding`, `StretchContents`. XAML children are the body. A nested section is pulled 6px out at each side (title line edge to edge), as tg |
| `StyleableSection` | `KsTguiStyleableSection` | Section title + padded content with no background; `TitleColor`, `LineColor` |
| `Collapsible` | `KsTguiCollapsible` | `Title`, `Open`, `Color`, `Icon`, `Buttons`, `ChildMarginTop`. Fluid header button with a chevron; XAML children are the content |
| `LabeledList` | `KsTguiLabeledList`, `KsTguiLabeledListItem`, `KsTguiLabeledListDivider` | `AddItem(label, text, color)`; `Buttons` slot (right-aligned in its column). The label gets tgui's `:` automatically (not doubled if written; `Colon = false` for a label tg would pass as a component). Cells share a text baseline like `td`s (`VerticalAlign`: Baseline/Top/Middle/Bottom), value starts at `max(label + 12, 60)`. `AddDivider(size)`, `LabelWrap`, `TextAlign`, `Tooltip` (dotted underline + engine tooltip). Put it inside a Section |
| `NoticeBox` | `KsTguiNoticeBox` | `NoticeColor` (every tg colour name; Info/Success/Warning/Danger are blue/green/orange/red; black text on Yellow/White, white stripes on Black). `Spaced = true` adds tg's 6px margin-bottom (off by default, because boxes in a separated BoxContainer already have that gap) |
| `Button` | `KsTguiButton` | `: Button`. `Icon`, `IconPosition`, `IconRotation`, `IconSpin`, `IconSize`, `IconColor`, `IconRegular`, `Color`, `Compact`, `Fluid`, `Circular`, `EllipsisText`, `VerticalAlignContent`, `Strikethrough`, `Selected`. Text is left aligned like tg (`LeftAligned = false` for `textAlign="center"`). The 2px right margin is dropped for fluid and last-child buttons, and the 2px bottom one in a horizontal box (tg's inline buttons share a baseline, so margin-bottom never shortens one); plain `Button`/`OptionButton` get only the right margin from the sheet. Transparent + selected is a clear button with green text (Button.Checkbox), not a green slab |
| `Button.Checkbox` / `.Confirm` / `.Input` | `KsTguiCheckboxButton`, `KsTguiConfirmButton`, `KsTguiInputButton` | Subclasses of `KsTguiButton`; `Toggled`, `Confirmed` (blur = keyboard focus lost disarms), `Committed` |
| `Button` on an item slot | `KsTguiItemSlotButton` | `: KsTguiButton`. `Filled`, `SlotId`. Eject while filled, Insert while empty, never disabled |
| `Icon` | `KsTguiIcon`, `KsTguiIcons`, `KsTguiIconStack` | FA6 Solid glyphs, `Regular = true` for tg's `-o` names (`SquareO`, `CheckSquareO`), `Rotation`, `Spin` (2s/turn). Add a `const string` when one is missing |
| `Tabs` | `KsTguiTabs`, `KsTguiTabItem` | Vertical too. `Count` is the badge. The Section-coloured strip, 6px bottom gap and in-Section -6px margins are as tg; the indicator grows from the middle over 200ms. Use this, not the engine `TabContainer`, in a ported window. `KsTguiTabItem.Attention` is tg's `textColor='yellow'` for a tab with something waiting (requests to answer, goods to collect): yellow text while unselected, yellow text and indicator while selected. Set it from the same count as the badge (`tab.Attention = pending > 0`), never a hardcoded `SelectedColor`; it wins over `SelectedColor`. Leave it off tabs whose badge is only a total or an unread counter (chat pages) |
| `ProgressBar` | `KsTguiProgressBar` | `AddRange` for good/average/bad. Text right-aligned, inset 7px, percentage unclamped, `FractionDigits`. Fill width/colour ease over 1s and the border colour over 1s ease-out, from `FrameUpdate`, nothing once settled (first value is drawn in place). `ShowNumber` puts an easing number in place of the text, as an `AnimatedNumber` inside tg's bar. `BackgroundColor` (tg's `backgroundColor`) and `Mirrored` (tg's `scaleX(-1)`, fill from the right, text still right-aligned) |
| `NumberInput` | `KsTguiNumberInput` | Value bar at the left, vertical drag, click selects everything. `Animated` is tg's `animated` prop: set it where tg does. Use it, not `FloatSpinBox`, in a ported window |
| `Knob` | `KsTguiKnob`, `KsTguiKnobMath` | tg's `size` is `KnobSize`; `Color` is a `KsTguiColor?` (null = ranges, then the theme's ring), plus `AddRange`, `FillValue`, `Bipolar`, `Step`, `StepPixelSize` (null = the knob's width over the steps), `Unclamped`, `TickWhileDragging`. Vertical drag relative to the press, up is more; `OnChanged` is on every release (a click too, as tg), after a typed value, and each 0.4 s when ticking. A click opens typing 0.1 s after the press; Esc reverts. The value popup is above it while the value has moved. All the numbers are in `KsTguiKnobMath`; the rings, cap and glow are triangles from `BuildVertices`. The popup and edit colours come from the `KsTguiKnob` rule in `KsTguiSheetlet.Themes.cs`. The value popup is a `KsTguiBackdropPanel` inside a `KsTguiScaleHost` (pivot at the bottom edge, or the top when it opens below): it blurs what is behind it, and opens and closes as tg's `Floating` does, scale .9 to 1 and opacity over 0.2 s of CSS ease (`KsTguiFloatingTransition`, in the popup layer until it has closed; opened again meanwhile it turns round from where it got to) |
| `candystripe` class, `Stack zebra` | `KsTguiLabeledList.Candystripe`, `KsTguiTabs.Candystripe`, `KsTguiCandystripe` | Odd visible rows darkened edge to edge, so hiding a row re-alternates. `KsTguiCandystripe` is a vertical box for any rows. The dark colour is only given by the tgui sheet |
| `VirtualList` of `<Button className="candystripe" color="transparent" fluid selected>` (SpawnSearch, any list of choices) | `KsTguiList`, `KsTguiListRow` | **The standard for lists.** Rows are transparent fluid `KsTguiButton`s with no transition (`Instant`), 20 tall with 2px between, striped on the first, third and so on, text at .5 and solid on hover, the selected row clear with green text. `AddRow(text, metadata)`, `Clear()`, `Select(index)` (silent), `SelectedIndex`/`SelectedRow`, `OnSelected` (a click or Up/Down, via `MoveSelection`), `OnActivated` (a second click on the row inside the double-click time, or Enter), `PressRow` for a test. Put it in a `KsTguiSection` with `Scrollable`. **The engine's own lists follow the same look under the tgui theme without changes**: `ItemList` (`KsTguiItemListStyle.cs`: the sheet can't give a list a stripe by index, a hover or per-row text colour, so its row box finds its list and row from the rectangle `ItemList` gives it and paints the row and its text itself; the list's own text is made clear), and `ListContainer` rows that carry `ListContainer.StyleClassListContainerButton` (stripe child, padding, selected and hovered classes in `ListContainer.Klovn.Tgui.cs`; rows 2px apart on every `ListContainer`). Colours are `KsTguiSheetlet.Lists.cs`'s `ListColors`, the transparent button's. Not a `Tree`/`FancyTree` (a navigation tree, not tg's list) |
| `Table`, `Table.Row`, `Table.Cell` | `KsTguiTable`, `KsTguiTableRow`, `KsTguiTableCell` | Cells are the row's children; columns size to the widest cell; cells hang from a common text baseline. Header rows/cells are bold (plain labels only) with 6px under. `KsTguiTableCell` (a vertical box) adds `ColSpan`, `Header`, `Collapsing`, `VerticalAlign`; `KsTguiTable.Collapsing` keeps the table to its columns. `HorizontalExpand` on a cell marks an expanding column (tgui marks the others `collapsing`) |
| `ColorBox` | `KsTguiColorBox` | 12px swatch, transparent by default; `BoxColor`/`NamedColor` (tg name, l-5), `Content` text centred on it |
| `Divider` | `KsTguiDivider` | 2px line, 6px space either side; `Vertical` |
| `BlockQuote` | `KsTguiBlockQuote` | `Text`, label colour, left bar that ends with the text; the 6px below is a margin (`Spaced = false` for a last child) |
| `ImageButton` | `KsTguiImageButton` | `: ContainerButton`, drawn from its own state. `ImagePrototype` or `ImageTexture`, `ImageSize` (tg's 64), `Text` (bar at the bottom), `TopLeftText`/`TopRightText` or `AltButtons` (top overlay), `Buttons` (above the bar), `Color` (`Default` is tg's grey, `Primary` the blue; `Transparent` for pickers), `Selected`, `Fluid` (row, `Content` replaces `Text`). Not ported: pressed glow, text outline, `title`, contrast on a disabled row |
| a wrapping grid of `ImageButton` cards | `KsTguiCardGrid` | Columns = how many minimum-size cards fit, the width then shared out equally: constant `Gap`, first and last card flush with the edges at any width and after a resize. `HorizontalExpand` it. Use it for every card grid |
| `RoundGauge` | `KsTguiRoundGauge` | Smooth geometry from tg's 100x50 view box, sharp at any scale. `MinValue`, `MaxValue`, `AddRange`, `AlertAfter`/`AlertBefore`, `Format`, `GaugeSize`. Pass only real ranges: no data, no range. The needle takes tg's .2 s to a new value, and while in alert (`AlertAfter`, on a range) it shakes and the alert pulses. The first value is drawn in place. Nothing to call: it runs off `FrameUpdate` Ring arcs are 200 degrees per unit and the track the last 160 (tg's dash arithmetic); no ranges draws the theme's primary; idle colours go through `KsTguiPalettes.Translucent`; the alert's "!" is a real hole; `MinValue` defaults to 1 as in tgui-core. |
| `AnimatedNumber` | `KsTguiAnimatedNumber` | `: Label`. Set `Value`; eases by frame time and does nothing once settled. `Format`, `Initial` (where a number's first value eases from). **Nothing counts up as a window opens**, even where tg's source says `initial={0}`: a value in the window's first state is placed, and `Initial = 0` is only for what arrives after it (`NewVolumeStart` in `KsTguiChemMasterWindow`). For a figure that changes in place; keep one instance. In rows that get rebuilt, get it from a `KsTguiAnimatedNumberPool` keyed by what the row shows, or each state starts it over. `ShowText` for a non-number such as N/A. `Instant` in a stock window, set from `!KsTguiTheme.ReadEnabled()`, so Baron doesn't change |
| `Dimmer` | `KsTguiDimmer` | `KsTguiDimmer.Over(target, out dimmer)` returns a host that stacks the target and the dimmer; add the host where the target was, toggle `dimmer.Visible`. `Content` is centred. Takes the clicks. The standard way to show a locked section (lock icon + the swipe text), with the notice kept above |
| `Window__dimmer` (window seen but not usable) | `KsTguiWindowDimmer`, `KsTguiWindowDimmerUIController` | Automatic for any BUI window that goes through `KsBoundUserInterfaceExt` (`CreateWindow`, or `WithGuide` for one made with `new`): washed over exactly as CSS does it (sRGB: `.25 * colour + .75 * behind`, so 16 becomes 31 and 26 becomes 38; the dimmer captures what is behind it through `KsTguiBackdropBlur` with `blur: false` and blends in `backdrop_composite.swsl`; it must stay the window's last child, which the controller keeps, and the window must draw nothing itself, its background being a child panel; inside another popup's pass it falls back to the flat `Translucent` colour), clicks pass through, while the local non-ghost player fails the server's `CanInteract`/`CanComplexInteract` check for the BUI. Polled at 4 Hz |
| `TitleBar__statusIcon` (layouts/TitleBar.tsx) | `KsTguiTitleStatusIcon`, `KsTguiWindowStatus`, `KsTguiChromeUIController.SetStatus` | Automatic in every chromed window. Every window has a status, so one no bound interface made shows the green `eye` from its first frame (tg's `tools` for `undefined` never appears). A bound window's status is `KsTguiWindowDimmerUIController.GetStatus`, the dimmer's own answer, so the eye is red `eye-slash` exactly when the dimmer shows, green `eye` when the local player can interact, and orange `eye` (tg's `UI_UPDATE`) for a ghost that cannot: the server lets a ghost open an interface (`SharedInteractionSystem.OnBoundInterfaceInteractAttempt`) and refuses everything it sends, which is a window that updates for an observer who cannot touch it. Colours are `KsTguiPalettes.Good/Average/Bad` (no window theme changes them), 20px glyph in a 45px cell, the title with no margin after it, colour eased over `--transition-time-slow` (0.5s), the first answer placed with no ease. The cell takes no mouse input, so dragging from it moves the window |
| `Modal` | `KsTguiModal` | A dimmer with a panel. Put it in a stacking host beside the page, `Body`, `Open()`/`Close()`, `Closed`, `Entered` (tg's `onEnter`; does not close). Takes keyboard focus, Esc closes (a focused text box eats the first Esc) |
| `LabeledControls`, `.Item` | `KsTguiLabeledControls`, `KsTguiLabeledControlsItem` | `AddItem(label, content)`; `Wrap`. Items spread edge to edge (space-between), 3px in from the sides, every one as tall as the row; the label at the bottom, the content halfway between the top and it |
| `Dialog`, `Dialog.Button` | `KsTguiDialog`, `KsTguiDialogButton` | Covers its parent (put it in a stacking host like a `KsTguiDimmer`). `Title`, `Body`, `AddButton(text, onPressed)`, `DialogWidth` (370), `DialogHeight`, `OnClose`. Title and buttons are 14px mono (`KsTguiFonts.GetMonoFont(14)`); the body's font is the caller's to set |
| `RestrictedInput` | `KsTguiRestrictedInput` | `: LineEdit`. Green border, lighter focused, red once `IsInvalid` (out of `MinValue`..`MaxValue`, or off whole steps without `AllowFloats`); nothing is clamped. `-` negates, text right-aligned by a left margin. `OnValueChanged`/`OnEnter`/`OnEscape`/`OnBlurred`/`OnValidationChanged`, `SetValue` (ignored while focused) |
| `TimeDisplay` | `KsTguiTimeDisplay` | `: Label`. `Value` in deciseconds, `Auto` up/down (1 s ticks, floors at 0, restarts only on a changed `Value`), `ShortFormat`, `Format` |
| `Blink` | `KsTguiBlink` | One child. `Interval`/`Time` in ms (1000 each); keeps its room while hidden. Starts visible for a whole period, as tg's timer does |
| `FitText` | `KsTguiFitText` | `Text`, `TextMaxWidth`, `MaxFontSize`, `AcceptableDifference`; tg's 10-round binary search (`KsTguiFitText.Fit`), `FitFontSize` |
| `MenuBar`, `.Dropdown`, `.MenuItem`, `.MenuItemToggle`, `.Submenu`, `.Separator` | `KsTguiMenuBar`, `KsTguiMenuBarDropdown` (`AddItem`/`AddToggle`/`AddSubmenu`/`AddSeparator`), `KsTguiMenuBarItem`, `KsTguiMenuBarSubmenu`, `KsTguiMenuBarSeparator` | Menus open in engine popups. The bar holds tg's open-menu and open-on-hover state (click toggles both). Submenus open/close after a 0.25 s hover. Clicking an item leaves the menu open, as tg |
| `Stack.Divider` | `KsTguiStackDivider` | 2px line, no margin: use in a box with `SeparationOverride = KsTguiSection.Gap` (14px in all, same as `KsTguiDivider`, which brings its own 6px). `Hidden`, `Vertical` |
| `Chart.Line` | `KsTguiChartLine` | `SetData`, `RangeX`/`RangeY`, `FillColor`, `StrokeColor`, `StrokeWidth`. Needs a size. Has tg's closing points, so a range wider than the data shows a stub right of the last point |
| `Input` | `LineEdit` | Already themed. tg's `monospace` is the `KsTguiSheetlet.StyleClassMonospace` class (mono 12). In a window with the tgui chrome every `LineEdit` lets go of the keyboard after Enter, once its `OnTextEntered` handlers have run, as tg's `blur()` (`KsTguiFeelUIController.BlurOnEnter.cs`); chat, the lobby and Baron windows are untouched |
| `TextArea` | `KsTguiTextArea` | A panel with tg's input box around a `TextEdit` (which has no stylebox). `Text` (setting raises nothing), `Placeholder`, `Fluid`, `Rows`, `Monospace`, `Disabled`, `OnChanged`/`OnEnter`/`OnEscape`/`OnBlurred`. Enter (not Shift+Enter) raises `OnEnter` and blurs; Escape too. Tab inserts a tab character in place of the selection (`DontUseTabForIndent` turns it off; the font draws no glyph for it), `MaxLength` stops typing and cuts a paste at the limit (cut in `FrameUpdate`, as the box moves its caret after the change event; code and Tab are not held to it). Not ported: `userMarkup`, `selfClear` |
| `Slider` | `KsTguiSlider` | A `ProgressBar` you drag, on `KsTguiKnobMath`'s DraggableControl numbers with the matrix [1, 0]: `MinValue`/`MaxValue`/`Step`/`StepPixelSize` (null = width over steps), `Unit`, `Format`, `Color`, `AddRange`, `FillValue` (the wide .4 fill), `Animated`, `Content`, `Unclamped`, `TickWhileDragging`, `Disabled`. Fills ease 1s unless dragging, the cursor line and pointer triangle are tg's, the value is inside right-aligned. Press holds 0.1 s before a drag; a click types (Enter/blur commit, Esc reverts); `OnChanged` on every release. The value popup is `PopupControl`, a separate control in the popup layer while the value moves (blurred, composited like the tooltip). tg's Slider popup is a plain div, not a Floating, so it is not animated. The pointer triangle has a one-pixel alpha fringe (`KsTguiSlider.WritePointer`) |
| `Input` / `NumberInput` / `RestrictedInput` right-aligned text | `KsTguiRightAlignedLineEdit` | `text-align: right` on a `LineEdit`: the style box's left margin is kept at width - text - padding, recomputed on text, font, size and focus, so the caret, selection and clicks agree. The base of `KsTguiRestrictedInput` and the edit boxes of `KsTguiNumberInput`, `KsTguiKnob` and `KsTguiSlider`. Setting `Text` raises no event: call `UpdateAlignment()` after it |
| `--font-family-mono` | `KsTguiFonts.GetMonoFont(size, kind)` | System Consolas by PostScript name, else bundled Roboto Mono. Same sizes and Noto fallbacks as `GetFont` |
| `SearchBar` (tg's `interfaces/common/SearchBar.tsx`) | `KsTguiSearchBar` | Magnifier, 6px gap, `Input` filling the rest. `PlaceHolder`, `ShowIcon` (tg's `noIcon`). Use it wherever tg's window has a `SearchBar` |
| `Tabs scrollable` | `KsTguiTabs.Scrollable` | One row scrolling sideways, no bar. 3.25em (39px) `mask-image` fade at each edge that has more (alpha 0 to 66% of 39px, ramping to 1): a real alpha mask, the tabs are drawn into an offscreen target (`RenderChildOverride`, in place of the first tab, so the scroll buttons stay unmasked) and drawn back through `tgui_tabs_mask.swsl` (premultiplied blend); the alpha maths is `KsTguiTabsMask.Alpha`, the shader repeats it, a scroll button there for half the width (smooth), the wheel scrolls a 100px notch at once, the selected tab is centred (first time at once). Not for `Vertical`. Its fade and buttons are a child only while scrollable: use `ClearTabs()`, not `RemoveAllChildren()`. Used by the chat header |
| `overflow-y: auto` box (`Section scrollable`, `overflowY="auto"`) | `KsTguiScrollContainer` | A `ScrollContainer` that measures the content once, at the width it is arranged at. The bar and its 15px column exist only while the content overflows. `KsTguiSection` uses it; use it, not `ScrollContainer`, in every ported window |
| `overflow-y: scroll` (`Window.Content scrollable`) | `KsTguiScrollContainer` with `ReserveVertical` (`ReserveHorizontal`) | The 15px column is always taken and the track and arrows drawn exactly as when it overflows, with no thumb (`KsTguiScrollContainer.StyleClassDisabled`; tg's ChemMaster sized to fit shows them unchanged); the wheel passes through while nothing overflows. Only where tg's window content is `scrollable`: Apc, ChemMaster, PDA home, Fax, ReagentDispenser (whose Beaker section is the only `fill`: the root box inside the scroll container is stretched to the viewport) |
| `Tooltip` | any `ToolTip` / `TooltipSupplier` | Automatic in every window but KS14's diegetic ones (`KsBaseWindow`): `KsTguiFeelUIController.Tooltips.cs` takes the engine's tooltip and places it 6px from its control (below unless `KsTguiTooltip.SetPlacement`), flipped and shifted to stay inside the window, opening after 200ms of rest, scaling .9 to 1 and fading in and out over 0.2s with tg's shadow and its backdrop blur (`KsTguiBackdropBlur`, below). Outside windows, tooltips stay at the mouse. For tg's `content={<Component/>}`, return `KsTguiTooltip.Create(control)` from a supplier, a new one each time (the old one may still be fading) |
| `backdrop-filter: blur(12px)` + translucent popup background | `KsTguiBackdropBlur`, `KsTguiScaledRaster`, `KsTguiBackdropBackground`, `KsTguiTint`, `KsTguiBackdropPanel`, `KsTguiBackdropUIController`, `KsTguiBackdropMath` | Not a control: a popup host owns one `KsTguiBackdropBlur` and calls `Draw` before its contents (`KsTguiTooltipHost`, `KsTguiScaleHost` for a dropdown) or is a `KsTguiBackdropPanel` (a `PanelContainer`, for a popup that draws its own translucent box, as the knob's value). `Draw` renders everything drawn before the anchor, the popup layers' earlier siblings and so on from the root, into an offscreen target the size of the popup, blurs it in two Gaussian passes (sigma 12 px, `backdrop_blur.swsl`) with mirrored edges as Chromium does, and draws it clipped to the rounded box (`backdrop_composite.swsl`) at the popup's fade and scale. The ancestors on the way must draw nothing themselves (a window's background is a child panel, so a window is fine). While it runs `ScalingViewport` only blits its last picture (`KsBackdropPassActive`, per client, on `KsTguiBackdropUIController`), so the world is not rendered twice; screen-space overlays and a popup's own blur are skipped in the pass. **Exact sRGB**: a browser blurs the encoded pixels and paints the element's translucent background over them in sRGB, so the blur shader averages `zToSrgb` values and `backdrop_composite.swsl` blends the popup's CSS colour at its CSS opacity (`tint`, a `KsTguiTint`: `opacity * colour + (1 - opacity) * behind` on encoded values) inside the rounded mask and outputs it opaque. The raw tint comes from the palette (`KsTguiThemePalette.TooltipTint` / `MenuTint` / `WindowDimmerTint`) via `KsTguiStyleBox.Tint` on the popup's box (not the `Translucent` colour); `KsTguiBackdropBackground` finds the tinted panel and, only while the pass painted this frame, swaps its box for `WithoutBackground()` through `PanelOverride`, so the border still draws. With the blur off (or inside another pass) the panel draws its `Translucent` background as before. **Animating popups** (scale or fade below 1: tooltip, dropdown and knob popup open and close) are rendered once at scale 1 into a target and drawn scaled and faded about the pivot at subpixel precision (`KsTguiScaledRaster`, `scaled_raster.swsl`, premultiplied), because the engine places scaled children at `(Vector2i)Transform(...)`, which jitters by whole pixels; the host stays on its final pixel (`PlaceHost` no longer shifts it), and once settled it draws as any control. Cost: one extra render of the popup's contents per animated frame, 0.2 s per open or close, plus the existing backdrop pass. Off with the tgui theme or `klovn.ui.tgui.backdrop_blur`. Scissored (scroll) controls in the pass clip with the engine's own render-target scissor |
| `Floating` closing transition | `KsTguiDropdownGhost` | A non-interactive control in the popup layer that holds a closed dropdown's contents (the scale host, taken from the popup and given back after) for 0.2 s, in the tree so they have the real UI scale (out of the tree a control's scale falls back to 1), scale to .9 and opacity to 0 from wherever the opening got to. The popup is never parented again; the ghost goes at once if it opens again, at the end, or on a theme change |
| `MaterialCostSequence` | `KsTguiMaterialCostSequence` | Material sprites in a row, sheets under each, white / orange `#f08f11` / red `#db2828` for enough twice, once, or not at all. The lathe's design, x5 and x10 tooltips |
| `Fabricator.tsx` / `Autolathe.tsx` (`DesignBrowser`) | `KsTguiLatheWindow` | Every lathe, in one of two layouts chosen by data: a lathe with `KsTguiAutolatheLayoutComponent` (a networked marker, added to the `Autolathe` prototype by `Prototypes/_KS14/Entities/Structures/Machines/Partials/autolathe_layout.yml`, so `AutolatheHyperConvection` inherits it) gets tg's Autolathe, 670x600: `KsTguiLatheMaterialSummary` (Section "Total Materials": a total bar with good/average/bad at 85%/25% of `MaterialStorageComponent.StorageLimit`, text in sheets, then a Collapsible of one row per stored material: a mirrored `KsTguiProgressBar` with `BackgroundColor` = the material's colour, "Eject:" and x1, x5, x all, sorted by number and each once) over the browser, with the build queue (SS14 only) in a section under the designs that shows only while something is printing or queued. Every other lathe gets the Fabricator: browser, queue column and the `KsTguiMaterialList` bar. Both: the search is in a Section of its own under the designs Section's title (`Results for "text"` while searching, searching spans every category, a new category clears it), no amount box (clicking a design queues 1, `[Max: N]` takes a typed amount), no "Building items..." dimmer (SS14 queues while printing). No `StorageLimit` on the autolathe means no maximum: the total bar is full with no ranges |
| `Dropdown` | `OptionButton` | 22 tall: text cell (6px/3.5px margins) plus a 22x22 chevron cell drawn by shader `KsTguiGlyphDropdownCell` (1px divider, chevron that `KsTguiFeelUIController.Dropdown.cs` turns 180 degrees over 0.2s while open). Menu opens 6px below, 200px max, 4px padding, eases in and out (scale .9 to 1, opacity, 0.2s, CSS `ease`; the engine closes it at once, so `KsTguiDropdownGhost` draws it fading out) and blurs what is behind it. Rows hover instantly, leave in 0.1s, no stray margin. No `buttons`, `searchInput`, `over` |

The Section's text colour is the body's normal white. The grey-blue labels in tg's air alarm come from `LabeledList`, and
the green values from a per-value `color`, so colour them there, not on the Section.

## Palette

Use `KsTguiPalettes` and `KsTguiColors`, never a hex copied from a screenshot. CSS alpha doesn't translate: the engine
blends in linear light and CSS in sRGB, so `rgba(0,0,0,.33)` over the window is
`KsTguiPalettes.Translucent(Color.Black, 0.33f, KsTguiPalettes.Base)`, or the existing `KsTguiPalettes.Section`.
`Translucent` is exact over one assumed backdrop (the window base) and wrong over lighter or darker content, so it is for
dark overlays on near-base content only. A light colour over varied content, or a popup, must composite in sRGB instead
(`KsTguiTint` + the backdrop pass, above).

## Porting a tgui interface 1:1

The stock window stays, because it's what Baron shows. The pattern, as in `Content.Client/_KS14/Tgui/Windows/Fax/`:

1. New window in `Content.Client/_KS14/Tgui/Windows/<Feature>/KsTgui<Feature>Window.cs`, `: FancyWindow`, so it gets the
   title bar and drag. Title, size, then contents built from the library.
2. Interface `IKs<Feature>Window` with what the BUI needs. The stock window implements it through a
   `*.Klovn.Tgui.cs` partial.
3. The stock BUI picks, in a marked block:
   ```csharp
   // KS14 start: the tgui theme gets its own layout
   _window = KsTguiTheme.ReadEnabled()
       ? this.CreateWindow<KsTguiFaxWindow>()
       : (IKsFaxWindow)this.CreateWindow<FaxWindow>();
   // KS14 end
   ```
   A window already open when the toggle flips stays until it's reopened.
4. A `Disposed` check on the field needs a cast to `Control`, since it's an interface now.

Rules for a port:

- **No lost function.** Every action, datum and KS14 addition in the stock window stays reachable. Put it where tg would.
- **No fake data.** What SS14 doesn't have is left out, not shown as a placeholder. A tg feature is added only if the
  server already supports it or it's a small addition.
- **Don't change networking** unless it's essential.
- **Strings** go in `Resources/Locale/<culture>/_KS14/tgui/<feature>.ftl`, in en-US and ru-RU. Reuse existing keys first.
- **Test:** the BUI opens your window with the theme on and the stock one with it off, and each action reaches the server.

## Designing an original window

For a window tg never had (a KS14 console, a new machine). It must read as tgui: the same few shapes, tg's own control for each
datum, none of SS14's habits. The survey behind every rule (446 interfaces, counts, archetype skeletons, the hazard-stripe
catalogue) is in [designing.md](designing.md). tg paths are under `tgui/packages/tgui/interfaces/`.

**Start from a template, not a blank page.**

1. Say what the window is: a device, a catalogue, a console with pages, a monitor with a list, a question. Pick the archetype in
   `designing.md` (A device column, B sidebar + content, C top Tabs, D status + table, E modal, F drill-down).
2. Open the closest tg interface (`Smes.tsx`, `Cargo/CargoCatalog.tsx`, `PowerMonitor.tsx`, `TextInputModal.tsx`) and port its
   skeleton as in *Porting a tgui interface 1:1*. Then swap in your data. With no tg repo, `designing.md` describes the shape.
3. Take its `width`/`height` times 12 for `SetSize`. Widen only for strings that need it, Russian especially.

Typical sizes (median of 273 windows is 500x450): device 300-450 x 115-450, catalogue 700-800 x 600-750, console 400-600 x 550-700,
modal 240-425 wide with a height computed from the text.

### Layout

- **A window is a column of Sections.** Status first, controls next, lists last (Apc, ThermoMachine, Cryo). A single title-less
  Section when the window title already names it (`AtmosPump.tsx`, 335x115).
- **Titles:** one or two words (88% of 417), sentence case in new windows, no colon. **Labels** never end in a colon (0 of 669);
  `KsTguiLabeledList` adds it.
- **A Section's own actions go in `Buttons`**: power, refresh, eject, back, help, a search box (235 Sections).
- **Scrolling:** several Sections of unknown height scroll at the window (`KsTguiScrollContainer` with `ReserveVertical`; Apc, Cryo,
  Fax, Gps). **One growing list** is a `Fill` + `Scrollable` Section and the rest keep their height (`PowerMonitor.tsx`, `Cargo`).
  Never both.
- **Sidebar + content:** sidebar 160-200px, search first, then vertical `KsTguiTabs`, in a `Fill` Section; the content in a `Fill` +
  `Scrollable` Section (`PersonalCrafting/index.tsx`, `Uplink/GenericUplink.tsx`).
- **Top tabs** sit inside a Section, fluid, with icons; never loose on the window background (`Cargo/index.tsx`).
- **Drill down, don't nest tabs:** change a Section's title and put Back (`arrow-left`, disabled at home) in its `Buttons`
  (`AirAlarm/index.tsx`).
- **Modals:** `Section fill`: message in label colour, input growing, then Cancel (`bad`) and Submit (`good`); Enter submits, Esc
  cancels (`TextInputModal.tsx`, `common/InputButtons.tsx`).

```csharp
// A device window (archetype A): status Section, then controls with the power switch in the Buttons slot
var status = new KsTguiSection { Title = Loc.GetString("ks-tgui-example-status") };
var charge = new KsTguiProgressBar { MaxValue = 1f };
charge.AddRange(KsTguiColor.Good, 0.5f, float.PositiveInfinity);
charge.AddRange(KsTguiColor.Average, 0.15f, 0.5f);
charge.AddRange(KsTguiColor.Bad, float.NegativeInfinity, 0.15f);
status.AddChild(charge);
var controls = new KsTguiSection { Title = Loc.GetString("ks-tgui-example-controls"), Buttons = powerButton };
```

### Data to control

| Datum | Use | Not |
| --- | --- | --- |
| Name: value | `KsTguiLabeledList` in a Section; colour the value | Two `Label`s in a row |
| A level (charge, health, fill) | `KsTguiProgressBar` + `AddRange`. tg's commonest set: good from .5, average .25-.5, bad below. A fixed colour for a quantity with no good side (supply teal, draw pink) | Thresholds in code |
| Pressure, or a gauge with an alarm | `KsTguiRoundGauge` with real ranges (Canister) | A ProgressBar |
| A typed or dragged number | `KsTguiNumberInput` with `Unit`; a `Max` Button (`plus`) beside it (`AtmosPump.tsx`) | `FloatSpinBox` |
| A wide range with steps | `KsTguiSlider` between fast/step Buttons (`Smes.tsx`) | |
| One dial | `KsTguiKnob` (Canister) | |
| 3-6 modes | A row of `KsTguiButton`s, `Selected` on the current (Cryo, SolarControl) | `OptionButton` |
| Many choices, browsed | Vertical `KsTguiTabs`, or `KsTguiList` (SpawnSearch). `OptionButton` only past about 7 options, for rare settings | A hand-styled `ItemList` |
| Records | `KsTguiTable`: header row, `Candystripe` rows, `Collapsing` narrow columns, numbers right-aligned | |
| A flag | `KsTguiCheckboxButton`; in a filter list `Fluid` + transparent | `CheckBox` |
| Empty, unavailable, needs login | `KsTguiNoticeBox` (Info blue, Danger red, default amber) | An empty Section; a lone red `Label` |

### Buttons

- **Icon + short text** for an action (42% of tg's 1739 buttons), **icon only** only with a `ToolTip` (27%), text only inside lists.
- **A toggle names its state and is `Selected` when on**: `Icon` `Power`/`Times`, text `On`/`Off` (the pair is in 20+ windows).
- **Icons by verb** (tg counts): close, clear, cancel `times` 50; eject and Insert `eject` 48; add, max `plus` 44; delete `trash` 31;
  refresh, reset `sync` 28; power `power-off` 21; back `arrow-left` 20; help `question` 18; remove `minus` 16; undo `undo`; print
  `print`; home `home`; login `lock-open`, logout `lock`; search `search`; settings `cog`. A missing glyph is a const in `KsTguiIcons`.
- **Colour means something:** none for ordinary actions; `Good`/`Bad` for the pair that confirms or abandons (Submit/Cancel, log
  in/out); transparent for a button that is a list row. `Caution`/`Danger` are rare (alarm states).
- **Destructive is `KsTguiConfirmButton`** (62 in tg), usually `trash` and `Bad`, `ConfirmText` a short question.
- **Unavailable is `Disabled` plus a `ToolTip` saying why**, not a hidden button (`Cargo/CargoCart.tsx`).
- `Compact` is rare (10 of 1739). `Fluid` is for list rows, sidebar entries and a small window's one main action.

```csharp
var powerButton = new KsTguiCheckboxButton { Icon = KsTguiIcons.Power, Text = Loc.GetString("ks-tgui-example-power") };
var purge = new KsTguiConfirmButton { Icon = KsTguiIcons.Trash, Text = Loc.GetString("ks-tgui-example-purge"), Color = KsTguiColor.Bad };
sendButton.Disabled = !canSend;
sendButton.ToolTip = canSend ? null : Loc.GetString("ks-tgui-example-shuttle-away", ("location", location));
```

### Colour, text, tooltips

- Colour carries **state**: label for captions and prompts, good/average/bad for a state word, never decoration. Raw colours are for
  data that has a colour (a reagent, damage type, department, wire): 72 of 651 files contain any hex. Functional panels set no
  background (99 `backgroundColor` in 63 files: swatches, previews, splash pages).
- Text is left aligned. Centre only modal buttons, a `KsTguiNoticeBox` and an empty-state message.
- Units follow the value with a space (`101 kPa`). A figure that changes while open is a `KsTguiAnimatedNumber` (38 uses in tg), or
  `KsTguiButton.ShowNumber` on a button. SI scaling is `formatPower`/`formatSiUnit` (`reference/tgui-core/dist/common/format.js`).
- Type over 24px is one emphasised readout, not a heading (43 of 651 files, a quarter of them antagonist greetings).
- Tooltips explain (why disabled, what an icon does, a limit); they never repeat the label. They are automatic here.

### The hazard stripe effect

tg's diagonal stripes are the **NoticeBox** (`reference/tgui-core/styles/components/NoticeBox.scss`): `repeating-linear-gradient(-45deg,
transparent 0 10px, hsla(0,0%,0%,.1) 10px 20px)` over the colour darkened to .825 lightness and .75 chroma, bold italic; the `black`
box has white .1 stripes. That is the only stripe in the default look; neither a ProgressBar nor the lathe has one. The one real
yellow/black hazard is `AnomalousWeatherTower.tsx` (`45deg`, `#ffc107` and black, 15px each, behind a round red button). Catalogue in
`designing.md`.

| Need | KS14 |
| --- | --- |
| A message in tg's notice style | `KsTguiNoticeBox { NoticeColor = KsTguiNoticeColor.Danger }`; every tg colour name; text, wraps, margins |
| The stripe behind other controls, in XAML | `<StripeBack KsNoticeColor="Danger">`. Under tgui it uses the style box of the `KsTguiNoticeBox` of that colour (null is the default notice); Baron ignores it. `KsTguiStripeBackTest`. SS14's cargo, atmos, crew manifest and options windows already use it |
| A striped panel from code | A style rule giving `new KsTguiNoticeStyleBox(color, whiteStripes)` with 4/6 content margins, as `AddNoticeBoxRules`; not `PanelOverride` |
| Yellow/black hazard (tg's anomalous weather tower) | `KsTguiHazardPanel` around the control: `#ffc107`/black 15px bands at `45deg` (`KsTguiHazardStyleBox`, mirrored notice geometry), a 3px `outset` `#333` border and the contents 6px inside it (`m={1}`). Under Baron it draws nothing and lays out as if absent (it is a `Container`, not a `PanelContainer`, because Baron paints every bare `PanelContainer`). `Shown`/`ShownChanged` say which theme is on, for a window whose control is coloured differently in each. Used on the nuclear reactor's SILENCE button (`NuclearReactorWindow.Klovn.Tgui.cs`). One alarming control per window, never an ordinary warning: when a window has a control like that (an emergency stop, a purge, an alarm silence), suggest the stripes to the user rather than adding them unasked. `KsTguiHazardTest` |
| Another stripe width, hue-matched dark stripes (tg's chat alerts) | **Not available.** `KsTguiNoticeStyleBox` is fixed 10px, `-45deg`, stripes black or white at .1; it would need a stripe colour parameter. Don't fake it with a texture |

### Anti-patterns

A hand-coloured panel, a border around a group, centred body text, a big heading in the window, a colon after a label, a hidden button
where a disabled one with a tooltip belongs, tabs inside a tab, raw hex for a state, a `Button` under its Section instead of in its
`Buttons`. None appears as a pattern in tg's windows; the counts are in `designing.md`.

## Retrofitting an existing window

For a window that keeps its layout, the look comes from the same containers, and Baron must not change by a pixel:

| Stock | Retrofit |
| --- | --- |
| `PanelContainer` with an inline flat `PanelOverride` | `<tg:KsSectionPanel BaronColor="<old colour>">`. A style rule decides, so a live toggle works |
| A heading row with no separator | Wrap it in `<tg:KsSectionTitle>` |
| A heading above an `HSeparator` | Add the `KsTguiSectionLine` class to the separator, so no second line is drawn |
| Label column of a label/value row | The theme-gated `KsTguiLabelKey` class, not `LabelKeyText`, which would change Baron |

Mark each with `<!-- KS14: ... -->` (CLAUDE.md §3). No layout changes. Leave panels whose colour means something.

## Examples: creating a window

**A status block.** tg's is a `Section` holding a `LabeledList`, with colour on the values:

```xml
<!-- do this -->
<BoxContainer Orientation="Vertical" SeparationOverride="6" Margin="6">
    <tg:KsTguiSection Title="{Loc 'ks-tgui-example-status'}">
        <tg:KsTguiLabeledList Name="StatusList"/>
    </tg:KsTguiSection>
</BoxContainer>
```
```csharp
StatusList.AddItem(Loc.GetString("ks-tgui-example-pressure"), pressureText, KsTguiPalettes.Good);
```
```xml
<!-- not this - a hand-built imitation: wrong sizes, no theme support, nothing lines up with real sections -->
<PanelContainer>
    <PanelContainer.PanelOverride>
        <gfx:StyleBoxFlat BackgroundColor="#171717" BorderColor="#48739E" BorderThickness="0 0 0 2"/>
    </PanelContainer.PanelOverride>
    <BoxContainer Orientation="Horizontal">
        <Label Text="Pressure:" FontColorOverride="#8F9FB0"/>
        <Label Text="101 kPa" FontColorOverride="#67BA2B"/>
    </BoxContainer>
</PanelContainer>
```

**Actions belong on the section.** tg puts a section's controls in its title row:

```csharp
// do this
var refreshButton = new KsTguiButton { Icon = KsTguiIcons.Sync, Text = Loc.GetString("ks-tgui-example-refresh"), Compact = true };
section.Buttons = refreshButton;

// not this - a stock button in a row of its own, under the section it acts on
root.AddChild(section);
root.AddChild(new Button { Text = "Refresh" });
```

**Say what a state means with its tgui colour.** A button's colour is a `KsTguiColor`, and a selected toggle is `Selected`:

```csharp
// do this
var sendButton = new KsTguiButton { Text = Loc.GetString("ks-tgui-example-send"), Color = KsTguiColor.Green };
powerButton.Selected = isPowered;

// not this - bypasses the per-state rules, so it loses hover, press and disabled colours
var sendButton = new KsTguiButton { Text = "Send", ModulateSelfOverride = Color.Green };
```

**Tell the player why something's unavailable.** tg uses a notice, not an empty section:

```csharp
// do this
section.AddChild(new KsTguiNoticeBox { Text = Loc.GetString("ks-tgui-example-no-peers"), NoticeColor = KsTguiNoticeColor.Danger });

// not this - in tg's colours, but not tg's component, and it doesn't wrap
section.AddChild(new Label { Text = "No peers.", FontColorOverride = KsTguiPalettes.Bad });
```

**A slot that can be empty.** tg disables its Eject button then. Here the button becomes Insert, which puts the held item in:

```csharp
// do this: Filled follows the state; SlotId makes a press the message the server's ItemSlotsSystem handles
var ejectButton = new KsTguiItemSlotButton { SlotId = SharedReagentDispenser.OutputSlotName };
ejectButton.SlotPressed += message => OnEjectPressed?.Invoke(message); // the BUI SendMessage()s it
ejectButton.Filled = state.OutputContainer is not null;
// not an ItemSlots slot (a material, a plain container): no SlotId, handle ejectButton.EjectPressed / InsertPressed

// not this - an Eject nobody can press, so the player must go and use the item on the machine
ejectButton.Disabled = state.OutputContainer is null;
```

**Spacing comes from tgui's units.**

```xml
<!-- do this: tg's <Stack vertical> around sections, inside the 6px window margin -->
<BoxContainer Orientation="Vertical" SeparationOverride="6" Margin="6">

<!-- not this: SS14's habitual values look nearly right, and everything ends up slightly off -->
<BoxContainer Orientation="Vertical" SeparationOverride="4" Margin="10 5">
```

**A grid of cards fills the width.** tg's cards are fixed and leave the remainder at the right. Here that reads as a hole:

```csharp
// do this: columns from the minimum card, the width shared out, edges flush
var grid = new KsTguiCardGrid { HorizontalExpand = true };
grid.AddChild(new KsTguiImageButton { ImagePrototype = id, Text = name });

// not this: fixed-width cards in a WrapContainer, with the leftover as a gap on the right
var wrap = new WrapContainer { SeparationOverride = 4, CrossSeparationOverride = 4 };
```

**A lost feature is a bug, however good the layout looks.** The fax port kept the dropdown's default-destination
behaviour when it turned recipients into tg's list of buttons. Going from a dropdown to buttons is fine. Dropping what
the dropdown did isn't.

## Examples: retrofitting a window

**A dark panel.** Real change, from `MicrowaveMenu.xaml`:

```xml
<!-- do this: the old colour moves to BaronColor, and every other attribute stays -->
<!-- KS14: tgui Section body under the tgui theme, flat colour under Baron -->
<ksTgui:KsSectionPanel
    Name="IngredientsPanel"
    MinSize="300 128"
    Margin="5 5" BaronColor="#1B1B1E">
```
```xml
<!-- not this: Baron loses its colour -->
<ksTgui:KsSectionPanel Name="IngredientsPanel" MinSize="300 128" Margin="5 5">

<!-- not this: the override beats every style rule, so tgui can never apply. Nothing warns -->
<ksTgui:KsSectionPanel Name="IngredientsPanel" MinSize="300 128" Margin="5 5" BaronColor="#1B1B1E">
    <PanelContainer.PanelOverride>
        <gfx:StyleBoxFlat BackgroundColor="#1B1B1E"/>
    </PanelContainer.PanelOverride>
```

**A heading with a separator.** Real change, from `BarSignMenu.xaml`:

```xml
<!-- do this: classes only, so Baron doesn't change, and there's still one line -->
<!-- KS14: heading and its separator are a tgui Section title and underline under the tgui theme -->
<Label Text="{Loc 'barsign-ui-set-label'}" StyleClasses="LabelSubText KsTguiSectionHeading" Margin="30 0"/>
<customControls:HSeparator Margin="0 0 0 5" StyleClasses="KsTguiSectionLine"/>
```
```xml
<!-- not this: two lines under tgui, and Baron gains padding -->
<tg:KsSectionTitle><Label Text="{Loc 'barsign-ui-set-label'}" StyleClasses="LabelSubText"/></tg:KsSectionTitle>
<customControls:HSeparator Margin="0 0 0 5"/>
```

**A label column.**

```xml
<!-- do this: the rule exists only in the tgui sheet -->
<Label Text="{Loc 'id-card-console-window-full-name-label'}" StyleClasses="KsTguiLabelKey"/>

<!-- not this: LabelKeyText is styled under Baron too, so it changes -->
<Label Text="{Loc 'id-card-console-window-full-name-label'}" StyleClasses="LabelKeyText"/>

<!-- not this: permanent, on both themes, and it survives a toggle -->
<Label Text="{Loc 'id-card-console-window-full-name-label'}" FontColorOverride="#8F9FB0"/>
```

**Keep the layout.** A retrofit swaps containers and classes.

```xml
<!-- not this: moving rows into a KsTguiSection reflows the window and changes Baron. That's a port -->
<tg:KsTguiSection Title="{Loc 'id-card-console-window-full-name-label'}">
    <LineEdit Name="FullNameLineEdit"/>
</tg:KsTguiSection>
```

**Leave a colour that means something.** A panel that's red for an alert or green for a department is information, so
it stays. Convert only neutral greys.

```xml
<!-- leave this one alone -->
<gfx:StyleBoxFlat BackgroundColor="#A22824"/>
```

**In C#, gate on a style property, not the cvar.** Under a sheet swap this needs no subscription:

```csharp
// do this, in StylePropertiesChanged
_highlight = TryGetStyleProperty<Color>("ks-my-highlight", out var color) ? color : DefaultHighlight;

// not this - frozen at construction, so a live toggle leaves it wrong
_highlight = KsTguiTheme.ReadEnabled() ? KsTguiPalettes.PrimaryElement : DefaultHighlight;
```

## Window themes (syndicate, wizard, admin)

tg's `<Window theme="syndicate">` swaps the whole palette of one window. Here a theme is a **complete extra stylesheet**
(`StylesheetManager.KsGetThemedSheet`: the Nanotrasen sheet built from a `KsTguiThemePalette`) that a window sets as its own
`Control.Stylesheet`, so it reaches everything inside the window and nothing outside. `KsTguiWindowTheme` (Shared) is
Nanotrasen, Syndicate or Wizard.

```csharp
// do this: FancyWindow has the property; any other window (a DefaultWindow, as StoreMenu is) uses the extension
window.KsTheme = KsTguiWindowTheme.Syndicate;
storeMenu.SetKsTheme(KsTguiWindowTheme.Wizard);   // KsTguiWindowThemes, in Content.Client._KS14.Tgui

// not this: the sheet is replaced and nothing remembers why, so the toggle, a rebuild and a reopened window all lose it
window.Stylesheet = stylesheetManager.KsGetThemedSheet(KsTguiWindowTheme.Syndicate);
```

- **Admin theme (tg's `theme="admin"`)** is `KsTguiWindowTheme.Admin`, and which windows wear it is **one list**,
  `KsTguiAdminWindows.WindowTypes` (by exact type, so upstream's and the engine's windows need no edit; a private nested
  window goes in `NestedWindowTypeNames`). `KsTguiChromeUIController.SyncWindow` applies it as a window opens. Put a window
  there only if no regular player is shown it. A plain `DefaultWindow` that has to be admin-themed (the player's ahelp) is
  given `SetKsTheme` where it is made. admin.scss sets `--candystripe-even` too, which no other theme does, so
  `KsTguiThemePalette.CandystripeEven` is clear by default and the stripe controls draw it when it is not.
- **Data-driven for stores**: `StoreComponent.KsUiTheme` (`ksUiTheme:`, default Syndicate), read by `StoreBoundUserInterface`.
  Say it in a `Partials/` prototype (`Prototypes/_KS14/Store/Partials/ui_theme.yml`), not by editing the upstream file.
- **Under Baron it does nothing**, and the toggle puts windows back on the UI's sheet and onto the rebuilt themed one.
  A window that was closed catches up in `KsTguiChromeUIController.SyncWindow`.
- **A control reads its colours from the sheet it is in.** A rule that needs a colour a theme changes (primary, label,
  selected, notice, input border, title bar, window gradient, logo) takes it from `_palette` in `KsTguiSheetlet`, never from
  `KsTguiPalettes`, which is the Nanotrasen theme's values written out and stays for what no theme changes (good, average, bad).
  A control that draws itself reads a **style property** that `KsTguiSheetlet.Themes.cs` sets, with the Nanotrasen colour as its
  fallback (what Baron gets).

```csharp
// do this: in StylePropertiesChanged, so it follows the window it's in and the toggle
_edge = TryGetStyleProperty<Color>(KsTguiSheetlet.StylePropertyInputBorder, out var border) ? border : KsTguiPalettes.InputBorder;

// not this: Nanotrasen blue in a Syndicate window, with nothing to say why
_edge = KsTguiPalettes.InputBorder;
```

- **Every derived token is worked out, not read off a picture.** `KsTguiThemePalette` holds each theme's SCSS tokens (HSL) and
  works the rest out as tgui-core does: button `l-5`, hover `+10`, pressed `-7.5`, disabled the element at .5 over the base;
  title bar the base at `+--secondary-lightness-adjustment` unless `--color-secondary` is set; gradient `base +- spread`;
  label the primary's hue at 17.5/57.5; tab text the primary at 90; input border the primary's hue at 100/75, .75 over the
  input background unless `--color-border-primary` is set; NoticeBox the oklch step. `KsTguiWindowThemeTest` checks the
  Nanotrasen result against the hand-written values, so a wrong derivation shows. To add a theme: read its
  `styles/themes/<name>.scss`, add a `Tokens`, a logo (below) and an enum value.
- **Logos** are `bg-<theme>.svg` rasterised white on clear (PNG, 2x the viewBox, even-odd fill), with an attributions entry.
  `KsTguiWindowBackgroundStyleBox` draws it black at .33, fitted to 70% of the window below the title bar by the PNG's aspect.
- **Popups are outside the window.** A dropdown (ModalRoot) and a tooltip (PopupRoot) are not its children, so
  `KsTguiFeelUIController` gives them the sheet of the window under the mouse (`KsTguiWindowThemes.FindThemedSheet`). Anything
  else in those layers (a context menu, a window opened from the window) stays on the UI's theme. A sub-window needs its own `KsTheme`.
- **Pitfall: a palette field is instance state.** The sheetlet is built once per sheet, so `KsTguiSheetlet` rule methods that
  read `_palette` are instance methods. A `static` method or field that bakes in a colour is a Nanotrasen colour in every theme.

## Traps

- **Scrollbar** is Chromium's, measured pixel by pixel from tg screenshots at 100% (ChemMaster for the normal bar, Adminhelp for the thin one), with the numbers in `KsTguiScrollBarMetrics.Normal` / `.Thin`:
  - Normal: a 15px column, a 9px pill thumb (3px either side), and arrows of solid pixel rows 1, 3, 5, 7, 9 wide, the top one's tip 6px from the column's top and the bottom one's 7px from its bottom. The thumb stops 2px short of each 15px arrow cell.
  - Thin (`scrollbar-width: thin`, tgui's TextArea): a 10px column, a 6px thumb, arrows 2, 4, 6 wide, each tip 4px from its end, 1px gap. A browser draws a bar between a box's padding and its border, so `KsTguiTextArea`'s TextEdit reaches the right border and its text box carries the right padding; the whole column then stays inside the TextEdit, under the mouse (`scrollbar-gutter: stable`: the column is kept, the bar shown only while the text overflows).
  - The track is `--color-scrollbar-base`, the Section colour, which a page inherits everywhere except inside a `.Section`, a TextArea or a dropdown, which set it transparent. `KsTguiScrollContainer` gives its bars `KsTguiSheetlet.StyleClassFilledScrollBar` unless it has a `KsSectionPanel` ancestor; engine scroll containers stay clear, because their bars overlay content.
  - Arrows are drawn as rows of `DrawRect`, not a triangle: triangle edges come out antialiased and a pixel wider. The column is snapped to whole pixels (`KsTguiScrollBarMetrics.Snap`, which keeps the size: rounding both edges grows 15 to 16).
  - `KsTguiScrollThumbStyleBox`/`KsTguiScrollTrackStyleBox` draw it, `KsTguiScrollArrows` (polled from `KsTguiFeelUIController`) takes arrow clicks and hold-repeat on any bar with a tgui track. A `ScrollContainer`'s bar counts in UI units; the engine's other owners of a bar (TextEdit, ItemList, OutputPanel, Tree) count in pixels, so the step is scaled for those. One thumb colour for every state.
- **Fonts are exact pixels.** `KsTguiFonts.Quantize` picks whole points and a fraction of the scale (`KsTguiScaledFont`), so 14px is 14px, and the font reports a browser's line box (Verdana 12px is 15 tall, not FreeType's 16). Tests reading `StackedFont._main` get a `KsTguiScaledFont`, with the `VectorFont` in `.Inner`.
- **A nested `ParentOf` chain is wrong.** `A.ParentOf(B.ParentOf(C))` never matches a grandchild C; write `A.ParentOf(B).ParentOf(C)`.
- **`KsTguiFonts` sizes are pixels; the engine's are points.** 12 at the engine draws at 16px. Ask `sheet.BaseFont.GetFont(12)` for
  tgui's 12px and it converts. Don't load a font size yourself, or compensate for it. `Stack[0]` of its fonts is an
  empty font that only carries the pixel size for markup, so read metrics from the font, not from `Stack[0]`.
- **Check a window with `game-screenshots`.** Nothing headless draws, and both of the biggest bugs in the port, the wrong
  text size and sections with no padding, passed every test.
- **A code-built `FancyWindow` adds to `ContentsContainer`, not to itself.** `AddChild(root)` compiles and puts the
  contents over the title bar and close button.
- **Clear a section with `ClearBody()`.** `RemoveAllChildren` takes the section's own title and body with it.
- **Prefer a style rule to reading the cvar.** A rule swaps on a live toggle. A value in XAML or code beats the sheet
  permanently on that control.
- **A rule belongs to a theme.** Add it to `KsTguiSheetlet` in the partial for its area, and don't assume Baron has it.
  A class with no Baron rule is unstyled under Baron.
- **A parent's pseudo-class doesn't restyle its children.** `SwitchButton` needed `Restyle()` on its children, and a rule
  like `E<X>().PseudoPressed().ParentOf(...)` changes nothing on screen without it.
- **Anything can be cut off.** `KsWindowContentsFitTest` runs on both themes. tg windows are small and fixed, and SS14
  text is longer, in Russian especially.
- **Headless measures DejaVu, and most players get Verdana.** Nothing headless draws either, so compare a real client
  with a screenshot of the tg window before calling a layout done.
