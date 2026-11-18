---
name: ui
description: How client UI is built in this repo - XAML and its code-behind, layout and mouse filtering, the sheetlet stylesheet system (specificity, the tgui and Baron themes overriding global defaults, dead StyleNano classes, flat/translucent styles, the reusable gradient stylebox), UI screens and controllers, windows and BUIs, drawing inside a Control, UI localisation, and testing UI headlessly. Also maps the reworked lobby. Use when writing or restyling any XAML, Control, window, BUI, sheetlet or lobby UI, or when a window's bottom is cut off, a style silently doesn't apply, a panel is opaque when it shouldn't be, clicks don't reach a button, or a control vanishes.
---

# Client UI

Facts read from `RobustToolbox` v291 (`RT` = `RobustToolbox/Robust.Client/UserInterface`) and content. Nothing
headless draws, so styling and visuals are only verified once someone has looked in game. Say so when reporting.

Already in `CLAUDE.md`, not repeated here: *CVars: unsubscribe from anything that does not live as long as the
process* (§6, applies to every `Control`). Client localisation is the `localization` skill.

## Where to look

| What | File |
| --- | --- |
| XAML build (glob, weaving) | `RobustToolbox/MSBuild/XamlIL.targets`, imported by `Content.Client.csproj` |
| Named-control generator | `Robust.Client.NameGenerator/XamlUiPartialClassGenerator.cs` |
| Layout | `RT/Control.Layout.cs`, `RT/Controls/BoxContainer.cs`, `LayoutContainer.cs` |
| Hit testing / mouse | `RT/UserInterfaceManager.Input.cs` |
| Selector specificity | `RT/Stylesheet.cs:209-232`, sort in `RT/Control.Styling.cs:221-246` |
| Active stylesheet and sheetlet order | `Content.Client/Stylesheets/StylesheetManager.cs`, `Stylesheets/NanotrasenStylesheet.cs` |
| Selector helpers (`E<T>`, `CButton`, `.Class` ...) | `Content.Client/Stylesheets/StylesheetHelpers.cs` |
| Class name constants | `Content.Client/Stylesheets/StyleClass.cs` |
| Fork global overrides | `Content.Client/_KS14/Tgui/KsTguiSheetlet.cs` (default) or `_KS14/BaronUI/KsBaronUiSheetlet.cs` |
| Gradient stylebox | `Content.Client/_KS14/UserInterface/Styles/KsStyleBoxGradient.cs` |
| Screens / widgets | `RT/UIScreen.cs`, `RT/IUserInterfaceManager.Screens.cs` |
| BUI windows | `RT/BoundUserInterfaceExt.cs` (`CreateWindow<T>`) |
| UI tests | `Tests/UserInterface/UiControlTest.cs`, `Tests/_KS14/Voice/KsVoiceLinkWindowTests.cs` |

## XAML

- XAML is compiled, not parsed at runtime. The glob is `**\*.xaml`, so a `Foo.xaml.old` next to a live file is
  ignored. The reworked lobby keeps its upstream originals that way.
- There are two steps:
  - A source generator emits the named-control members.
  - `CompileRobustXaml` weaves the tree into IL after C# compiles. XAML errors arrive *after* a successful C#
    compile, so read the whole build output.
- Code-behind is `[GenerateTypedNameReferences] public sealed partial class X : Base`, and its constructor calls
  `RobustXamlLoader.Load(this)` before touching any named control.
  - The root element must be the class or its direct base.
  - A missing weave fails at runtime with "No precompiled XAML found for X".
- `Name="Foo"` makes a **property** that calls `FindControl` on every access, not a field. Cache it in a local in hot
  code.
  - `Access` defaults to Protected, which on a sealed class becomes private.
  - Anything another class touches needs `Access="Public"`.
- `{Loc 'key'}` is evaluated **once at construction**. Text with arguments, or text that must change, is set in code.
- `{x:Static ns:Type.Member}` works for attribute values. Inside `StyleClasses="..."` it is unverified, so the list
  is literal space-separated names.
- Use property element syntax for objects:
  ```xml
  <PanelContainer>
      <PanelContainer.PanelOverride>
          <gfx:StyleBoxFlat BackgroundColor="#1b1b1e"/>
      </PanelContainer.PanelOverride>
  </PanelContainer>
  ```
- Content controls need a `clr-namespace:` xmlns prefix. An unknown element almost always means a missing or wrong
  prefix.

## Layout and mouse

