# The lobby

The lobby screen has two layouts, and the server picks which one every client shows:

| Layout | id | What it is |
| --- | --- | --- |
| Tactical | `tactical` | The upstream lobby reworked into a translucent "tactical menu": menu column, character preview, chat and server info on the right. Most of the year. |
| Kharkiv | `kharkiv` | A gag: an "Escape from Klovn" main menu, after a well-known shooter's. Wordmark, season banner, centred menu, bottom tab bar, glare and bloom. No chat on screen. |

Both live in the one screen, `LobbyGui`, and switch live: no reconnect, no state change, no lost chat.

## Switching layouts

The cvar `klovn.lobby.layout` (`KsCCVars.LobbyLayout`, `SERVER | REPLICATED`, default `auto`) names the layout.
Players can't change it. Admins with the `Fun` flag use the command, which also lists the ids:

```
lobbylayout            # shows the setting, and what it shows today by the server's date
lobbylayout kharkiv    # every client switches now, whatever the date
lobbylayout tactical
lobbylayout auto       # back to the calendar
```

`auto` shows the tactical layout, except on clown holidays (`KsLobbyLayouts.IsKharkivSeason`), when it shows Kharkiv:

| Dates | What |
| --- | --- |
| April 1-3 | April Fools', and two days to recover |
| August 1-7 | International Clown Week |
| November 5 | World Clown Day |
| December 27 - January 1 | the end of the year |

Both ends of every range count. Each client reads its own date (`DateTime.Now`) when the lobby is built and when the cvar
changes, so players in other timezones flip a few hours apart, and a lobby left open over midnight keeps its layout until
the next one. The setting is not archived: a server restart puts it back to `auto` (set it in the server's config
file to keep a forced layout). `cvar klovn.lobby.layout kharkiv` does the same as the command for a host. An unknown id is refused by the
command; a cvar set to one anyway acts as `auto`. The ids and the calendar are in
`Content.Shared/_KS14/Lobby/KsLobbyLayouts.cs`, covered by `Content.Tests/Shared/_KS14/Lobby/KsLobbyLayoutsTest.cs`.

### How the switch works

`LobbyGui.xaml` is the tactical layout. The Kharkiv layout is a separate control, `KsKharkivLobbySkin`
(`Content.Client/_KS14/Lobby/Kharkiv/`), placed last in `LobbyGui.xaml` as `KsKharkiv` and hidden. Switching shows one and
hides the other: the tactical layout is `KsFadeLeft`, `KsFadeBottom` and `KsRoot`, the Kharkiv one is `KsKharkiv`.
`Background` and the lobby-view viewport stay, shared.

Most controls belong to one layout and are simply hidden with it: chat, server info, AHelp and the character preview
exist only in the tactical layout, and keep working while hidden (their controllers never notice). A few controls show
in both. These are **single instances moved between layouts** by `LobbyGui.Klovn.Layout.cs`:

| Control | Tactical home | Kharkiv marker |
| --- | --- | --- |
| `ReadyButton`, `ObserveButton`, `ManifestButton`, `LeaveButton` | `KsMenuColumn` | `ReadySlot`, `ObserveSlot`, `ManifestSlot`, `LeaveSlot` |
| `StartTimeValue` (the countdown) | `KsMenuColumn` | `TimerSlot`, in the banner |
| `VoteContainer` | `KsMenuColumn` | `VoteSlot`, top right |
| `CharacterSetupState` | `LeftSide` | `SetupSlot`, over a dark backdrop |

Moving rather than duplicating is what keeps it seamless: `LobbyState` subscribed to these buttons, the vote manager
holds `VoteContainer`, `LobbyUIController` put the character editor into `CharacterSetupState`. A Robust named control
is found through the name scope it was loaded in, not through its parent, so `LobbyGui.ReadyButton` stays the same
control wherever it sits.

Each Kharkiv `*Slot` is an invisible marker, not a wrapper: the shared control is inserted right after it and takes the
marker's alignment, margin and expand settings, so the tree is exactly what it would be with the control written there.
(A wrapper adds a layout level, and centred text then rounds a pixel differently.) Each shared control records its
tactical parent, index, margin, alignment, expand flags, style classes and text once, at construction. Moving it to
Kharkiv swaps the layout's own style classes and the text; moving back restores every one, at the same index. Only the
layouts' classes are swapped, never cleared: every button also carries `button`, which all button style rules select on. What else differs per layout is decided where it is set:

