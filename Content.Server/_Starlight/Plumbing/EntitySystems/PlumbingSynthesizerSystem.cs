using Content.Server._Starlight.Plumbing.Components;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.UserInterface;
using Content.Shared._Starlight.Plumbing;
using Content.Shared._Starlight.Plumbing.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.PowerCell;
using Content.Shared.PowerCell.Components;
using Content.Shared.Power.EntitySystems;
using Content.Shared.UserInterface;
using JetBrains.Annotations;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server._Starlight.Plumbing.EntitySystems;

/// <summary>
///     Handles plumbing synthesizer machine behavior: basically a small reagent dispenser,
///     you can select a generatable reagent and it will produce it into its buffer container.
///     Uses power from an internal battery and the reagents have the same power cost as reagent dispensers.
///     Charges from APC like reagent dispensers as well.   
/// </summary>
[UsedImplicitly]
public sealed class PlumbingSynthesizerSystem : EntitySystem
{
    [Dependency] private readonly SharedSolutionContainerSystem _solutionSystem = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly PowerCellSystem _powerCell = default!;
    [Dependency] private readonly BatterySystem _battery = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly Dictionary<EntityUid, TimeSpan> _nextUiUpdate = new();
    private static readonly TimeSpan UiUpdateInterval = TimeSpan.FromSeconds(0.5);
    private const float ChargeRate = 5f; // 5W charge rate when powered

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlumbingSynthesizerComponent, PlumbingDeviceUpdateEvent>(OnSynthesizerUpdate);
        SubscribeLocalEvent<PlumbingSynthesizerComponent, PlumbingSynthesizerToggleMessage>(OnToggle);
        SubscribeLocalEvent<PlumbingSynthesizerComponent, PlumbingSynthesizerSelectReagentMessage>(OnSelectReagent);
        SubscribeLocalEvent<PlumbingSynthesizerComponent, BoundUIOpenedEvent>(OnUIOpened);
        SubscribeLocalEvent<PlumbingSynthesizerComponent, PlumbingPullAttemptEvent>(OnPullAttempt);
        SubscribeLocalEvent<PlumbingSynthesizerComponent, ComponentRemove>(OnComponentRemove);
    }

    /// <summary>
    ///     Charge the battery when the machine is powered like a dispenser
    /// </summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;

        var query = EntityQueryEnumerator<PlumbingSynthesizerComponent, PowerCellSlotComponent, ApcPowerReceiverComponent, ActivatableUIComponent>();
        while (query.MoveNext(out var uid, out var synth, out var cellSlot, out var powerReceiver, out var activatableUI))
        {
            if (!_powerCell.TryGetBatteryFromSlot((uid, cellSlot), out var batteryUid))
                continue;

            if (!powerReceiver.Powered)
                continue;

            if (_battery.IsFull((batteryUid.Value, batteryUid.Value.Comp)))
                continue;

            // Charge the battery
            _battery.ChangeCharge((batteryUid.Value, batteryUid.Value.Comp), ChargeRate * frameTime);

            // Update UI periodically if open
            var uiOpen = activatableUI.Key != null && _ui.IsUiOpen(uid, activatableUI.Key);
            if (uiOpen)
            {
                if (!_nextUiUpdate.TryGetValue(uid, out var nextUpdate) || curTime >= nextUpdate)
                {
                    _nextUiUpdate[uid] = curTime + UiUpdateInterval;
                    UpdateUI((uid, synth));
                }
            }
        }
    }

    private void OnComponentRemove(Entity<PlumbingSynthesizerComponent> ent, ref ComponentRemove args)
    {
        _nextUiUpdate.Remove(ent.Owner);
    }

    private void OnSynthesizerUpdate(Entity<PlumbingSynthesizerComponent> ent, ref PlumbingDeviceUpdateEvent args)
    {
        if (!ent.Comp.Enabled)
            return;

        if (ent.Comp.SelectedReagent == null)
            return;

        if (!_solutionSystem.TryGetSolution(ent.Owner, ent.Comp.BufferSolutionName, out var bufferEnt, out var buffer))
            return;

        // Check if buffer has space
        var availableSpace = buffer.AvailableVolume;
        if (availableSpace <= 0)
            return;

        // Get power drain for selected reagent
        if (!ent.Comp.GeneratableReagents.TryGetValue(ent.Comp.SelectedReagent.Value, out var powerDrain))
            return;

        // Calculate how much we can generate
        var toGenerate = FixedPoint2.Min(availableSpace, buffer.MaxVolume);

        // Check if we have enough power
        var powerNeeded = powerDrain * (float)toGenerate;
        if (!_powerCell.HasCharge(ent.Owner, powerNeeded))
        {
            // Try to generate as much as we can afford
            if (!_powerCell.TryGetBatteryFromSlot(ent.Owner, out var batteryCheck))
                return;

            var availableCharge = batteryCheck.Value.Comp.LastCharge;
            if (availableCharge <= 0)
                return;

            var affordableUnits = FixedPoint2.New((int)(availableCharge / powerDrain));
            if (affordableUnits <= 0)
                return;

            toGenerate = FixedPoint2.Min(toGenerate, affordableUnits);
            powerNeeded = powerDrain * (float)toGenerate;
        }

        // Use power and generate reagent
        if (!_powerCell.TryUseCharge(ent.Owner, powerNeeded))
            return;

        _solutionSystem.TryAddReagent(bufferEnt.Value, new ReagentId(ent.Comp.SelectedReagent.Value, null), toGenerate, out _);
        UpdateUI(ent);
    }

    /// <summary>
    ///     Only allow pulling the selected reagent. Maybe not needed but trying to fix an issue
    /// </summary>
    private void OnPullAttempt(Entity<PlumbingSynthesizerComponent> ent, ref PlumbingPullAttemptEvent args)
    {
        // If no reagent selected, or the requested reagent doesn't match, cancel
        if (ent.Comp.SelectedReagent == null || args.ReagentPrototype != ent.Comp.SelectedReagent)
        {
            args.Cancelled = true;
        }
    }

    private void OnToggle(Entity<PlumbingSynthesizerComponent> ent, ref PlumbingSynthesizerToggleMessage args)
    {
        ent.Comp.Enabled = args.Enabled;
        Dirty(ent);
        UpdateUI(ent);
    }

    private void OnSelectReagent(Entity<PlumbingSynthesizerComponent> ent, ref PlumbingSynthesizerSelectReagentMessage args)
    {
        if (args.ReagentId == null)
        {
            ent.Comp.SelectedReagent = null;
        }
        else if (ent.Comp.GeneratableReagents.ContainsKey(args.ReagentId))
        {
            ent.Comp.SelectedReagent = args.ReagentId;
        }

        Dirty(ent);
        UpdateUI(ent);
    }

    private void OnUIOpened(Entity<PlumbingSynthesizerComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUI(ent);
    }

    private void UpdateUI(Entity<PlumbingSynthesizerComponent> ent)
    {
        if (!_solutionSystem.TryGetSolution(ent.Owner, ent.Comp.BufferSolutionName, out _, out var buffer))
            return;

        // Get battery charge ratio
        var batteryCharge = 0f;
        if (_powerCell.TryGetBatteryFromSlot(ent.Owner, out var battery))
        {
            batteryCharge = battery.Value.Comp.LastCharge / battery.Value.Comp.MaxCharge;
        }

        var generatableReagents = new Dictionary<string, float>();
        foreach (var (reagent, drain) in ent.Comp.GeneratableReagents)
        {
            generatableReagents[reagent.Id] = drain;
        }

        var bufferContents = new Dictionary<string, FixedPoint2>();
        foreach (var reagent in buffer.Contents)
        {
            bufferContents[reagent.Reagent.Prototype] = reagent.Quantity;
        }

        var state = new PlumbingSynthesizerBoundUserInterfaceState(
            generatableReagents,
            ent.Comp.SelectedReagent?.Id,
            bufferContents,
            ent.Comp.Enabled,
            batteryCharge);

        _ui.SetUiState(ent.Owner, PlumbingSynthesizerUiKey.Key, state);
    }
}