- **Constraint priority:** `MinSize` > `MaxSize` > `SetSize`.
- **Measure is capped at the space offered** (`Control.Layout.cs:605`). A squeezed control reports less than it
  needs and is laid out at that size. It *vanishes* rather than overflowing, which is the root of the wrapping-window
  trap in *Wrapping text in a resizable window* below.
- `Label` doesn't wrap. For free text (server names, character summaries), use a `RichTextLabel` with `MaxWidth`,
  which wraps, and fill it with `FormattedMessage.FromUnformatted` so player text can't inject markup.
- **`ClipText="True"` measures zero width** (`Label.cs:372-375`: `MeasureOverride` returns `(0, textHeight)`).
  Unless the label is given width some other way (`HorizontalAlignment="Stretch"` in a sized parent, `MinWidth`), it
  takes up its height and draws nothing. The lobby's name, job and summary vanished this way, leaving an empty gap.
  Only clip a label whose width comes from its parent.
- **BoxContainer:**
  - Children with Expand on the main axis share the leftover space by `SizeFlagsStretchRatio`.
  - `<Control VerticalExpand="True"/>` is the spacer.
  - `SeparationOverride` sets the gap between children.
- **GridContainer: keep `HSeparationOverride` and `VSeparationOverride` equal.** `MeasureOverride` reads
  `Separations` as (columns, rows) but `ArrangeOverride` unpacks it the other way round (`RT/Controls/GridContainer.cs`),
  so rows are *placed* with the column gap while the grid's height was *measured* with the row gap (default 4 each).
  With a wider column gap, every row lands lower than measured, the error builds up row by row, and the last rows
  spill past the grid's bounds over whatever sits below it. With a narrower one they bunch up and leave a gap. The gas
  analyzer's reagent table (`KsFluidListControl`) had `HSeparationOverride = 10` alone. With one reagent the spacer
  under it hid the overflow, with two its bar touched the text, and from three on the bar covered the last row. Set
  both to the same value, and put any extra column spacing in cell `Margin`s. `KsFluidListControlTests.TableRowsStayClearOfTheBar`
  checks it by comparing the last cell's bottom with the grid's own bottom and with the next control's top.
- **ScrollContainer:** `ReturnMeasure="False"` (the default) means the scroll child doesn't push the container's
  desired size.
- **LayoutContainer** (and so every `UIScreen`): children are placed by anchors and margins, e.g.
  `SetAnchorPreset(ctl, LayoutPreset.Wide)` and `SetMarginTop(ctl, -220)` (see `LobbyGui.xaml.cs`).
- **`MouseFilter`** defaults to **Ignore** on `Control`, and only `BaseButton` sets Stop.
  - A panel swallows clicks only if something set Pass or Stop on it.
  - Decorative overlays (fades, glass) still say `MouseFilter="Ignore"` explicitly, so a later style or edit can't
    silently start eating clicks meant for buttons underneath.
- **Visible vs remove:** use `Visible=false` for toggles; it measures as zero and isn't hit-tested. A disposed control
  can't be re-added.
- **Units:** `Size`, `MinSize` and `Position` are virtual (UI-scaled); `PixelSize` is device pixels.
- **Edges can fall a pixel short.** `PixelPosition` and `PixelSize` are each truncated to int separately
  (`Control.Layout.cs:116,168`), so at a fractional UI scale a bar laid out to the screen edge can stop one pixel
  short of it. `StyleBox.Padding` **throws** if negative, so for edge-to-edge chrome use `KsStyleBoxGradient` (equal
  stops for a flat fill) with `Bleed` toward that edge, as `KsLobbyBottomBar` does. Anything drawn past the window
  edge simply isn't shown.
- **A control's width can lag its text (stale arrange).** `Control.Arrange` re-measures a child whose measure went invalid
  (`Control.Layout.cs:649`). If that changes the child's size, `Parent.InvalidateMeasure` → `InvalidateArrange` is
  dropped because the parent is still arranging (`_arranging`), so the parent ends the pass marked valid at its old
  size and stays there until something else changes it. The lobby's ready button kept NOT READY's width after
  switching to JOIN at round start.
  - It shows only through alignment. `ButtonSheetlet` **centres button labels**, so the new text sat centred in the
    stale box, about 50px off. Left-aligned text in a left-aligned button looks right at any stale width.
  - So for a button whose text changes at runtime, don't rely on centring inside a content-sized button. Left-align
    it (`.AlignMode(Label.AlignMode.Left)` on the label rule), or give it a width that doesn't follow the text
    (`HorizontalAlignment="Stretch"`, or a `MinWidth` that fits every locale).
  - It didn't reproduce in a standalone `LobbyGui`, only in the real `LobbyState` screen through an actual round start.
    Bisect on the real flow.