| What | Tactical | Kharkiv |
| --- | --- | --- |
| Ready state (`KsSetReadyStatus`) | the ready button's own text: NOT READY / READY / JOIN | the button keeps ESCAPE FROM KLOVN; `ReadyStatus` under it says the state |
| Countdown (`KsSetCountdown`) | caption over a big value, both hidden when empty | the banner's TIMER, dashes when empty |
| Round running (`KsSetRoundElapsed`) | countdown hidden, `StationTime` says the round time | TIMER shows the round's length |
| Backdrop shader (`KsApplyContrastShader`) | `KsLobbyContrast`: dims bright art under the text | the skin's `KsLobbyGrade`: the screen effect |
| Character setup (`SwitchState`) | `RightSide` may hide to make room; a switch to tactical during setup judges that again once `LeftSide` has a width (`KsRecheckRightSide`) | `KsKharkiv.ShowSetup`, bloom off |

`LobbyState` calls these and doesn't know which layout shows. The layout cvar is subscribed from `EnteredTree` to
`ExitedTree` (and `Dispose`), next to the backdrop cvar, per CLAUDE.md §6.

**Adding a control that both layouts show:** add it to the tactical XAML where it belongs, put a named, invisible marker
`Control` in the skin where it goes (with the alignment and margin it should have there), and add one `KsShare(...)`
line in `KsInitialiseLayout`, naming each layout's own style classes. Nothing that references the control changes.

**Adding something to one layout only:** put it in that layout's XAML. A tactical-only control needs nothing else; a
Kharkiv-only control the lobby must feed gets a setter on the skin, called from a `LobbyGui` method.

## Files

| Piece | File |
| --- | --- |
| Screen, tactical layout | `Content.Client/Lobby/UI/LobbyGui.xaml` (+ `.xaml.cs`), upstream original in `LobbyGui.xaml.old` |
| Layout switch, shared setters | `LobbyGui.Klovn.Layout.cs` |
| Countdown | `LobbyGui.Klovn.Countdown.cs` |
| Backdrop shaders, both cvar subscriptions | `LobbyGui.Klovn.Contrast.cs` |
| Character block (tactical) | `LobbyCharacterPreviewPanel.xaml`, `.Klovn.Info.cs` (`SetCharacterInfo`, `GetPreferredJobName`), original in `.xaml.old` |
| Tactical styles | `Content.Client/_KS14/Lobby/KsLobbySheetlet.cs`, `KsLobbyStyleClasses` |
| Kharkiv layout | `Content.Client/_KS14/Lobby/Kharkiv/KsKharkivLobbySkin.xaml` (+ `.xaml.cs`) |
| Kharkiv styles | `KsKharkivSheetlet.cs`, `KsKharkivStyleClasses`, registered after `KsLobbySheetlet` in `NanotrasenStylesheet` |
| Kharkiv controls | `KsKharkivNavTab`, `KsKharkivBloomHost`, `KsKharkivLightLeaks` |
| Layout cvar, ids, command | `KsCCVars.Lobby.cs`, `Content.Shared/_KS14/Lobby/KsLobbyLayouts.cs`, `Content.Server/_KS14/Lobby/KsLobbyLayoutCommand.cs` |
| Labels | `Resources/Locale/{en-US,ru-RU}/_KS14/lobby/lobby.ftl` (tactical), `kharkiv.ftl`, `lobby-layout.ftl` (command) |
| Kharkiv art | `Resources/Textures/_KS14/Lobby/Kharkiv/`, all drawn by `Tools/_KS14/lobby_kharkiv_assets.py` |
| Shaders | `Resources/Textures/_KS14/Shaders/lobby_contrast.swsl`, `lobby_grade.swsl`, `lobby_bloom.swsl`, `lobby_bloom_composite.swsl`, `lobby_light_leaks.swsl`; prototypes in `Resources/Prototypes/_KS14/Shaders/lobby_*.yml` |

