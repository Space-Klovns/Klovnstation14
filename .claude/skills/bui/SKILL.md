---
name: bui
description: How bound user interfaces (BUIs) connect a window to the player and the game in this repo - the BoundUserInterface class, UI key enums, prototype wiring (UserInterface, ActivatableUI, ActivatableUIRequires*), CreateWindow, the Open/UpdateState/Update lifecycle, opening and closing from code, range and access checks, choosing component state vs BUI state vs messages, BuiPredictionState and InputCoalescer, SendPredictedMessage and shared handlers, and the bugs simple BUIs have (stale values, state overwriting what the player types, snap-back). Use when adding or changing a BUI, its state or messages, when a window shows old values, reverts input, flickers, sends too many messages, never opens, or stays blank. For XAML, layout, styling and windows cut off at the bottom use the ui skill.
---

# Bound user interfaces

Read from `RobustToolbox` v291 and content. `SUIS` = `Robust.Shared/GameObjects/Systems/SharedUserInterfaceSystem.cs`.
XAML, layout, styles, window sizing and UI tests are in the `ui` skill. Read its *Sizing a window* before giving a BUI window a size. ECS and naming rules are in `CLAUDE.md` §4, and localisation in §7.

## Getting a window on screen

In one `Content.Shared` file, the contract: a `KsWidgetUiKey` enum, messages deriving `BoundUserInterfaceMessage`, and any
state deriving `BoundUserInterfaceState`, all `[Serializable, NetSerializable]`. Then the prototype:

```yaml
- type: ActivatableUI              # opens on activate or verb; `key` is required
  key: enum.KsWidgetUiKey.Key
- type: ActivatableUIRequiresAnchor  # also RequiresPower, RequiresAccess, RequiresVision, RequiresPanel, RequiresPowerCell
- type: UserInterface
  interfaces:
    enum.KsWidgetUiKey.Key:
      type: KsWidgetBoundUserInterface  # found by bare class name; needs an (EntityUid, Enum) constructor
      interactionRange: 3               # default 2; 0 or less disables the range check
      # requireInputValidation: false   # default true; see the table below
```

| Member | When it runs |
| --- | --- |
| Constructor | Built by reflection as soon as the client is an actor. The base constructor injects `[Dependency]` fields, so `IoCManager.InjectDependencies` isn't needed. |
| `Open()` | Deferred to `SUIS.Update`, outside state handling. Call `base.Open()`, then `_window = this.CreateWindow<T>()`, and wire events. |
| `UpdateState(state)` | When a state arrives that isn't `Equals` to the stored one. Never for a component change. |
| `Update()` | Straight after every `UpdateState`, and whenever your client system calls it. |
| `Update<T>()` | Re-applies the stored state, then `Update()`. For windows built from a state *and* a component (`GasCanisterSystem`). |
| `ReceiveMessage(msg)` | For each `ServerSendUiMessage`. |

**Blank on open:** right after `Open()`, the engine runs `UpdateState` + `Update` only if a state is stored. With component
state there is none, so call `Update()` at the end of `Open()`. A stored state can also be left over from the last viewer,
so send a fresh one from `BoundUIOpenedEvent` or `BeforeActivatableUIOpenEvent` (`GasCanisterSystem`, `SpaceHeaterSystem`).

**From code:** `_uiSystem.OpenUi(uid, key, actor)`, `TryOpenUi` (false if an attempt handler refused), `TryToggleUi`,
`CloseUi(uid, key)` for everyone, `CloseUi(uid, key, actor)`, `CloseUis(uid)`. `OpenUi` silently does nothing if the entity
lacks the key, and it skips everything `ActivatableUI` checks. `CreateWindow` wires `OnClose` to `bui.Close()` and disposes
the window with the BUI, so `Dispose` needs no override for it.

### Who validates what

| Check | Where | When |
| --- | --- | --- |
| `CanInteract`, `requiresComplex`, `inHandsOnly`, `adminOnly`, `singleUser`, then `ActivatableUIOpenAttemptEvent` (each `Requires*`) | `ActivatableUISystem.InteractUI` | Opening by activate or verb |
| Range (`BoundUserInterfaceCheckRangeEvent`) | `SUIS.Update` | Every tick; failure closes the UI |
| Sender has this UI open | `SUIS.OnMessageReceived` | Every message |
| `CanInteract`, accessibility if range is 0 or less, `singleUser`, `requiresComplex` | `SharedInteractionSystem.OnBoundInterfaceInteractAttempt` | Every message, if `requireInputValidation` |
| Value ranges, access, power, game rules | **Your message handler** | Every message |