## Stylesheets

The active sheet is `NanotrasenStylesheet`, built from **sheetlets** (`Sheetlet<T>.GetRules`). `StyleNano` and
`StyleSpace` still exist but are `[Obsolete]` and are **not** the UI's stylesheet.

### Registration order

`NanotrasenStylesheet` concatenates these, in this order:
1. Fonts and core rules.
2. Every `[CommonSheetlet]`, found by reflection.
3. The Nanotrasen-specific sheetlets.
4. The fork's sheetlets, registered by hand with `// KS14`:
   ```csharp
   GetSheetletRules<PalettedStylesheet>(typeof(KsBaronUiSheetlet), man), // KS14
   GetSheetletRules<PalettedStylesheet>(typeof(Content.Client._KS14.Lobby.KsLobbySheetlet), man), // KS14
   ```

A new fork sheetlet is either a `[CommonSheetlet]` (`KsInstrumentSheetlet`) or another explicit line here. Use the
explicit line when it has to sit after Baron. `SystemStylesheet` registers only Baron.

### Specificity: classes beat order

A selector scores `(ids, classes, types)` and is compared lexicographically:
- **ids** is 1 if `.Identifier` is set.
- **classes** counts `.Class` names **plus** `.Pseudo*` names.
- **types** is the inheritance depth of the element type, so a `Button` selector outranks a `ContainerButton` one.
- `ParentOf` sums both sides.
- **The later rule wins only on an exact tie.**
- Properties merge rule by rule: the first rule in sorted order that names a property sets it.

So add a class rather than rely on registration order. A bare `E<PanelContainer>()` rule loses to *any* classed
PanelContainer rule, wherever either sits.

### Traps that fail silently

- **Only the child selector exists.** `SelectorDescendant` is internal ("hidden due to performance concerns").
  `A.ParentOf(B)` matches only when A is B's **immediate** parent.
  - A button's label needs its own `CButton().Class(X).ParentOf(E<Label>())` chain.
  - A class on a banner does nothing for buttons nested one container deeper. Put the class on the inner container
    (`LinkBanner.cs`) or on each button.
- **Dead `StyleNano` classes.** `LabelBig`, `AngleRect`, `ButtonBig` (its StyleNano form), `StyleNano.*` constants
  and similar are styled only by the obsolete sheet. Using them does nothing: no error, no log.
  - `StyleClass.cs` notes the replacements: `BackgroundPanel` replaces AngleRect, `BackgroundPanelDark` replaces
    Inset, `LabelWeak` replaces LabelSecondaryColor.
  - Check any `StyleNano.` you copy; `ChatBox.xaml` still has one.
- **Baron overrides global defaults, when it's the active theme.** With `klovn.ui.tgui` off, `KsBaronUiSheetlet`
  restyles every `Label`/`RichTextLabel` font, unclassed `PanelContainer` (opaque `panel_dark_1_clean` texture),
  `ContainerButton` (`button_3` texture), LineEdit, scrollbars, tabs, sliders and tree rows. See *The tgui theme* below.
  - **A bare `PanelContainer` is opaque under Baron, and see-through under tgui.** Don't rely on either: give it a class
    whose rule sets `.Panel(new StyleBoxEmpty())` (or the box you want), or set `PanelOverride`.
  - Baron's fonts lack some glyphs; its `DefaultFont` class falls back to the base font.
- **ModulateSelf tints flat buttons.** `ButtonSheetlet.MakeButtonRules` sets `ModulateSelf` to a palette colour on
  every `CButton()` pseudo-state. That tint lands on anything the button itself draws: a `StyleBoxEmpty`, gradient or
  flat box. It doesn't reach its label, which is a child (`UserInterfaceManager.cs:419,429`). Every state of a flat
  style must also set `.Prop(Control.StylePropertyModulateSelf, Color.White)` (`KsLobbySheetlet` does). Panels too:
  `PanelSheetlet` tints `BackgroundPanel`, so a rule giving it a new box without `.Modulate(Color.White)` comes out dark.
- **Two layout helpers do nothing.** `.HorizontalAlignment()` and `.VerticalAlignment()` write to the `*Expand` key, so
  they're dropped (`StylesheetHelpers.cs:81-89`). Use `.Prop(nameof(Control.VerticalAlignment), value)`.
