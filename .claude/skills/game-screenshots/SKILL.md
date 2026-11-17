---
name: game-screenshots
description: How to launch a private client and server beside the user's own and take real, 1:1 PNG screenshots of in-game UI with real state - ports, flags, output folder, getting in game, spawning an entity and opening its real BUI, driving a script from a UIController FrameUpdate, saving via IClyde.Screenshot, reading the client log, cleanup, and every trap hit. Use when asked to see, show, screenshot or visually check a window, HUD or UI in a running client, or when a headless test cannot judge how something looks.
---

# Screenshots of the real game

Tested recipe (tgui port, 28 windows in one run). The harness is two non-compiled files next to this one:
`TempKsHarness.cs.txt` (server commands) and `TempKsController.cs.txt` (client script). Copy them in, shoot, delete them.

## 0. Rules

- The user's own client and server may be running from `bin/` on port 1212. **Never kill or touch them.** Only stop
  processes whose `Path` is under `bin\kstest\` (list `Get-Process Content.Server,Content.Client | Select Id,Path` first).
- Never run `dotnet build` yourself: use `python <scratch>/ks.py build client` and `... build server` (a machine-wide
  lock queues you, so use `run_in_background` or accept a wait). Both write to `bin/kstest/`.
- Launch with bash `nohup ... &`. A PowerShell `Start-Process` child died when the tool call ended.

## 1. Add the harness (temporary)

```sh
mkdir -p Content.Server/_KS14/TempKs Content.Client/_KS14/TempKs
cp .claude/skills/game-screenshots/TempKsHarness.cs.txt    Content.Server/_KS14/TempKs/TempKsHarness.cs
cp .claude/skills/game-screenshots/TempKsController.cs.txt Content.Client/_KS14/TempKs/TempKsController.cs
```

Plain files, no `#if`, auto-discovered. The server gets commands `tempks`, `tempksopen`, `tempksclear` (all `[AnyCommand]`).
The client gets a `UIController` that runs a step list once gameplay has been up 10 s.

### Adapting it to a new target

- Client `Build()` is the script. `Scenario(name, "tempks <ProtoId> [flags]")` = spawn, wait 1.5 s, open the UI, wait,
  shoot, close all windows, delete the spawns. Add one line per target. Set `Only = "canister"` to shoot just the
  scenarios whose name contains it (empty = everything).