A ghost who can't interact can open a UI but not press anything. After opening, anchor fails the range check, and power and
vision close the UI. **`ActivatableUIRequiresAccess` is checked only at open**, so someone whose ID is taken away can keep
pressing: check access in the handler. For a per-message rule, subscribe to `BoundUserInterfaceMessageAttempt` (`BarSignSystem`).

## Choosing a transport

| | Component state | BUI state | Server-to-client message |
| --- | --- | --- | --- |
| API | `[AutoNetworkedField]`, `Dirty` | `SetUiState` | `ServerSendUiMessage` |
| Reaches | Everyone in PVS | Everyone in PVS | Actors on that key, or one actor |
| Use for | Settings that gameplay reads and shared code can predict | Server-only values that change constantly and matter only while open | One-shot results, per-viewer data |

The engine calls `UserInterfaceComponent.States` "legacy data": prefer component state, unless the value only exists on the
server (a power network, an atmos reading) or dirtying it would be wasteful. A BUI state isn't private: the engine filters
only `Actors` per player, so no secrets in it. **Trap:** the `ICommonSession` overload of `ServerSendUiMessage` returns early
on the server (`if (!_netManager.IsClient)`). Pass the actor's `EntityUid`.

## Good practice

**Send raw values and limits, not text.** The window formats them, so numbers follow the client's culture (§7), and the
limits size its sliders, so none is copied into client code. It computes what derives from them (an ETA) and animates in
`FrameUpdate`. It never talks to the network: `event Action<float>` out, `Update(state)` in, and only the BUI sends.

**For live values, one method builds the state, from `Update`.** Handlers only change the game, so no path can forget to
refresh the UI. Order it after the simulation (`UpdatesAfter`), and skip closed UIs, which then cost one check and no traffic:

```csharp
if (_uiSystem.IsUiOpen(uid, KsWidgetUiKey.Key)) // inside the query loop in Update
    _uiSystem.SetUiState(uid, KsWidgetUiKey.Key, new KsWidgetBuiState { Rate = widgetComponent.Rate, MaxRate = widgetComponent.MaxRate });
```

**Clamp in the handler against the entity's own limits**, never a constant, and treat UI clamps as a courtesy. §6
(*Client-sent floats*) covers what the server flushes and what it doesn't. Server-sent NaN reaches the client unflushed, so
windows that divide check `float.IsFinite`.

**Writing to a control must not send a message.** `Range.Value`, and `MinValue`/`MaxValue` when they re-clamp, raise
`OnValueChanged`, so every state would echo back as a message and overwrite another player's change. Set limits, then the
value, inside a `_suppressEvents = true/false` pair that the slider handler checks. `SetValueWithoutEvent` covers the value
alone. `LineEdit.Text`, `FloatSpinBox.Value` and `OnOffButton.IsOn` raise nothing.

**One message per tick for continuous input.** A slider raises an event per mouse move. Hold the latest value in an
`InputCoalescer<T>` and flush it from `IBuiPreTickUpdate`. Or commit on release (`KsExpansionValveWindow`).

**Replay what the server hasn't answered.** Until the server handles a change, its states still hold the old value, so the
control snaps back for a round trip. `BuiPredictionState` queues sent messages and returns those newer than the last server
state, so they're re-applied to each one. `TurbineBoundUserInterface` is a second user.

```csharp
public sealed partial class KsWidgetBoundUserInterface(EntityUid owner, Enum uiKey)
    : BoundUserInterface(owner, uiKey), IBuiPreTickUpdate
{
    [Dependency] private IClientGameTiming _gameTiming = default!;

    private KsWidgetWindow? _window;
    private BuiPredictionState? _prediction;
    private InputCoalescer<float> _rateCoalescer; // mutable struct: keep it a field, don't copy it

    protected override void Open()
    {
        base.Open();
        _prediction = new BuiPredictionState(this, _gameTiming);
        _window = this.CreateWindow<KsWidgetWindow>();
        _window.OnRate += rate => _rateCoalescer.Set(rate); // a switch would call _prediction.SendMessage directly
    }

    void IBuiPreTickUpdate.PreTickUpdate()
    {
        if (_rateCoalescer.CheckIsModified(out var rate))
            _prediction!.SendMessage(new KsWidgetSetRateMessage(rate));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is not KsWidgetBuiState widgetState)
            return;

        foreach (var message in _prediction!.MessagesToReplay())
            if (message is KsWidgetSetRateMessage setRate)
                widgetState.Rate = setRate.Rate; // state fields stay mutable for this

        _window?.Update(widgetState);
    }
}
```