- **Layout styles need the exact type, and lose to any code.** `.Prop(nameof(Control.Margin), ...)` needs a `Thickness`
  and `MinWidth` a `float`, or it's silently ignored, and `MinSize` only works through its helper. A value set in XAML or
  code is permanent: the sheet can never take that control back (`Control.Layout.Styling.cs`). To let a theme decide a
  margin, leave it out of the XAML and give the control a class.
- **Overrides beat the sheet.** `PanelOverride`, `ModulateSelfOverride` and `FontColorOverride` win over any rule. Some
  upstream code writes `PanelOverride` itself. `ChatUIController.SetChatWindowOpacity` did, until the lobby was
  exempted, so grep for writers before assuming your style applies.

### Building rules

```csharp
// using static Content.Client.Stylesheets.StylesheetHelpers;
var regular = ResCache.GetFont("/Fonts/NotoSansDisplay/NotoSansDisplay-Regular.ttf", 26); // or sheet.BaseFont.GetFont(26, FontKind.Bold)

CButton().Class("KsLobbyMenuButton").PseudoHovered()
    .Box(new StyleBoxEmpty())
    .Prop(Control.StylePropertyModulateSelf, Color.White),
CButton().Class("KsLobbyMenuButton").PseudoHovered().ParentOf(E<Label>())
    .Font(regular).FontColor(accent),
E<PanelContainer>().Class("KsLobbyGlass").Panel(new StyleBoxFlat(Color.Black.WithAlpha(0.45f))),
```

- Keep class names in a static constants class (`KsLobbyStyleClasses`) and reference them from C#, fully qualified if
  only used once in an upstream file.
- Available fonts are under `Resources/Fonts/`: NotoSans and NotoSansDisplay (no condensed face), Boxfont Round,
  RobotoMono, `_KS14/BaronUI/FontStack-*`, `_KS14/Tgui/DejaVuSans*`, `_KS14/VT323`, and the lobby's `_KS14/Jura` (in use) and `_KS14/Bender` (kept as
  an alternative). Both are OFL with Cyrillic. Jura's files are static instances cut from the variable font with fontTools
  `varLib.instancer`, since Robust loads a variable font's default instance only.
- **Styleboxes:**
  - `StyleBoxFlat` takes alpha colours and borders.
  - `StyleBoxTexture` takes patch margins and Tile/Stretch.
  - `StyleBoxEmpty` draws nothing.
  - Content margins add to the control's minimum size.

### The tgui theme

To make a window look like tgui, or port a tg interface, see the `tgui` skill.

The UI's default look is a port of /tg/station's tgui (`nanotrasen` theme), and Baron is what the option turns it back to:
`klovn.ui.tgui` (CLIENTONLY, archived, default on, in the KS14 options tab). It switches live. Everything is in
`Content.Client/_KS14/Tgui/`. The source of truth for a value is `tgstation/tgui/node_modules/tgui-core/styles/`.

