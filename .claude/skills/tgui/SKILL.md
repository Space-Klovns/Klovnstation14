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
| `Icon` | `KsTguiIcon`, `KsTguiIcons` | FA6 Solid glyphs. Add a `const string` when one is missing |
| `Tabs` | `KsTguiTabs`, `KsTguiTabItem` | Vertical too. `Count` is the badge. Use this, not the engine `TabContainer`, in a ported window |
| `ProgressBar` | `KsTguiProgressBar` | `AddRange` for good/average/bad. Text right-aligned, as tgui |
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

## Traps

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