This corrects itself only if the server keeps sending states, or a rejected change stays on screen. So don't give such a
state `Equals`: `SetUiState` drops a state equal to the stored one.

**Don't write over the player's input.** Three working ways:
- Guard the write: `if (!Input.HasKeyboardFocus())` and `if (!Slider.Grabbed)` (`KsExpansionValveWindow.UpdateState`).
- Compare first: `GasCanisterWindow.SetReleasePressure` returns if the value is unchanged, so uncommitted text survives.
- Keep them apart: `TurbineWindow` shows state in labels, and writes its inputs only on open and from the player's own edit.

## What goes wrong in simpler UIs

`GasPressurePumpBoundUserInterface` has the right shape for a settings-only device: component state, shared handler, no
polling. Its faults are missing guards, so fix those rather than moving it to a BUI state.

| Pattern | Where | What goes wrong |
| --- | --- | --- |
| Every refresh writes the input | `GasPressurePumpWindow`, `GasVolumePumpWindow`, `GasThermomachineWindow`, `GasFilterWindow`, `GasMixerWindow` | Toggling power refreshes the window, which rewrites the field (pump: `OnToggleStatusMessage` → `UpdateUi` → `Update` → `SetOutputPressure`). Type a value, flip the switch before Set, and the typed value is gone. Another player's change does it mid-typing. Writing `Text` raises no event, so in the `LineEdit` windows Set stays enabled over the reverted number. |
| Refresh only where a handler remembers | `GasFilterSystem.DirtyUI`, `GasMixerSystem.DirtyUI`, `SpaceHeaterSystem.DirtyUI` | State is sent from specific handlers and on open. Anything else leaves the window stale: the title comes from `MetaData` inside `DirtyUI`, so a rename doesn't show. Live readings can't work this way. |
| Limits on the client | `GasMixerBoundUserInterface.MaxPressure`, `GasVolumePumpWindow.SetMaxRateButton`, `GasVolumePumpBoundUserInterface._maxTransferRate` | The mixer BUI caps at a constant while the server allows `mixer.MaxTargetPressure`. The volume pump's Set Max uses the constant, not `pump.MaxTransferRate`. `_maxTransferRate` is read once in `Open` from a non-networked field. |
| Client and server disagree | `GasPressurePumpWindow` vs `SharedGasPressurePumpSystem` | The window clamps to `pump.MaxTargetPressure`, the handler to `Atmospherics.MaxOutputPressure`. The per-entity limit exists only on the client. |
| Bad input becomes a value | `UserInputParser.TryFloat(...) ? parsed : 0f` in the volume pump, filter and mixer BUIs (mixer percentage: `1f`) | An empty or mistyped field plus Set sends 0 and stops the device. Send nothing, and leave the field as it was. |
| No prediction | `SendMessage` in the filter, mixer and heater BUIs | A switch waits a round trip to show its new state. |
| Optimistic write, no replay | `SpaceHeaterBoundUserInterface.OnModeChanged` | `SelectId` runs locally, but any state built before the server handled it (`OnPowerChanged` → `DirtyUI`) puts the old mode back until the next one arrives. |

## Prediction

`SendMessage` goes to the server only. `SendPredictedMessage` also raises the message locally, and again on every
re-predicted tick until the server has processed it (`ClientGameStateManager`). `BoundUserInterfaceMessageAttempt` and
`args.Actor` work the same on both sides. So a handler in shared code predicts by itself, but it re-runs: it may only change
networked state (and must `Dirty`), and anything else needs an `IsFirstTimePredicted` gate. If the handler is server-only,
nothing is predicted; use replay above.

```csharp
// Shared
[SubscribeLocalEvent]
private void OnSetRate(Entity<KsWidgetComponent> entity, ref KsWidgetSetRateMessage args)
{
    entity.Comp.Rate = Math.Clamp(args.Rate, 0f, entity.Comp.MaxRate);
    Dirty(entity);
    UpdateUi(entity); // protected virtual, empty in shared
}

// Client subclass: AfterAutoHandleStateEvent also calls UpdateUi, for other players' changes.
protected override void UpdateUi(Entity<KsWidgetComponent> entity)
{
    if (_uiSystem.TryGetOpenUi(entity.Owner, KsWidgetUiKey.Key, out var bui))
        bui.Update();
}
```

See `SharedGasPressurePumpSystem` and its client subclass. The component needs `[AutoGenerateComponentState(true)]`, or
`AfterAutoHandleStateEvent` never fires. A plain subscription fires for every key. If a component has several UIs sharing a message type, filter on `args.UiKey` or
use `Subs.BuiEvents<TComp>(key, subs => ...)`, which does it for you.
