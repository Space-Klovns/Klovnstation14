using Content.Server._KS14.KillCredit;
using Content.Shared._KS14.Companions;
using Content.Shared._KS14.Perks.Support;
using Content.Shared.Mobs;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Random;

namespace Content.Server._KS14.Perks.Support;

/// <summary>
///     The Support perk's ammo drop (<see cref="KsAmmoDropAbilityComponent"/>): a kill may leave a resupply
///         (<see cref="KsAmmoPickupComponent"/>) that refills a friendly's gun when they walk over it
///         (<see cref="KsAmmoPickupSystem"/>).
/// </summary>
public sealed partial class KsAmmoDropSystem : EntitySystem
{
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private KsAmmoPickupSystem _ammoPickupSystem = default!;
    [Dependency] private KsKillCreditSystem _killCreditSystem = default!;
    [Dependency] private NpcFactionSystem _npcFactionSystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private EntityQuery<KsAmmoDropAbilityComponent> _dropAbilityQuery = default!;
    [Dependency] private EntityQuery<KsCompanionComponent> _companionQuery = default!;
    [Dependency] private EntityQuery<NpcFactionMemberComponent> _factionMemberQuery = default!;

    // MobStateChangedEvent is raised by value, so a plain broadcast handler.
    [SubscribeLocalEvent]
    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead ||
            args.OldMobState == MobState.Dead ||
            !TryGetDropper(_killCreditSystem.GetAttacker(args.Origin), out var dropper) ||
            dropper.Owner == args.Target ||
            _npcFactionSystem.IsEntityFriendly(dropper.Owner, args.Target) ||
            !_robustRandom.Prob(Math.Clamp(dropper.Comp.Chance, 0f, 1f)))
            return;

        var pickupUid = Spawn(dropper.Comp.Pickup, _transformSystem.GetMapCoordinates(args.Target));
        var pickupComponent = EnsureComp<KsAmmoPickupComponent>(pickupUid);
        if (_factionMemberQuery.TryComp(dropper.Owner, out var factionMemberComponent))
            _ammoPickupSystem.SetFactions((pickupUid, pickupComponent), factionMemberComponent.Factions);
    }

    /// <summary>
    ///     Who the kill counts for: the attacker, whoever holds it (a gun fires a beam as itself), or the owner of a
    ///         companion that made it.
    /// </summary>
    private bool TryGetDropper(EntityUid? attackerUid, out Entity<KsAmmoDropAbilityComponent> dropper)
    {
        dropper = default;
        if (attackerUid is not { } uid || Deleted(uid))
            return false;

        if (_containerSystem.TryGetContainingContainer(uid, out var container))
            uid = container.Owner;

        if (_companionQuery.TryComp(uid, out var companionComponent) && companionComponent.OwnerUid is { } ownerUid)
            uid = ownerUid;

        if (!_dropAbilityQuery.TryComp(uid, out var dropComponent))
            return false;

        dropper = (uid, dropComponent);
        return true;
    }
}
