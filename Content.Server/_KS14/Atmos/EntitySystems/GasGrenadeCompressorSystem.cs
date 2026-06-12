using Content.Server.Atmos.EntitySystems;
using Content.Server.Materials;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.EntitySystems;
using Content.Shared._KS14.Atmos.Components;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Emag.Components;
using Content.Shared.Emag.Systems;
using Content.Shared.Materials;
using Content.Shared.Trigger;
using Content.Shared.Trigger.Components;
using Content.Shared.Trigger.Components.Effects;
using Content.Shared.Trigger.Components.Triggers;
using Content.Shared.Trigger.Systems;
using JetBrains.Annotations;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown.Mapping;

namespace Content.Server._KS14.Atmos.EntitySystems;

[UsedImplicitly]
public sealed class GasGrenadeCompressorSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private readonly NodeContainerSystem _nodeContainer = default!;
    [Dependency] private readonly PowerReceiverSystem _power = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly MaterialStorageSystem _materialStorage = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly ISerializationManager _serializationManager = default!;

    private const string AirGrenadeId = "AirGrenade";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GasGrenadeCompressorComponent, AtmosDeviceUpdateEvent>(OnUpdate);
        SubscribeLocalEvent<GasGrenadeCompressorComponent, GasGrenadeCompressorChangeTargetPressureMessage>(OnChangeTargetPressure);
        SubscribeLocalEvent<GasGrenadeCompressorComponent, GasGrenadeCompressorToggleMessage>(OnToggle);
        SubscribeLocalEvent<GasGrenadeCompressorComponent, GasGrenadeCompressorRearmMessage>(OnRearm);
        SubscribeLocalEvent<GasGrenadeCompressorComponent, GotEmaggedEvent>(OnEmagged);
        SubscribeLocalEvent<GasGrenadeCompressorComponent, MaterialAmountChangedEvent>(OnMaterialAmountChanged);
    }

    private void OnUpdate(EntityUid uid, GasGrenadeCompressorComponent comp, ref AtmosDeviceUpdateEvent args)
    {
        if (!comp.Enabled || !_power.IsPowered(uid))
        {
            UpdateUserInterface(uid, comp);
            return;
        }

        if (!_nodeContainer.TryGetNode(uid, comp.InletName, out PipeNode? inlet))
        {
            UpdateUserInterface(uid, comp);
            return;
        }

        if (!_itemSlots.TryGetSlot(uid, "grenade_slot", out var slot) || slot.Item is not { } grenade)
        {
            UpdateUserInterface(uid, comp);
            return;
        }

        if (!TryComp<ReleaseGasOnTriggerComponent>(grenade, out var releaseComp) || releaseComp.Air == null)
        {
            UpdateUserInterface(uid, comp);
            return;
        }

        var grenadeAir = releaseComp.Air;
        if (grenadeAir.Pressure >= comp.TargetPressure)
        {
            UpdateUserInterface(uid, comp);
            return;
        }

        // Transfer gas
        var transferVol = Atmospherics.MaxTransferRate * _atmosphereSystem.PumpSpeedup() * args.dt;
        
        // We want to fill the grenade up to TargetPressure.
        var deltaMoles = -AtmosphereSystem.MolesToPressureThreshold(grenadeAir, comp.TargetPressure);
        if (deltaMoles <= 0)
        {
            UpdateUserInterface(uid, comp);
            return;
        }

        var availableMoles = inlet.Air.TotalMoles;
        var molesToTransfer = Math.Min(deltaMoles, availableMoles);
        
        // Ensure we don't transfer more than the transfer rate allows
        var maxMolesByRate = (comp.TargetPressure * transferVol) / (Atmospherics.R * inlet.Air.Temperature);
        molesToTransfer = Math.Min(molesToTransfer, maxMolesByRate);

        if (molesToTransfer <= 0)
        {
            UpdateUserInterface(uid, comp);
            return;
        }

        var removed = inlet.Air.Remove(molesToTransfer);

        // Whitelist check
        bool emagged = HasComp<EmaggedComponent>(uid);
        if (!emagged)
        {
            var filteredRemoved = new GasMixture(removed.Volume) { Temperature = removed.Temperature };
            foreach (var gas in comp.GasWhitelist)
            {
                var moles = removed.GetMoles(gas);
                if (moles > 0)
                {
                    filteredRemoved.SetMoles(gas, moles);
                    removed.SetMoles(gas, 0);
                }
            }
            // Put back non-whitelisted gases
            _atmosphereSystem.Merge(inlet.Air, removed);
            removed = filteredRemoved;
        }

        _atmosphereSystem.Merge(grenadeAir, removed);
        UpdateUserInterface(uid, comp);
    }

    private void UpdateUserInterface(EntityUid uid, GasGrenadeCompressorComponent comp)
    {
        if (!_ui.HasUi(uid, GasGrenadeCompressorUiKey.Key))
            return;

        bool hasGrenade = false;
        float grenadePressure = 0;
        bool isSpent = false;

        if (_itemSlots.TryGetSlot(uid, "grenade_slot", out var slot) && slot.Item is { } grenade)
        {
            hasGrenade = true;
            if (TryComp<ReleaseGasOnTriggerComponent>(grenade, out var releaseComp))
            {
                grenadePressure = releaseComp.Air?.Pressure ?? 0;
            }
            else
            {
                isSpent = true;
            }
        }

        int steelAmount = 0;
        if (TryComp<MaterialStorageComponent>(uid, out var storage))
        {
            steelAmount = _materialStorage.GetMaterialAmount(uid, "Steel", storage);
        }

        var state = new GasGrenadeCompressorBoundUserInterfaceState(comp.TargetPressure, comp.Enabled, hasGrenade, grenadePressure, isSpent, steelAmount);
        _ui.SetUiState(uid, GasGrenadeCompressorUiKey.Key, state);
    }

    private void OnChangeTargetPressure(EntityUid uid, GasGrenadeCompressorComponent comp, GasGrenadeCompressorChangeTargetPressureMessage args)
    {
        comp.TargetPressure = Math.Clamp(args.TargetPressure, 0, comp.MaxTargetPressure);
        UpdateUserInterface(uid, comp);
    }

    private void OnToggle(EntityUid uid, GasGrenadeCompressorComponent comp, GasGrenadeCompressorToggleMessage args)
    {
        comp.Enabled = args.Enabled;
        UpdateUserInterface(uid, comp);
    }

    private void OnRearm(EntityUid uid, GasGrenadeCompressorComponent comp, GasGrenadeCompressorRearmMessage args)
    {
        if (!_itemSlots.TryGetSlot(uid, "grenade_slot", out var slot) || slot.Item is not { } grenade)
            return;

        if (HasComp<ReleaseGasOnTriggerComponent>(grenade))
            return; // Not spent

        if (HasComp<ActiveTimerTriggerComponent>(grenade))
            return; // Still ticking

        if (!TryComp<MaterialStorageComponent>(uid, out var storage) || _materialStorage.GetMaterialAmount(uid, "Steel", storage) < 1000)
            return; // Not enough steel

        // Consume steel
        _materialStorage.TryChangeMaterialAmount(uid, "Steel", -1000, storage);

        // Re-arm grenade using prototype specs to ensure correctness
        if (_prototypeManager.TryIndex<EntityPrototype>(AirGrenadeId, out var proto))
        {
            if (proto.TryGetComponent<ReleaseGasOnTriggerComponent>(out var protoRelease, _componentFactory))
            {
                var release = _serializationManager.CreateCopy(protoRelease, notNullableOverride: true);
                release.Active = false;
                release.StartingTotalMoles = 0;
                if (release.Air != null)
                {
                    release.Air.Clear();
                    release.Air.Volume = 1000f;
                }
                if (TryComp<ReleaseGasOnTriggerComponent>(grenade, out var existingRelease))
                {
                    _serializationManager.CopyTo(release, ref existingRelease, notNullableOverride: true);
                    Dirty(grenade, existingRelease);
                }
                else
                {
                    AddComp(grenade, release);
                    Dirty(grenade, release);
                }
            }

            if (proto.TryGetComponent<TriggerOnUseComponent>(out var protoOnUse, _componentFactory))
            {
                var onUse = _serializationManager.CreateCopy(protoOnUse, notNullableOverride: true);
                if (TryComp<TriggerOnUseComponent>(grenade, out var existingOnUse))
                {
                    _serializationManager.CopyTo(onUse, ref existingOnUse, notNullableOverride: true);
                    Dirty(grenade, existingOnUse);
                }
                else
                {
                    AddComp(grenade, onUse);
                    Dirty(grenade, onUse);
                }
            }

            if (proto.TryGetComponent<TimerTriggerComponent>(out var protoTimer, _componentFactory))
            {
                var timer = _serializationManager.CreateCopy(protoTimer, notNullableOverride: true);
                if (TryComp<TimerTriggerComponent>(grenade, out var existingTimer))
                {
                    _serializationManager.CopyTo(timer, ref existingTimer, notNullableOverride: true);
                    Dirty(grenade, existingTimer);
                }
                else
                {
                    AddComp(grenade, timer);
                    Dirty(grenade, timer);
                }
            }

            if (proto.TryGetComponent<RemoveComponentsOnTriggerComponent>(out var protoRemove, _componentFactory))
            {
                var remove = _serializationManager.CreateCopy(protoRemove, notNullableOverride: true);
                remove.Triggered = false;
                if (TryComp<RemoveComponentsOnTriggerComponent>(grenade, out var existingRemove))
                {
                    _serializationManager.CopyTo(remove, ref existingRemove, notNullableOverride: true);
                    Dirty(grenade, existingRemove);
                }
                else
                {
                    AddComp(grenade, remove);
                    Dirty(grenade, remove);
                }
            }
        }
        else
        {
            // Fallback for non-standard grenades
            var release = EnsureComp<ReleaseGasOnTriggerComponent>(grenade);
            release.Active = false;
            release.StartingTotalMoles = 0;
            release.KeysIn = new() { "timer" };
            release.Air ??= new GasMixture(1000f);
            Dirty(grenade, release);

            EnsureComp<TriggerOnUseComponent>(grenade);
            var timer = EnsureComp<TimerTriggerComponent>(grenade);
            timer.Delay = TimeSpan.FromSeconds(3);
            Dirty(grenade, timer);
        }

        // Reset visuals to default
        _appearance.RemoveData(grenade, ReleaseGasOnTriggerVisuals.Key);
        _appearance.SetData(grenade, TriggerVisuals.VisualState, TriggerVisualState.Unprimed);

        UpdateUserInterface(uid, comp);
    }

    private void OnEmagged(EntityUid uid, GasGrenadeCompressorComponent comp, ref GotEmaggedEvent args)
    {
        args.Handled = true;
    }

    private void OnMaterialAmountChanged(EntityUid uid, GasGrenadeCompressorComponent comp, ref MaterialAmountChangedEvent args)
    {
        UpdateUserInterface(uid, comp);
    }
}
