---
name: ui
description: How client UI is built in this repo - XAML and its code-behind, layout and mouse filtering, the sheetlet stylesheet system (specificity, the fork's Baron sheetlet overriding global defaults, dead StyleNano classes, flat/translucent styles, the reusable gradient stylebox), UI screens and controllers, windows and BUIs, drawing inside a Control, UI localisation, and testing UI headlessly. Also maps the reworked lobby. Use when writing or restyling any XAML, Control, window, BUI, sheetlet or lobby UI, or when a style silently doesn't apply, a panel is opaque when it shouldn't be, clicks don't reach a button, or a control vanishes.
---

# Client UI

Facts read from `RobustToolbox` v291 (`RT` = `RobustToolbox/Robust.Client/UserInterface`) and content. Nothing
headless draws, so styling and visuals are only verified once someone has looked in game. Say so when reporting.

Already in `CLAUDE.md`, not repeated here: *A window's `MinSize` does not protect contents that wrap* (§6), *CVars:
unsubscribe from anything that does not live as long as the process* (§6, applies to every `Control`), and client
localisation (§7).

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
| Fork global overrides | `Content.Client/_KS14/BaronUI/KsBaronUiSheetlet.cs` |
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
  trap in CLAUDE.md.
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
- **Baron overrides global defaults.** `KsBaronUiSheetlet` restyles every `Label`/`RichTextLabel` font, unclassed
  `PanelContainer` (opaque `panel_dark_1_clean` texture), `ContainerButton` (`button_3` texture), LineEdit,
  scrollbars, tabs, sliders and tree rows.
  - **A bare `PanelContainer` is opaque in this fork.** For a transparent one, give it a class whose rule sets
    `.Panel(new StyleBoxEmpty())` (or the box you want), or set `PanelOverride`.
  - Baron's fonts lack some glyphs; its `DefaultFont` class falls back to the base font.
- **ModulateSelf tints flat buttons.** `ButtonSheetlet.MakeButtonRules` sets `ModulateSelf` to a palette colour on
  every `CButton()` pseudo-state. That tint also lands on a `StyleBoxEmpty`, gradient or flat box, and on the label.
  Every state of a flat style must also set
  `.Prop(Control.StylePropertyModulateSelf, Color.White)` (`KsLobbySheetlet` does).
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
  RobotoMono, `_KS14/BaronUI/FontStack-*`, `_KS14/VT323`, and the lobby's `_KS14/Jura` (in use) and `_KS14/Bender` (kept as
  an alternative). Both are OFL with Cyrillic. Jura's files are static instances cut from the variable font with fontTools
  `varLib.instancer`, since Robust loads a variable font's default instance only.
- **Styleboxes:**
  - `StyleBoxFlat` takes alpha colours and borders.
  - `StyleBoxTexture` takes patch margins and Tile/Stretch.
  - `StyleBoxEmpty` draws nothing.
  - Content margins add to the control's minimum size.

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
- **BUIs:** in `Open()`, use `_window = this.CreateWindow<T>()`. It disposes with the BUI, wires `OnClose` to
  `Close`, and restores position. Don't `new` and hand-dispose. Keep the window dumb: state arrives in `UpdateState`.
- **`UiControlTest`** instantiates every content `BaseWindow` with a parameterless constructor and no injection. A
  window constructor must not need state, and its ignore list says not to add to it.

## Drawing inside a Control

- Override `protected internal override void Draw(DrawingHandleScreen handle)`. The handle works in device pixels.
- **Vertex colours are linear.** The convenience overloads convert sRGB for you. `DrawVertexUV2DColor` doesn't, and
  `handle.Modulate` isn't applied to raw vertices, so pass `Color.FromSrgb(color * handle.Modulate)`.
- **No per-frame allocation.** `Draw` runs every frame, so cache arrays and resize them only on length change. No
  LINQ or closures.
- **Sandbox:** no `stackalloc` (CLAUDE.md §6). `SandboxTest` is the only check.
- **UI has no `SCREEN_TEXTURE`**, so a post-process blur of the UI is impossible. Blurring something in UI means a
  shader that samples `TEXTURE` set on the control that draws the texture (`TextureRect.ShaderOverride`, or
  `UseShader` around the draw). `KsTextureBlur` (`_KS14/Shaders/blur.swsl`, `radius` uniform in texels) is that shader.

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
- Assert against `Measure(Vector2Helpers.Infinity)`, not a squeezed control's own `DesiredSize` (CLAUDE.md §6), and
  prove the test fails with the fix removed.
- Lobby smoke tests: `--filter "FullyQualifiedName~Lobby|FullyQualifiedName~SandboxTest"`.
- **Drive the real screen when standalone doesn't reproduce.** With `PoolSettings { Connected = true, InLobby = true }`,
  `((LobbyState)Client.Resolve<IStateManager>().CurrentState).Lobby` is the live `LobbyGui`. The server side drives it:
  `Server.System<GameTicker>().StartRound()` / `RestartRound()`, and `toggleready True` through the client's
  `IConsoleHost` (pre-round only, so ready up after a restart, not after a round start). Template:
  `KsLobbyReadyButtonAlignmentTest`.
