using Content.Shared.Body.Components;
using Content.Shared.Body.Events;
using Content.Shared.Body.Systems;
using Content.Shared.Damage.Systems;
using Robust.Shared.GameStates;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Perks.Healing;

/// <summary>
///     Never bleeds: wounds open no bleeding, and any already open closes.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class KsNoBleedingAbilityComponent : Component;

public sealed partial class KsNoBleedingSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedBloodstreamSystem _bloodstreamSystem = default!;
    [Dependency] private EntityQuery<BloodstreamComponent> _bloodstreamQuery = default!;

    [SubscribeLocalEvent]
    private void OnStartup(Entity<KsNoBleedingAbilityComponent> entity, ref ComponentStartup args)
    {
        if (!_gameTiming.ApplyingState)
            StopBleeding(entity.Owner);
    }

    // A by-value event, so no Entity<T> handler (RA0054). After the bloodstream has opened the wound.
    [SubscribeLocalEvent(after: [typeof(SharedBloodstreamSystem)])]
    private void OnDamageChanged(EntityUid uid, KsNoBleedingAbilityComponent component, DamageChangedEvent args)
    {
        // A state being applied carries the bleed the server settled on.
        if (args.DamageIncreased && !_gameTiming.ApplyingState)
            StopBleeding(uid);
    }

    /// <summary>
    ///     Anything else that started a bleed (a reagent) loses no blood to it and is closed on the next bleed tick.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnBleedModifier(Entity<KsNoBleedingAbilityComponent> entity, ref BleedModifierEvent args)
    {
        args.BleedAmount = 0f;
        args.BleedReductionAmount = float.MaxValue;
    }

    private void StopBleeding(EntityUid uid)
    {
        if (_bloodstreamQuery.TryComp(uid, out var bloodstreamComponent) && bloodstreamComponent.BleedAmount > 0f)
            _bloodstreamSystem.TryModifyBleedAmount((uid, bloodstreamComponent), -bloodstreamComponent.BleedAmount);
    }
}
