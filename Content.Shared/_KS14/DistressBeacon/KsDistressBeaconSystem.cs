using Content.Shared.Clothing;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;

namespace Content.Shared._KS14.DistressBeacon;

/// <summary>
///     Raised on a beacon when its state has changed, so the server can start its beeping.
/// </summary>
[ByRefEvent]
public readonly record struct KsDistressBeaconStateChangedEvent(MobState State);

/// <summary>
///     Keeps a <see cref="KsDistressBeaconComponent"/> in step with its wearer: critical, dead or neither, as they
///         change state and as the beacon is put on and taken off. Sets the light and the worn sprite's state; the
///         client pulses the light and the server beeps.
/// </summary>
public sealed partial class KsDistressBeaconSystem : EntitySystem
{
    /// <summary>
    ///     The prefix of the lit worn and icon sprites (<c>on-equipped-NECK</c>, <c>on-icon</c>).
    /// </summary>
    private const string LitPrefix = "on";

    /// <summary>
    ///     The slot a beacon is worn in.
    /// </summary>
    public const string NeckSlot = "neck";

    [Dependency] private SharedPointLightSystem _pointLightSystem = default!;
    [Dependency] private ClothingSystem _clothingSystem = default!;
    [Dependency] private InventorySystem _inventorySystem = default!;
    [Dependency] private EntityQuery<MobStateComponent> _mobStateQuery = default!;

    // GotEquippedEvent and GotUnequippedEvent are by-value class events, which RA0054 lets only take the uid and
    //     component apart.
    [SubscribeLocalEvent]
    private void OnGotEquipped(EntityUid uid, KsDistressBeaconComponent component, GotEquippedEvent args)
    {
        if (args.Slot != NeckSlot)
            return;

        // Put on someone who is already down.
        var state = _mobStateQuery.TryComp(args.EquipTarget, out var mobStateComponent)
            ? mobStateComponent.CurrentState
            : MobState.Alive;

        SetState((uid, component), state);
    }

    [SubscribeLocalEvent]
    private void OnGotUnequipped(EntityUid uid, KsDistressBeaconComponent component, GotUnequippedEvent args)
        => SetState((uid, component), MobState.Alive);

    /// <summary>
    ///     By-value, as the event is (<c>RA0054</c> only allows <c>Entity&lt;T&gt;</c> handlers for <c>ref</c>
    ///         events), and broadcast: the beacon is not the one that changed state.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (!_inventorySystem.TryGetSlotEntity(args.Target, NeckSlot, out var neckUid) ||
            !TryComp<KsDistressBeaconComponent>(neckUid, out var beaconComponent))
            return;

        SetState((neckUid.Value, beaconComponent), args.NewMobState);
    }

    private void SetState(Entity<KsDistressBeaconComponent> entity, MobState state)
    {
        if (entity.Comp.State == state)
            return;

        entity.Comp.State = state;
        Dirty(entity);

        var settings = entity.Comp.SettingsFor(state);
        var lit = settings != null;
        _pointLightSystem.SetEnabled(entity.Owner, lit);
        if (settings != null)
        {
            _pointLightSystem.SetColor(entity.Owner, settings.LightColor);
            _pointLightSystem.SetRadius(entity.Owner, settings.LightRadius);
            _pointLightSystem.SetEnergy(entity.Owner, settings.LightEnergy);
        }

        _clothingSystem.SetEquippedPrefix(entity.Owner, lit ? LitPrefix : null);

        var ev = new KsDistressBeaconStateChangedEvent(state);
        RaiseLocalEvent(entity.Owner, ref ev);
    }
}