| Part | What it does |
| --- | --- |
| `KsTguiPalettes` | Swapped in by both stylesheets, so upstream sheetlets come out tgui with no per-class rules |
| `KsTguiFonts` | Verdana by PostScript name through `ISystemFontManager`, else bundled DejaVu Sans. Behind `BaseFont` and `FontTagHijack` |
| `KsTguiSheetlet` | Everything the palette can't reach. Sits in Baron's slot, so it has to cover every selector Baron sets |
| `KsTguiStyleBox` and friends | No engine box has a corner radius, and CSS's stripes and shadows have no equivalent |
| `KsTguiChromeUIController` | Title bar, close button, status icon and drag on each `DefaultWindow`/`FancyWindow`, via `WindowRoot.OnChildAdded`. The status icon (`KsTguiTitleStatusIcon`, tg's `TitleBar__statusIcon`) is a 45px cell before the title, ignoring the mouse so the bar under it drags: an eye that is green (can interact; also a window no bound interface made), orange (a ghost that may look and not use) or a red `eye-slash` (the dimmer shows), eased over 0.5s. `KsTguiWindowDimmerUIController.GetStatus` is the one answer and its existing 4 Hz poll feeds `SetStatus` |
| `KsTguiFeelUIController` | Button fades, input focus, tooltip fade, tagging `OptionButton` entries |
| `KsTguiThemeUIController` | The toggle. Restyles on the next frame, since a cvar's callback runs on the setter's thread |

- **New UI gets the theme by using ordinary classes and palette colours.** A hardcoded `#25252A` stays that colour under
  both themes.
- **A custom style under both themes:** add rules to both sheetlets, or use a property each theme sets, as
  `MenuButton.KsStylePropertyChildColor` and `StripeBack.KsStylePropertyFlat` do. A sheetlet can't set a value a control
  writes in code, so make the control read a style property, with the old value as its fallback.
- **Windows opt out by not being a `DefaultWindow` or `FancyWindow`.** `KsBaseWindow` gets no chrome. The Nuke UI stays
  untouched because everything in it overrides the sheet too.
- **A button only fades if a rule sets `KsTguiSheetlet.StylePropertyNormalColor`** on it, and nothing else writes its
  `ModulateSelfOverride`. The property doesn't vary by state, which is what gives a hover a colour to start from:
  controllers run after style updates, so by then the sheet has discarded the normal one.
- **Code-written chrome values are saved and restored on detach**, since the sheet can't reclaim them. Detach has to run
  for closed windows too, which is why `SyncWindow` also runs when a window reopens.
- **`Color.Transparent` is white**, so a fade or a normal state uses `Color.Black.WithAlpha(0f)`, as `KsTguiSheetlet.Clear`.
- **A CSS alpha is not an engine alpha.** A browser blends in sRGB and this engine in linear light, so a translucent token
  ported as-is comes out wrong, and worst for white on dark: `white .1` over `#262626` is `#3C3C3C` in CSS and about
  `#616161` here. Use `KsTguiPalettes.Translucent(color, cssOpacity, backdrop)`, or a flat colour where one will do, as
  text and disabled states do.
- **Check against pixels, not by eye.** tgui's tokens are HSL and relative CSS, so a screenshot of a live tg window is the
  better reference. Colours here were confirmed by sampling a real client's capture against the user's screenshots.
- **The two stylesheets differ.** `ExecutorStyle` is a global `[CommonSheetlet]` on the Nanotrasen sheet alone, and it clears
  `BackgroundPanel`'s tint there. So a missing `.Modulate(Color.White)` only shows on the System sheet: vote windows, the
  connecting screen. Check both, as `KsTguiThemeTest.SheetletReachesWhatItSaysItDoes` does.
- **A stylebox can get a box of nothing.** A control can draw before it's laid out, and an inverted `UIBox2` is a fatal assert
  in Debug only, so subtracting a title bar or border from a box goes through `KsTguiGeometry.Deflate`. Nothing headless
  draws, so no test reaches a `DoDraw`.
- **Testing hover:** `UserInterfaceManager.FrameUpdate` sets the hover from the real mouse every frame, and a headless
  client has none, so `SetHovered` doesn't survive one. Stay in one `WaitAssertion` and drive
  `KsTguiFeelUIController.UpdateButtons` (`KsTguiFeelTest`), or the test passes without anything having happened.
- **`KsWindowContentsFitTest` runs under both themes**, with a list for each: headers differ, 32 against 71. It measures
  DejaVu, while most players have Verdana.

### Gradients: `KsStyleBoxGradient`

There's no gradient in the engine. The fork's `KsStyleBoxGradient` is a `StyleBox`, so it works in sheetlet rules,
`PanelOverride` and button boxes. It is a multi-stop linear gradient with a `KsGradientDirection`:

```csharp
new KsStyleBoxGradient(KsGradientDirection.LeftToRight,
    (0f, Color.Black.WithAlpha(0.75f)), (0.55f, Color.Transparent), (1f, Color.Transparent));
```

Apply it to a `<PanelContainer StyleClasses="..." MouseFilter="Ignore"/>`. It reuses its vertex array and converts
colours itself (see below), so don't re-solve either.

## Screens, controllers, windows

- **Screens:** `UIScreen : LayoutContainer`. Widgets are keyed by **exact type**: `GetWidget<T>()`, and a duplicate
  throws. So a screen holds one widget per type. `ChatUIController` finds the chat as
  `ActiveScreen.GetWidget<ChatBox>()`, which is why the lobby must hold exactly one `ChatBox`.
- **States:** a state links its screen with `LinkedScreenType`. `LobbyState` casts `ActiveScreen` to `LobbyGui` and
  wires its buttons.
- **`UIController`:**
  - Lives for the process.
  - Takes `[Dependency]` and `[UISystemDependency]` fields.
  - Implements `IOnStateEntered<T>`, `IOnStateExited<T>` and `IOnSystemLoaded<T>`.
  - Its event and cvar subscriptions need no teardown. Anything a short-lived control registers does.
- **Windows:** `BaseWindow` is the base of `DefaultWindow` and `FancyWindow` (content). Open one with `OpenCentered()`
  and listen to `OnClose`.
- **Window height: leave it to the contents.** A window whose bottom is cut off is a very common bug here. See the next
  section before giving any window a `SetSize`.
- **BUIs:** in `Open()`, use `_window = this.CreateWindow<T>()`. It disposes with the BUI, wires `OnClose` to
  `Close`, and restores position. Don't `new` and hand-dispose. Keep the window dumb: state arrives in `UpdateState`.
- **`UiControlTest`** instantiates every content `BaseWindow` with a parameterless constructor and no injection. A
  window constructor must not need state, and its ignore list says not to add to it.

## Sizing a window

**This is a very common issue: a window's bottom row is cut off, and it comes from a hard-coded height.** Writing
`SetSize="340 250" MinSize="340 250"` in XAML means guessing the contents' height, and nothing checks the guess:

- **`SetHeight` beats what the contents ask for** (`Control.MeasureCore`, `Control.Layout.cs:600`). The window doesn't
  grow. A window with no `SetSize` is sized to its contents.
- **`MinSize` is only your guess again.** It knows nothing about the contents, and neither does `FancyWindow`.
- **`FancyWindow`'s `ContentsContainer` has `RectClipContent="True"`.** What doesn't fit is clipped with no error, and a
  squeezed `BoxContainer` looks fine until its last row.
- **`FancyWindow`'s header is 71 px, and only the rest of the height is yours.** The Baron frame added 41 px to it, so
  numbers copied from an upstream window or an older one come up short. Add a row and the guess is wrong again.

Pick by what the window holds:

| Contents | Do |
| --- | --- |
| A form with a fixed set of rows (most BUIs) | `MinWidth="340" Resizable="False"`, no `SetSize`, no height at all. Sizing to the contents can't go wrong in any language. |
| Something that wants the room (a list, a map, a log) | `SetSize` and `MinSize`, with a `ScrollContainer` or an expanding child taking the slack, so a smaller window shrinks that and not the rows. |
| Wrapping text in a resizable window | The minimum has to follow the contents: see below. |

### Wrapping text in a resizable window

`BaseWindow` clamps a drag to `MinSize` and never asks the contents. Wrapping text (`RichTextLabel`, anything
word-wrapped) gets *taller* as the window narrows, so a window can meet its minimum and still push what's below the text
out of the bottom: those controls are laid out at zero height and vanish. The voice link window lost its buttons this
way. Make the minimum follow the contents: the rows that can't wrap set the minimum width, and the minimum height is what
the contents need *at the current width*. Recompute on every resize and whenever a row's text changes:

```csharp
protected override void Resized()
{
    base.Resized();
    UpdateMinimumSize();
}

private void UpdateMinimumSize()
{
    if (RootBox.Size.X <= 0f)
        return; // not laid out yet

    var marginSize = new Vector2(RootBox.Margin.SumHorizontal, RootBox.Margin.SumVertical);
    var frameSize = Size - RootBox.Size - marginSize; // title bar and borders

    var widestRow = 0f;
    foreach (var row in _unwrappableRows) // buttons, single-line labels
    {
        row.Measure(Vector2Helpers.Infinity);
        widestRow = MathF.Max(widestRow, row.DesiredSize.X);
    }

    // DesiredSize includes the margin.
    RootBox.Measure(new Vector2(RootBox.Size.X + marginSize.X, float.PositiveInfinity));

    MinSize = new Vector2(frameSize.X + marginSize.X + widestRow, frameSize.Y + RootBox.DesiredSize.Y);
}
```

A `MinSize` bigger than `SetSize` wins at measure time, so the window grows back by itself; set `SetSize` to match too,
so the next drag starts from the size shown (`KsVoiceLinkWindow`). **Testing it:** a squeezed control is laid out *and
measured* at the squeezed size, so its `Size` and `DesiredSize` agree at zero, and "is it inside the window?" passes too.
Compare each control against what it needs with room to spare: `Measure` it with unlimited height (and width, for
anything that can't wrap), then check its shown size against that (`KsVoiceLinkWindowTests`). Layout runs in the
headless client with real font metrics.

Don't fix a cut-off window by raising the number; that's how the pressure pump went from 110 to 160 and was still 2.6 px
short. `KsWindowContentsFitTest` measures every `FancyWindow` that can be built empty and fails on a new one that doesn't
fit. It can't see rows added by state, so check a window that grows at runtime yourself. Its list of known windows only
ever shrinks.

## Drawing inside a Control

- Override `protected internal override void Draw(DrawingHandleScreen handle)`. The handle works in device pixels.
- **Vertex colours are sRGB, whatever the engine says.** `DrawVertexUV2DColor`'s docs call them linear, but the vertex
  shader converts them (`base-default.vert:55`), so a `Color.FromSrgb` converts twice: `#40668C` came out `#0D2243`, and
  black hides it completely. `handle.Modulate` isn't applied to raw vertices, so pass `color * handle.Modulate`. Nothing
  headless draws, so check a colour you care about against a real client's pixels.
- **No per-frame allocation.** `Draw` runs every frame, so cache arrays and resize them only on length change. No
  LINQ or closures.
- **Sandbox:** no `stackalloc` (CLAUDE.md §6). `SandboxTest` is the only check.
- **UI has no `SCREEN_TEXTURE`**, so a post-process blur of the UI is impossible. Blurring something in UI means a
  shader that samples `TEXTURE` set on the control that draws the texture (`TextureRect.ShaderOverride`, or
  `UseShader` around the draw), as the lobby's `KsLobbyContrast` (`_KS14/Shaders/lobby_contrast.swsl`) does for its
  dimming. There is no blur shader: the lobby's was removed on purpose, so write one only when something needs it.

## Localisation in UI

- Put fork keys under `Resources/Locale/<culture>/_KS14/<feature>/`. Add every culture that has a `_KS14` folder
  (`en-US`, `ru-RU`).
- Uppercase labels are written uppercase in the `.ftl` (`ks-lobby-menu-ready = READY`). Don't `ToUpper()` in C#,
  which is culture-sensitive and leaves the translator no say.
- When code reassigns a control's `Text` at runtime (`LobbyState` swaps Ready, Not Ready and Join), the runtime keys
  need the same treatment as the xaml ones.
- Russian runs longer than English. Leave room in fixed-width menus.

## Testing

- The headless client runs real layout with real font metrics, but nothing draws. Size, visibility and text are
  testable; colour, style and draw output aren't.
- Template: `KsVoiceLinkWindowTests`.
  ```csharp
  [TestOf(typeof(X))]
  public sealed class XTest : GameTest
  {
      public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };
      // await Client.WaitPost(() => { window = new X(); window.OpenCentered(); });
      // await Pair.RunTicksSync(5);   // resizes take a few passes to settle
      // assert inside Client.WaitPost, Close() last
  }
  ```
- Assert against `Measure(Vector2Helpers.Infinity)`, not a squeezed control's own `DesiredSize` (see *Wrapping text
  in a resizable window*), and
  prove the test fails with the fix removed.
- Lobby smoke tests: `--filter "FullyQualifiedName~Lobby|FullyQualifiedName~SandboxTest"`.
- **Drive the real screen when standalone doesn't reproduce.** With `PoolSettings { Connected = true, InLobby = true }`,
  `((LobbyState)Client.Resolve<IStateManager>().CurrentState).Lobby` is the live `LobbyGui`. The server side drives it:
  `Server.System<GameTicker>().StartRound()` / `RestartRound()`, and `toggleready True` through the client's
  `IConsoleHost` (pre-round only, so ready up after a restart, not after a round start). Template:
  `KsLobbyReadyStatusTest`.
- **Probing layout:** record `Size`, `DesiredSize`, `IsMeasureValid` and `IsArrangeValid` up the parent chain once per
  `Pair.RunTicksSync(1)`, logging only lines that change. A control whose `DesiredSize` differs from its `Size` while
  both flags say valid is the stale-arrange trap above.
- **Style results are readable**, even though drawing isn't: `Label.Align` returns the style-resolved alignment
  (`StylePropertyAlignMode`), not just the value set in code.

## The lobby

The lobby is an "Escape from Klovn" main menu, a send-up of a shooter's main menu: wordmark, season banner, a centred
column of large items, a bottom tab bar. All art is our own (`Tools/_KS14/lobby_tarkov_assets.py` regenerates it into
`Resources/Textures/_KS14/Lobby/Tarkov/`). The previous translucent "tactical" layout is kept as `LobbyGui.xaml.tactical`
(+ `LobbyGui.xaml.cs.tactical`), upstream's as `LobbyGui.xaml.old`; none are built.

| Piece | File |
| --- | --- |
| Layout | `Content.Client/Lobby/UI/LobbyGui.xaml` (+ `.xaml.cs`, `.Klovn.Tarkov.cs` for the status/banner/version setters) |
| Styles | `Content.Client/_KS14/Lobby/Tarkov/KsTarkovSheetlet.cs` (`KsTarkovStyleClasses`), registered after `KsLobbySheetlet` |
| Bar tabs | `KsTarkovNavTab`: draws its own box and colours from its draw mode (`Active`, `Dim`, `Slot`), theme-proof |
| Bloom | `KsTarkovBloomHost`, around everything above the art |
| Glare | `KsTarkovLightLeaks` (`lobby_light_leaks.swsl`): flare lines, cold haze over the wordmark, warm bottom corners |
| Backdrop | `LobbyGui.Klovn.Contrast.cs` puts `KsLobbyGrade` (`lobby_grade.swsl`) on `Background` |
| Labels | `Resources/Locale/{en-US,ru-RU}/_KS14/lobby/tarkov.ftl` (+ the older `lobby.ftl` keys still used) |

Layers, back to front: `Background` (or `KsViewportContainer` when a lobby-view eye exists), `KsFadeBottom`,
`KsLightLeaks`, then `KsBloom` holding `KsRoot`: `KsContent` (`KsHiddenLegacy`, `DefaultState`, `CharacterSetupState`)
over `KsBottomBar`.

Wiring:
- **Menu → real actions.** ESCAPE FROM KLOVN is `ReadyButton` (ready toggle, late join after start), CHARACTER and the
  CHARACTER tab raise `LobbyGui.KsCharacterSetupRequested` (handled in `LobbyState`), TRADING is `ObserveButton`,
  HIDEOUT is `ManifestButton`, EXIT is `LeaveButton`, the cog tab is `OptionsButton`. Other tabs are decoration.
- **The ready item keeps its title.** `LobbyState` calls `KsSetReadyStatus` instead of writing `ReadyButton.Text`; the
  `ReadyStatus` line under it says NOT READY / READY / JOIN (`KsLobbyReadyStatusTest`).
- **Banner:** PLAYERS from `IPlayerManager.PlayerCount` (`KsSetPlayerCount`, only touches the label on change), TIMER is
  `StartTimeValue`: the countdown through `KsSetCountdown` (urgency colours kept), then the round's elapsed time.
- **`KsHiddenLegacy` (`Visible="False"`) keeps everything the menu doesn't show:** server name, chat (still exactly one
  `ChatBox`), server info, AHelp, vote button, song/art credits, link banners, `CharacterPreview`, `RightSide`. Their
  controllers keep working; move a control out of it to bring it back. `VoteContainer` stays visible, top right.