- `tempks` flags: `beaker` (large beaker with Water/Ethanol/Iron in `beakerSlot`), `lathe` (steel/glass/plastic),
  `hand` (pick the item up), `panel` (open the wiring panel), `wires` (open the wires UI key), `k1` (use the 2nd UI
  key; the default is the first key of the prototype's UserInterface). For other state (power, gas, reagents) edit the
  server file: it is ordinary server code with an `IEntityManager`.
- `store` (balance 100 TC / 10 WizCoin, lifts owner/faction limits) and `usehand` (open through the real use-in-hand: `hand usehand`). **Stores must use `hand store usehand`**: a bare `OpenUi` skips `BeforeActivatableUIOpenEvent`, so listings stay empty. Tested: `tempks BaseUplinkRadio hand store usehand`, `tempks WizardsGrimoire hand store usehand`.
- Client-only windows (late join, guidebook, chat): an `Act(...)` step that opens it from code, then `Shot`. Private
  members are reached by reflection (fine in a throwaway file; a real commit would trip the sandbox).
- Cvar toggles (`ui.layout` = `Default` or `Separated`, `klovn.ui.tgui`): `_configurationManager.SetCVar` in an `Act`.
- Chat lines: `Server("say ...")`, `me`, `ooc`, `whisper` run as the player.
- The last step calls `IGameController.Shutdown`, so the client exits by itself.
- The `if (Only == "")` blocks cause unreachable-code warnings in Debug; harmless (Debug is not warnings-as-errors).

## 2. Build

```sh
python <scratch>/ks.py build server
python <scratch>/ks.py build client      # read the tail: "0 distinct errors; BUILD OK"
```

**Trap: both builds share `bin/kstest`, and the client build downgrades `Microsoft.*` / `System.*` dlls.** The server then
dies on start with `Could not load file or assembly 'Microsoft.Extensions.Primitives, Version=10...'`. After the last
build, copy every `Microsoft.*`/`System.*` dll from `bin/Content.Server/` over its `bin/kstest/` twin when the file
version is newer (version-compare loop in PowerShell; only reads the user's folder). Redo after every build.

**Trap: a running server or client in `bin/kstest` locks its dlls**, so the next build fails with `MSB3027 Could not copy ...`. Stop your own processes (section 7) before rebuilding. Another agent's half-edited UI file can also break the client build; wait and retry.

**Trap: build the client first, then the server.** The dll downgrade happens only when the client build runs last; client
then server leaves the newer dlls in `bin/kstest` with no copying (hand-overwriting dlls may be refused by the permission classifier).

**Trap: the game in `bin/kstest` does not run the content built into `bin/kstest`.** The client and server mount their
content assemblies from `bin/Content.Client/` and `bin/Content.Server/` (two levels up from wherever they're started,
then `bin/<ContentBuildDirectory>/`: `ProgramShared.DoMounts`). So a fresh build of your change in `bin/kstest` is
silently ignored, and the game runs whatever the user last built, with none of your code. To run yours, copy the
`Content.*.dll` and `.pdb` files from `bin/kstest/` over their twins there: `Content.Client`, `Content.Shared`,
`Content.Klovn.Concentus` and `Content.Shared.Database` for the client; `Content.Server`, `Content.Server.Database` and
the same shared ones for the server. **That overwrites the user's own build.** One user was fine with it; others may
well not be, so ask before doing it, and say that it has happened when you report. If the client dies at start-up with a
`NullReferenceException` in `AssemblyTypeChecker.DoVerifyIL`, the sandbox's IL verifier found a problem in those mounted
assemblies and crashed formatting it: run `SandboxTest` against your build for the readable message.

**Trap: teleporting the player onto a wall-mounted machine (to use the map's real one) puts them in vacuum.** They crit,
and every later window silently fails to open. Offset by the machine's rotation (`LocalRotation.RotateVec((0,-1))`).

**Trap: queue a lathe after adding its materials**, or `TryAddToQueue` returns false and the queue shot is empty.

**In a git worktree** (an agent's isolated checkout), set `KS_REPO` to its path and use `ks.py build game` (client, then
server). `ks.py` handles what a fresh worktree lacks: it builds `Robust.Client.Injectors` where the XAML task looks for it,
and keeps `bin/Content.Client` and `bin/Content.Server` as real folders holding only the content assemblies (`ks.py stage`
redoes this by hand). Never make those folders junctions to `bin/kstest`: the game then loads the test assemblies too and
dies registering `MarkingManager` twice. Launch from the worktree's own `bin/kstest` on a port other than 1313 (1414) if
another run may be going. Content loaded from those folders is **sandbox-checked**, unlike in the main checkout, so the
harness may not use reflection there: delete `FocusChat`/`SetSettings` from the installed `TempKsController.cs` (the
client otherwise dies with `Assembly Content.Client failed type checks`), and open private UI through a public path
(`UIManager.CreateWindow<T>().OpenCentered()`).

## 3. Launch (separate port, separate data)

From `bin/kstest`, in bash (`S` = your scratch folder):
```sh
nohup ./Content.Server.exe --config-file "$S/none.toml" --data-dir "$S/serverdata" --cvar net.port=1313 \
  --cvar game.lobbyenabled=false --cvar game.map=Dev --cvar auth.mode=0 --cvar status.enabled=false > "$S/server.log" 2>&1 &
sleep 45     # wait for "Server Version ... -> Ready" in server.log
nohup ./Content.Client.exe --self-contained --username tempks --connect --connect-address localhost:1313 \
  --cvar display.width=1280 --cvar display.height=800 --cvar display.uiScale=1 --cvar auth.mode=0 > "$S/client.log" 2>&1 &
```
- `game.lobbyenabled=false` + `game.map=Dev` (`Maps/Test/_KS14/klovndev.yml`) spawns you straight in as Captain among
  many machines, no lobby, no `joingame`. Server ready in about 30 s, client in game in about 60 s.
- `--self-contained` puts client user data in `bin/kstest/user_data/` instead of the user's `%APPDATA%`, so cvar changes
  (`ui.layout`...) and screenshots never touch their config. `--data-dir` is server-only; the client ignores it.
- `--connect` is needed as well as `--connect-address`, or the client sits at the main menu.
- `auth.mode=0` on localhost needs no login and the connection is made admin (log shows `Admin login: localhost@tempks`). The harness commands are `[AnyCommand]`, so no admin setup is needed.
  Console commands run from the client (`IConsoleHost.ExecuteCommand`) are forwarded to the server when not local.
- The 28-shot run took about 3.5 minutes after launch.

## 4. How the script is driven

- **`Timer.Spawn` does not run at the main menu.** The script is a `UIController` whose `FrameUpdate` runs every frame,
  waits for `IStateManager.CurrentState is GameplayState` plus 10 s, then pops one step each time its wait has elapsed.
- **Wait for layout.** A BUI needs a server round trip and a frame or two to open and fill. 2.5-3.5 s after `tempksopen`
  worked (crew/power monitor 3.5 s). Shorter waits shoot half-built windows. `Shot` adds 1 s before the next step.
- **Do not spawn and open in the same tick.** The client queues the BUI open twice and
  `UserInterfaceSystem.RegisterControl` throws `DebugAssertException` as an unhandled exception that kills the client
  (seen on vending machine and RCD). Hence separate `tempks` and `tempksopen` steps with 1.5 s between.
- **Spawn within 1 tile.** At 2 tiles the server throws `DebugAssertException` in
  `SharedInteractionSystem.OnBoundInterfaceInteractAttempt` (UI range check) while opening.
- **Prototype ids must be concrete.** `GasCanister` is abstract: `Attempted to spawn an entity with an invalid prototype`
  (use `AirCanister`). The failed spawn leaves the previous entity pending, so the shot shows the wrong window.
- `UserInterfaceComponent.Interfaces` is internal; the server file reads it by reflection. All keys print as `Key`.
- Close between shots (`CloseAll` closes every `BaseWindow` under `UIManager.WindowRoot`; `tempksclear` deletes the
  spawns) so each PNG has one window.

## 5. Capture and where it lands

`IClyde.Screenshot(ScreenshotType.Final, image => ...)`, then SixLabors `image.SaveAsPng` into
`IResourceManager.UserData` at `/TguiShots/<name>.png`. With `--self-contained` that is
**`bin/kstest/user_data/TguiShots/`**. Copy the PNGs to your scratch `shots/` folder and view them with the Read tool.
`Final` is the finished window framebuffer: what the player sees, 1:1 (1280x800 with the flags above, no scaling).

Caveats when comparing with other shots:
- `display.uiScale=1` pins UI scale. The default (0) follows Windows DPI, so a shot on a scaled display is not comparable.
- `display.width/height` set the window; check the PNG's size, the OS can clamp it on a small display.
- The shot includes HUD, chat and world; windows open over them (the autolathe covers the hotbar).
- Fonts: tgui uses Verdana if installed, else bundled DejaVu Sans; other machines may differ.
- Say the size and scale with any shot you hand over.

## 6. Reading the logs

The client log is the file you redirected (`$S/client.log`), plus `bin/kstest/user_data/logs/`. Use `grep -a` (binary bytes).
```sh
grep -a "TempKs.*threw\|no ui key" $S/client.log
grep -a "^\[ERRO\]\|^\[WARN\]" $S/client.log | grep -a "Ks"
grep -a "^\[ERRO\]" $S/client.log | grep -av "unregistered variable"
grep -a "Unhandled exception" $S/client.log      # a crash; the stack follows
grep -a "ERRO\|Exception" $S/server.log
```
Baseline noise (not yours): `Trying to set unregistered variable '...'`, `parallax: Failed to loaded parallax`,
`Unknown messageId "hud-chatbox-select-channel-None"` and `"ghost-gui-ghost-respawn-button"`, `MainLoop: Cannot keep up!`.
Never print the OpenGL extensions debug line (it is enormous).

## 6b. Measuring prediction frame by frame

A screenshot can't show a flicker that lasts a round trip, and a headless test can miss it: in a real client, input is
handled inside a tick and a frame runs before the next state, an order the integration pair doesn't reproduce. Twice a
passing test hid a misprediction that this caught at once. To see what a player sees:

- **Add latency** on both sides: `--cvar net.fakelagmin=0.05` on the server and the client. Locally, a state is otherwise
  back before a frame passes and nothing shows.
- **Act through the real input path**, not a server command: `InputSystem.HandleInputCommand(session, EngineKeyFunctions.Use,
  new ClientFullInputCmdMessage(tick, fraction, functionId) { State = Down, Coordinates, Uid })`, then the same with `Up`.
- **Log from `FrameUpdate` for ~90 frames**, the values the UI reads (`GetMaterialAmount`, not the raw component field; a
  client-side overlay can sit between them), plus what's in hand and whether the window is open. Print only the lines where
  something changed: a bounce reads as `3000, 0, 3000`.

Then encode what you found as a test where the headless client can reproduce it, or as a pure decision function where it
can't (`MaterialStorageSystem.KsCanRelease`).

## 7. Cleanup (always)

```sh
rm -rf Content.Server/_KS14/TempKs Content.Client/_KS14/TempKs
git status --short        # none of your files may remain
```
Stop the server (the client exits itself):
`Get-Process Content.Server,Content.Client | ? { $_.Path -like '*\bin\kstest\*' } | Stop-Process`.
Confirm with `tasklist | grep Content` that only the user's own processes remain. Leave `bin/kstest` in place; the next
`ks.py build` drops the harness from it.
