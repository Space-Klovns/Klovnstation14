---
name: tgui
description: How to style and lay out client UI so it resembles /tg/station's tgui - reading tgui's React and SCSS sources, converting its units, the KsTgui control library (Section, LabeledList, NoticeBox, Button, Tabs, ProgressBar, Icon), the palette tokens, porting a window 1:1 while keeping the stock window for the Baron theme, and retrofitting tgui Sections into existing XAML without changing Baron. Use when adding or restyling a window that should look like tgui, porting a tgstation interface, choosing tgui colours or spacing, or when a window looks unlike tg. For XAML, sheetlet and layout mechanics use the ui skill; for BUI wiring use the bui skill.
---

# Making UI look like /tg/station

KS14's default theme is a port of tgui's `nanotrasen` theme, and Baron is what `klovn.ui.tgui` turns it back to. The `ui`
skill's *The tgui theme* section covers the machinery: palettes, fonts, window chrome, fades. This skill is about making
a given window look right.

## Where the truth is

Never guess a tgui value. tgstation is at `C:/Space/SS13 Dev/Repo/tgstation`:

| What | Where |
| --- | --- |
| A window's layout | `tgui/packages/tgui/interfaces/<Name>.tsx`, or a folder of that name |
| A component's structure and CSS | `tgui/node_modules/tgui-core/dist/components/`, `.../styles/components/<Name>.scss` |
| Spacing and colour tokens | `tgui/node_modules/tgui-core/styles/vars-values.scss` |
| Chat | `tgui/packages/tgui-panel/`, `tgui/packages/tgui-say/` |

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
| `<Dimmer>` | Not ported: use a `KsTguiNoticeBox` or disable the controls |

Window contents sit 6px in from the window edge: `Margin = new Thickness(6f)` on the root box. Stacked sections use
`SeparationOverride = KsTguiSection.Gap` in their parent.

## Control library

`Content.Client._KS14.Tgui.Controls`. Parameterless constructors, so they work from code and from XAML with
`xmlns:tg="clr-namespace:Content.Client._KS14.Tgui.Controls"`. Read the class for the current API.

| tgui | KS14 | Notes |
| --- | --- | --- |
| `Section` | `KsTguiSection` | `Title`, `Buttons` slot, `Scrollable`, `Fill`, `Fitted`. XAML children are the body. Nesting works |
| `LabeledList` | `KsTguiLabeledList`, `KsTguiLabeledListItem` | `AddItem(label, text, color)`; `Buttons` slot. Put it inside a Section |
| `NoticeBox` | `KsTguiNoticeBox` | `NoticeColor` |
| `Button` | `KsTguiButton` | `: Button`. `Icon`, `Color`, `Compact`, `Fluid`, `Selected` |
| `Button` on an item slot | `KsTguiItemSlotButton` | `: KsTguiButton`. `Filled`, `SlotId`. Eject while filled, Insert while empty, never disabled |
| `Icon` | `KsTguiIcon`, `KsTguiIcons` | FA6 Solid glyphs. Add a `const string` when one is missing |
| `Tabs` | `KsTguiTabs`, `KsTguiTabItem` | Vertical too. `Count` is the badge. Use this, not the engine `TabContainer`, in a ported window |
| `ProgressBar` | `KsTguiProgressBar` | `AddRange` for good/average/bad. Text right-aligned, as tgui |
| `NumberInput` | `KsTguiNumberInput` | Value bar at the left, vertical drag, click selects everything. Use it, not `FloatSpinBox`, in a ported window |
| `candystripe` class, `Stack zebra` | `KsTguiLabeledList.Candystripe`, `KsTguiTabs.Candystripe`, `KsTguiCandystripe` | Odd visible rows darkened edge to edge, so hiding a row re-alternates. `KsTguiCandystripe` is a vertical box for any rows. The dark colour is only given by the tgui sheet |
| `Table`, `Table.Row`, `Table.Cell` | `KsTguiTable`, `KsTguiTableRow` | Cells are the row's children; columns size to the widest cell. `HorizontalExpand` on a cell marks an expanding column (tgui marks the others `collapsing`). `Header` for 6px under it, `Candystripe` per row (the header counts in the odd/even) |
| `ColorBox` | `KsTguiColorBox` | `BoxColor`, 12px swatch |
| `Divider` | `KsTguiDivider` | 2px line, 6px space either side; `Vertical` |
| `BlockQuote` | `KsTguiBlockQuote` | `Text`, label colour, left bar |
| `ImageButton` | `KsTguiImageButton` | `: ContainerButton`, drawn from its own state. `ImagePrototype` or `ImageTexture`, `ImageSize` (tg's 64), `Text` (bar at the bottom), `TopLeftText`/`TopRightText` or `AltButtons` (top overlay), `Buttons` (above the bar), `Color` (`Default` is tg's grey, `Primary` the blue; `Transparent` for pickers), `Selected`, `Fluid` (row, `Content` replaces `Text`). Not ported: pressed glow, text outline, `title`, contrast on a disabled row |
| a wrapping grid of `ImageButton` cards | `KsTguiCardGrid` | Columns = how many minimum-size cards fit, the width then shared out equally: constant `Gap`, first and last card flush with the edges at any width and after a resize. `HorizontalExpand` it. Use it for every card grid |
| `RoundGauge` | `KsTguiRoundGauge` | Smooth geometry from tg's 100x50 view box, sharp at any scale. `MinValue`, `MaxValue`, `AddRange`, `AlertAfter`/`AlertBefore`, `Format`, `GaugeSize`. Pass only real ranges: no data, no range. Needle snaps (tg eases) |
| `AnimatedNumber` | `KsTguiAnimatedNumber` | `: Label`. Set `Value`; eases by frame time and does nothing once settled. `Format`, `Initial` (start from 0 on first show). For a figure that changes in place; keep one instance, don't rebuild it with its row |
| `Dimmer` | `KsTguiDimmer` | `KsTguiDimmer.Over(target, out dimmer)` returns a host that stacks the target and the dimmer; add the host where the target was, toggle `dimmer.Visible`. `Content` is centred. Takes the clicks. The standard way to show a locked section (lock icon + the swipe text), with the notice kept above |
| `Modal` | `KsTguiModal` | A dimmer with a panel. Put it in a stacking host beside the page, `Body`, `Open()`/`Close()`, `Closed`. Takes keyboard focus, Esc closes (a focused text box eats the first Esc) |
| `Input` | `LineEdit` | Already themed |
| `Dropdown` | `OptionButton` | Already themed |

The Section's text colour is the body's normal white. The grey-blue labels in tg's air alarm come from `LabeledList`, and
the green values from a per-value `color`, so colour them there, not on the Section.

## Palette

Use `KsTguiPalettes` and `KsTguiColors`, never a hex copied from a screenshot. CSS alpha doesn't translate: the engine
blends in linear light and CSS in sRGB, so `rgba(0,0,0,.33)` over the window is
`KsTguiPalettes.Translucent(Color.Black, 0.33f, KsTguiPalettes.Base)`, or the existing `KsTguiPalettes.Section`.

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

## Window themes (syndicate, wizard)

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