## Constraints on both layouts

- **Keep the names, and `Access="Public"`**, of everything `LobbyState`, `LobbyUIController`, `AHelpUIController`,
  `KsLobbyViewSystem` and `LobbyGui`'s own partials touch. Grep `Lobby!.` and `Lobby.` before renaming anything.
- **Exactly one `ChatBox`** on the screen. `ChatUIController` finds it with `GetWidget<ChatBox>()`. It lives in the
  tactical layout only, so the Kharkiv layout has none to show.
- **Lobby art is server-picked** and sent to every client in `TickerLobbyStatusEvent`, so everyone sees the same one.
  A `gameMap` can force one with `forcedLobbyBackground`. A `lobbyBackground` with `randomlySelectable: false` appears
  only through such a map (`GameTicker.Klovn.ForcedLobbyBackground.cs`). The client only displays
  `ClientGameTicker.LobbyBackground`; never pick art client-side.
- **The backdrop cvar** `klovn.lobby.adaptive_contrast` (CLIENTONLY, archived, default on; the KS14 option) turns the
  backdrop shader of whichever layout shows on or off. Both are off while the lobby-view camera (`KsViewportContainer`)
  shows the live game.
- **Lobby font is a cvar**, `klovn.lobby.font` (CLIENTONLY, not archived, default `jura`; ids in
  `KsLobbySheetlet.FontFamilies`: jura, bender; unknown falls back to jura). Both sheetlets read it in `GetRules`
  (`IoCManager.Resolve`, sheetlets have no injection). `KsLobbyFontUIController` rebuilds the Nanotrasen sheet on change
  (`StylesheetManager.KsRebuildNanotrasen`) and reassigns `IUserInterfaceManager.Stylesheet`, so it switches live; try
  `cvar klovn.lobby.font bender`. The KS14 options dropdown for it is commented out in `Ks14Tab.xaml(.cs)`. Windows
  holding `SheetSystem` are unaffected.
- **Countdown urgency:** in the last 20s `KsSetCountdown(..., secondsLeft:)` shifts the value from amber to `#FF5A3C`
  with a flash on each digit change, in both layouts. `LobbyState` passes `secondsLeft`, null while paused.

## The tactical layout

Layers, back to front:
1. `Background` (TextureRect), or `KsViewportContainer`/`KsViewport` when a lobby-view eye exists (`KsLobbyViewSystem`
   toggles the two).
2. `KsFadeLeft` and `KsFadeBottom` gradient panels.
3. `KsRoot` (vertical, anchored Wide), holding `MainContainer` and the bottom bar. `MainContainer` holds:
   - `LeftSide`:
     - `DefaultState`: the menu column `KsMenuColumn` (`ServerName`, `StationTime`, `StartTime`, `StartTimeValue`,
       `PlaytimeComment`, Ready / Observe / Manifest / Options / Leave, `VoteContainer`) and `CharacterPreview`.
     - `CharacterSetupState`.
   - `RightSide`: AHelp, vote, `ServerInfo`, `Chat`, `CollapseButton`.
   - `ExpandPanel`.

Constraints:
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

Text and fonts:
- Regular for menu items, chips and subtitles, Bold for titles and the character name, Black 44 for the countdown
  (families without a Black use Bold).
- **Baron forces a font on every `RichTextLabel`.** Give lobby ones `StyleClasses="KsLobbyRichText"` (the lobby font at
  13, dim modulate). The sheetlet also has a direct-child rule, so a class on a parent (`ServerInfo`, whose label is
  built in code) works. Don't class the Chat's own text.
- **Chips are uppercase through `ks-lobby-chip-*` keys** (`lobby.ftl`), swapped into `LinkBanner`, `DevInfoBanner`,
  `CharacterSetupGui.xaml`, `LobbyGui.xaml.cs` (Vote), and `ChangelogButton` (opt-in `KsUpperCase`, because the main
  menu and escape menu share the class).
- **Countdown is two labels**: `StartTime` (small caption, `KsLobbySubtitle`) over `StartTimeValue` (big amber).
  Round started or ready-state refresh hides both; paused shows the caption over `PAUSED`; "starting soon" and "right
  now" show the value alone.