- **Badges** above the bar are fixed decoration. Every bar column is `VerticalAlignment="Bottom"`, or a column with a
  badge row pushes its tab down a few pixels.

Effects:
- **Bloom (`KsTarkovBloomHost`):** in `PreRenderChildren` it renders its children into a target, bright-passes and blurs
  them (half and quarter resolution, two levels), adds the result to the screen, then the children draw sharp on top.
  Off in character setup. The UI has no screen texture, so the second render of the children is the price.
- **Additive passes use `blend_mode add;` in the `.swsl` header, output alpha 1.** A prototype's `blend_mode` is ignored
  for source shaders (see the shaders skill); normal blending at alpha 1 covers what is behind, at alpha 0 adds nothing.
- **Backdrop grade** (cvar `klovn.lobby.adaptive_contrast`, the KS14 option "Screen effect on the lobby background"):
  exposure, desaturation, vignette, edge fringe, slight barrel curvature (zoomed to fill, no bezel), a staggered RGB slot mask
  on the curved coordinates, slow brightness roll, faint coloured static. Off while the lobby-view camera shows.
  `quadPixelSize` is set in `LobbyGui.Draw` only after a resize.
- **Lobby art is server-picked** and sent in `TickerLobbyStatusEvent`; `forcedLobbyBackground` on a `gameMap` forces one.
  The client only displays `ClientGameTicker.LobbyBackground`; never pick art client-side.
- Fonts follow `klovn.lobby.font` (Jura by default); `KsTarkovSheetlet` reads it like `KsLobbySheetlet`.