- **Probing layout:** record `Size`, `DesiredSize`, `IsMeasureValid` and `IsArrangeValid` up the parent chain once per
  `Pair.RunTicksSync(1)`, logging only lines that change. A control whose `DesiredSize` differs from its `Size` while
  both flags say valid is the stale-arrange trap above.
- **Style results are readable**, even though drawing isn't: `Label.Align` returns the style-resolved alignment
  (`StylePropertyAlignMode`), not just the value set in code.

## The lobby

The upstream lobby was reworked into a translucent "tactical menu" layout. The upstream originals are kept as
`LobbyGui.xaml.old` and `LobbyCharacterPreviewPanel.xaml.old`. Diff against them on upstream merges.

| Piece | File |
| --- | --- |
| Layout | `Content.Client/Lobby/UI/LobbyGui.xaml` (+ `.xaml.cs`: fade anchoring, lobby chat override) |
| Character block | `LobbyCharacterPreviewPanel.xaml`, `.Klovn.Info.cs` (`SetCharacterInfo`, `GetPreferredJobName`) |
| Styles | `Content.Client/_KS14/Lobby/KsLobbySheetlet.cs`, `KsLobbyStyleClasses` |
| Labels | `Resources/Locale/{en-US,ru-RU}/_KS14/lobby/lobby.ftl` |

Layers, back to front:
1. `Background` (TextureRect), or `KsViewportContainer`/`KsViewport` when a lobby-view eye exists (`KsLobbyViewSystem`
   toggles the two).
2. `KsFadeLeft` and `KsFadeBottom` gradient panels.
3. `MainContainer`, which holds:
   - `LeftSide`:
     - `DefaultState`: the menu column (`ServerName`, `StationTime`, `StartTime`, `PlaytimeComment`, Ready / Observe /
       Manifest / Options / Leave, `VoteContainer`), `CharacterPreview`, and a bottom strip (song, background,
       `LinkBanner`, `DevInfoBanner`).
     - `CharacterSetupState`.
   - `RightSide`: AHelp, vote, `ServerInfo`, `Chat`, `CollapseButton`.
4. `ExpandPanel`.

Constraints:
- **Keep the names, and `Access="Public"`**, of everything `LobbyState`, `LobbyUIController`, `AHelpUIController`,
  `KsLobbyViewSystem` and `LobbyGui.xaml.cs` touch. Grep `Lobby!.` and `Lobby.` before renaming anything.
- **Exactly one `ChatBox`.**
- **`VoteContainer` outside `RightSide`.** `RightSide` collapses (`TogglePanel`) and hides in character setup
  (`SwitchState`).
- **`RightSide` width comes from a cvar** (`LobbyState`).
- **Lobby chat translucency** is set only in `LobbyGui.xaml.cs`. `ChatUIController.SetChatWindowOpacity` skips the
  lobby on purpose.
- **Small buttons are chips.** `KsLobbyMenuButtonSmall` draws a faint white fill and outline so neighbours read as
  separate. A `BoxContainer` carrying that class gets the 6px row gap from the sheetlet. Any other row of them sets
  `SeparationOverride` (`KsLobbySheetlet.SmallButtonSeparation`) and `VerticalAlignment="Center"` on its buttons, or
  in a row with a `MinHeight` the chips stretch to full height.
- **AHelp turns red** through `StyleClass.Negative`, added by `AHelpUIController`. The sheetlet has a
  `KsLobbyMenuButtonSmall` + `Negative` rule for it.

Fonts, text and countdown:
- **Lobby font is a cvar**, `klovn.lobby.font` (CLIENTONLY, not archived, default `jura`; ids in
  `KsLobbySheetlet.FontFamilies`: jura, bender; unknown falls back to jura). The sheetlet reads it in `GetRules`
  (`IoCManager.Resolve`, sheetlets have no injection). `KsLobbyFontUIController` rebuilds the Nanotrasen sheet on change
  (`StylesheetManager.KsRebuildNanotrasen`) and reassigns `IUserInterfaceManager.Stylesheet`, so it switches live; try
  `cvar klovn.lobby.font bender`. The KS14 options dropdown for it is commented out in `Ks14Tab.xaml(.cs)`. Windows holding `SheetSystem` are unaffected.
  Regular for menu items, chips and subtitles, Bold for titles and the character name, Black 44 for the countdown
  (families without a Black use Bold).
- **Baron forces a font on every `RichTextLabel`.** Give lobby ones `StyleClasses="KsLobbyRichText"` (Bender 13, dim
  modulate). The sheetlet also has a direct-child rule, so a class on a parent (`ServerInfo`, whose label is built in
  code) works. Don't class the Chat's own text.
- **Chips are uppercase through `ks-lobby-chip-*` keys** (`lobby.ftl`), swapped into `LinkBanner`, `DevInfoBanner`,
  `CharacterSetupGui.xaml`, `LobbyGui.xaml.cs` (Vote), and `ChangelogButton` (opt-in `KsUpperCase`, because the main
  menu and escape menu share the class).