- Disabled menu buttons (Observe before the round) draw at about 28% alpha.
- `CharacterPreview` is right-aligned (`Margin="0 0 40 0"`) and vertically centred between two `VerticalExpand`
  spacers, in the free space right of the menu column, so it follows `RightSide` collapsing.
- **Menu item labels are left-aligned** (`AddButtonRules`, non-chip), overriding `ButtonSheetlet`'s centring. This
  keeps the ready button's text still across NOT READY / READY / JOIN: a button's width can lag a text change (the ui
  skill's stale-arrange trap), and `KsLobbyReadyButtonAlignmentTest` checks the text stays put.

Bottom bar:
- `KsBottomBar`: a full-width hairline, then a `KsLobbyBottomBar` panel (black, alpha 0.85) around the song/background
  labels and the `LinkBanner`/`DevInfoBanner`. It stays visible in character setup. `KsFadeBottom` softens the
  transition above it.
- `ChangelogButton` adds `StyleClass.Positive` for unread entries. `KsLobbySheetlet.AddPositiveRules` (direct and
  parent-child forms, like Negative) gives a green label and a faint green box.

Backdrop dimming (`KsLobbyContrast`, `lobby_contrast.swsl`): one `InstanceUnique()` made in the constructor, on
`Background.ShaderOverride` and `KsViewport.KsDrawShader` while the backdrop cvar is on.
- It caps the linear luminance (`maxLuminance` 0.07, about 7:1 against #E6E6E6) of bright pixels under up to four
  padded regions (`KsMenuColumn`, `CharacterPreview`, `RightSide`, `CharacterSetupState`, only while visible in the
  tree), through a soft knee starting at 60% of it, never brightening, and desaturates them 30%. Dark pixels are
  untouched. It doesn't blur: the backdrop stays sharp, character setup included.
- While it is active `RightSide.PanelOverride` is a `StyleBoxEmpty`, so the right fade doesn't stack into a stripe.
- Each region is fully dimmed inside and fades out over `feather` **screen pixels outside** its edge (64 × UI scale),
  with a rounded box distance scaled by `quadPixelSize`, so corners fade radially and the aspect ratio doesn't stretch it.
- Regions are in the shader's **UV2** (the quad: the whole `Background`, or the viewport's `KsGetDrawBox()`), not the
  texture's UV, so no cover-aspect math and atlasing doesn't matter. Top-left origin; the shader flips v. The pure
  mapping is `KsLobbyContrastMath.RegionToQuadSpace`.
- Rects are recomputed in `LobbyGui.Draw` only while `_contrastDirty`, set by `OnResized` of the gui, background,
  viewport and region controls, `OnVisibilityChanged` of `DefaultState`/`CharacterSetupState`/`RightSide`,
  `SwitchState`, `KsSetCameraView` and a layout switch. A pure position change with no resize would be missed.

## The Kharkiv layout

A send-up of a shooter's main menu, with every piece of art our own: the wordmark, the HONK PROTOCOL lockup and its
pixel skyline, the emblems, the icons and the banner panel come from `Tools/_KS14/lobby_kharkiv_assets.py` (rerun it to
regenerate; its attribution is CC0, own work). The font is the lobby font, like the tactical layout's.

Layers inside `KsKharkiv`, back to front: `KsFadeBottom`, `KsLightLeaks`, then `KsBloom` holding `KsRoot`: `KsContent`
(`MainMenu`, and `SetupView` with `SetupSlot`) over `KsBottomBar`.

Menu, each item a real action. Every request for character setup, from either layout, goes through
`LobbyGui.KsRequestCharacterSetup`, which ignores it while setup is open: the CHARACTER tab stays on screen during setup,
and a second request would reload the editor from the saved profile (dropping unsaved edits) and un-ready the player.

| Shows | Is | Does |
| --- | --- | --- |
| ESCAPE FROM KLOVN | `ReadyButton` | ready toggle; late join once the round runs |
| CHARACTER (menu and tab) | the skin's `CharacterButton`, `CharacterTab` | `LobbyGui.KsCharacterSetupRequested`, handled by `LobbyState` |
| TRADING | `ObserveButton` | observe |
| HIDEOUT | `ManifestButton` | the ready manifest |
| EXIT | `LeaveButton` | disconnect |
| the cog tab | the skin's `OptionsTab` | the options window |

The other tabs, the badges, the squad slots and the EXPANSIONS tile are decoration. Every bar column is
`VerticalAlignment="Bottom"`, or a column with a badge row pushes its tab down a few pixels. Tabs (`KsKharkivNavTab`)
set their own box and colours from their draw mode (`Active`, `Dim`, `Slot`, `Opaque`), so no theme can restyle them;
the bar and ordinary tabs are a little see-through, MAIN MENU, HIDEOUT and the slots solid.

Banner: a fixed 820 × 120 box drawn from `banner_panel.png` as a 9-patch that expands 14 px past the box for its halo
(`KsKharkivSheetlet.BannerBox`; the pad matches `BANNER_PAD` in the generator). PLAYERS is `IPlayerManager.PlayerCount`
(the skin only touches the label when it changes), TIMER is `StartTimeValue`.

Effects:
- **Bloom (`KsKharkivBloomHost`):** in `PreRenderChildren` it renders its children into a target, bright-passes and
  blurs them (half and quarter resolution, two levels), adds the result to the screen, then the children draw sharp on
  top. Off in character setup. The UI has no screen texture, so the second render of the children is the price. The
  scene target holds premultiplied colour (cleared transparent, drawn with the default blend), so the bright pass doesn't
  scale by alpha again. The passes are delegates made once and the uniforms are set only when an input changes, so a
  frame allocates nothing; the targets are freed while the layout is hidden and made again on its next frame.
- **Glare (`KsKharkivLightLeaks`, `lobby_light_leaks.swsl`):** diagonal flare lines with rainbow edges, a cold haze
  over the wordmark, warm glows behind EXPANSIONS and PVE ZONE. Procedural, drifting with `TIME`.
- **Backdrop grade (`KsLobbyGrade`, `lobby_grade.swsl`, owned by the skin):** exposure, desaturation, vignette, edge
  fringe, a slight barrel curve (zoomed so the art always fills the screen), a staggered RGB slot mask on the curved
  coordinates, a very slow brightness band rolling down (`rollSpeed` 0.06 rad/s, about 100 s a pass), faint coloured
  static. `quadPixelSize` follows the backdrop's size.
- **Additive passes use `blend_mode add;` in the `.swsl` header and output alpha 1.** A prototype's `blend_mode` is
  ignored for source shaders (see the shaders skill): normal blending at alpha 1 covers what is behind, at alpha 0 adds
  nothing.

## Tests

`Content.IntegrationTests/Tests/_KS14/Lobby/`:
- `KsLobbyLayoutSwitchTest`: `lobbylayout` switches a connected client both ways, live; every shared control moves and
  comes back to its exact place, with its text and style, and the countdown keeps running; one `ChatBox` throughout. Also:
  a second character setup request while setup is open is ignored; a switch to tactical during setup judges the chat
  column as if tactical all along; a lobby out of the tree stops following the cvar. (An unknown id isn't tested: the
  command refuses it with an error, and the pool fails any test whose console logs one.)
- `KsLobbyReadyButtonAlignmentTest` (tactical): the ready text stays put across NOT READY, JOIN and READY.
- Every layout test forces its layout with `OverrideCVar(Side.Server, KsCCVars.LobbyLayout, ...)`: the default, `auto`,
  depends on the date the tests run.
- `KsLobbyReadyStatusTest` (Kharkiv): the status line follows the round and stays centred under the title.
- `KsLobbyContrastCVarTests`: the backdrop cvar puts each layout's own shader on the art and takes it off.
- `KsLobbyContrastMathTests`, `KsLobbyCharacterPreviewPanelTests`.

Smoke run: `--filter "FullyQualifiedName~Lobby|FullyQualifiedName~SandboxTest"`. Nothing headless draws: the shaders,
the bloom and how either layout looks are only seen in a real client (the `game-screenshots` skill).