- **Countdown is two labels**: `StartTime` (small caption, `KsLobbySubtitle`) over `StartTimeValue` (big amber),
  driven only by `LobbyGui.KsSetCountdown(caption, value)`. Round started or ready-state refresh hides both; paused
  shows the caption over `PAUSED`; "starting soon" and "right now" show the value alone.
- Disabled menu buttons (Observe before the round) draw at about 28% alpha.
- **Dimming is frosted glass**: inside the mask the art is blurred (6 texels, only where the mask is above zero),
  luminance is eased down to `maxLuminance` 0.07 through a soft knee (starting at 60% of it, never brightening) and
  desaturated 30%. While it is active `RightSide.PanelOverride` is a `StyleBoxEmpty`, so the right fade doesn't
  stack into a stripe; it returns to null otherwise (`KsApplyContrastShader`).
- `CharacterPreview` is right-aligned (`Margin="0 0 40 0"`) and vertically centred between two `VerticalExpand`
  spacers, in the free space right of the menu column, so it follows `RightSide` collapsing.
- **Menu item labels are left-aligned** (`AddButtonRules`, non-chip), overriding `ButtonSheetlet`'s centring. This
  keeps the ready button's text still across NOT READY / READY / JOIN (see the stale-arrange trap in Layout).
- **Countdown urgency:** in the last 20s `LobbyGui.KsSetCountdown(..., secondsLeft:)` shifts the value from amber to
  `#FF5A3C` with a flash on each digit change (`LobbyGui.Klovn.Countdown.cs`). `LobbyState` passes `secondsLeft`,
  null while paused.
- **Lobby art is server-picked** and sent to every client in `TickerLobbyStatusEvent`, so everyone sees the same one.
  A `gameMap` can force one with `forcedLobbyBackground`. A `lobbyBackground` with `randomlySelectable: false` appears
  only through such a map (`GameTicker.Klovn.ForcedLobbyBackground.cs`). The client only displays
  `ClientGameTicker.LobbyBackground`; never pick art client-side.

Bottom bar and blur:
- `LobbyGui` is `KsRoot` (vertical, anchored Wide) holding `MainContainer` (expand) and `KsBottomBar`: a full-width
  hairline, then a `KsLobbyBottomBar` panel (black, alpha 0.85) around the song/background labels and the
  `LinkBanner`/`DevInfoBanner`. It stays visible in character setup. `KsFadeBottom` is kept: it softens the
  transition above the bar.
- `ChangelogButton` adds `StyleClass.Positive` for unread entries. `KsLobbySheetlet.AddPositiveRules` (direct and
  parent-child forms, like Negative) gives a green label and a faint green box.
- **Backdrop shader** (`LobbyGui.Klovn.Contrast.cs`, `KsLobbyContrast` = `_KS14/Shaders/lobby_contrast.swsl`): one
  `InstanceUnique()` created in the constructor, on `Background.ShaderOverride` and `KsViewport.KsDrawShader` whenever
  the cvar `klovn.lobby.adaptive_contrast` (CLIENTONLY, default on) or the character-setup blur is active, else null.
  It caps the linear luminance (`maxLuminance` 0.20, about 3.4:1 against #E6E6E6) of bright pixels under up to four
  padded regions (`KsMenuColumn`, `CharacterPreview`, `RightSide`, `CharacterSetupState`, only while visible in the
  tree); dark pixels are untouched. `blurRadius` 14 in `SwitchState(CharacterSetup)` makes it the blur too
  (`strength` 0 when the cvar is off). `KsTextureBlur` is now unused by the lobby.
  - Dimming applies to the lobby art only: `strength` is 0 while the lobby-view camera (`KsViewportContainer`) is
    shown, since it shows the live game. Only the blur still reaches the camera.
  - Each region is fully dimmed inside and fades out over `feather` **screen pixels outside** its edge (64 × UI scale).
    It uses a rounded box distance scaled by `quadPixelSize`, so corners fade radially and the feather isn't
    stretched by the aspect ratio.
  - Lobby art changes only between rounds. Nothing about it is computed per frame; the regions are just uniforms.
  - Regions are in the shader's **UV2** (the quad: the whole `Background`, or the viewport's `KsGetDrawBox()`), not
    the texture's UV, so no cover-aspect math and atlasing doesn't matter. Top-left origin; the shader flips v.
    The pure mapping is `KsLobbyContrastMath.RegionToQuadSpace`.
  - Rects are recomputed in `LobbyGui.Draw` only while `_contrastDirty`, set by `OnResized` of the gui, background,
    viewport and region controls, `OnVisibilityChanged` of `DefaultState`/`CharacterSetupState`/`RightSide`,
    `SwitchState` and `KsSetCameraView`. A pure position change with no resize would be missed.
  - The cvar handler is held from `EnteredTree` to `ExitedTree` (and `Dispose`), per CLAUDE.md §6.
  - Headless tests check the cvar and the math (`Tests/_KS14/Lobby`); the GLSL is only seen on a GPU client.
