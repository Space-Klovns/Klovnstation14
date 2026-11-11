using Content.Server.Humanoid.Systems;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;

namespace Content.Server._KS14.SupplyPod.Passengers;

/// <summary>
///     Loads NPCs into supply pods asleep, and wakes them when the pod lets them out.
/// </summary>
public sealed partial class KsSupplyPodPassengerSystem : EntitySystem
{
    [Dependency] private NPCSystem _npcSystem = default!;
    [Dependency] private SharedEntityStorageSystem _entityStorageSystem = default!;
    [Dependency] private RandomHumanoidSystem _randomHumanoidSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private EntityQuery<HTNComponent> _htnQuery = default!;

    /// <summary>
    ///     Puts <paramref name="passengerUid"/> into the pod's storage and keeps it asleep until it leaves.
    /// </summary>
    public bool TryBoard(EntityUid passengerUid, Entity<EntityStorageComponent?> pod)
    {
        if (!Resolve(pod.Owner, ref pod.Comp, logMissing: false) ||
            !_entityStorageSystem.Insert(passengerUid, pod.Owner, pod.Comp))
            return false;

        EnsureComp<KsSupplyPodPassengerComponent>(passengerUid).PodUid = pod.Owner;

        if (_htnQuery.TryComp(passengerUid, out var htnComponent))
            _npcSystem.SleepNPC(passengerUid, htnComponent);

        return true;
    }

    [SubscribeLocalEvent]
    private void OnPassengerPodMapInit(Entity<KsSupplyPodHumanoidPassengersComponent> entity, ref MapInitEvent args)
    {
        var coordinates = _transformSystem.GetMoverCoordinates(entity.Owner);
        var name = Loc.GetString(entity.Comp.Name);

        foreach (var settingsId in entity.Comp.Settings)
        {
            var passengerUid = _randomHumanoidSystem.SpawnRandomHumanoid(settingsId, coordinates, name);
            if (!TryBoard(passengerUid, entity.Owner))
                Log.Warning($"{ToPrettyString(entity.Owner)} couldn't take aboard {ToPrettyString(passengerUid)}, which is left beside it.");
        }
    }

    [SubscribeLocalEvent]
    private void OnRemovedFromContainer(Entity<KsSupplyPodPassengerComponent> entity, ref EntGotRemovedFromContainerMessage args)
    {
        if (args.Container.Owner != entity.Comp.PodUid)
            return;

        RemCompDeferred(entity.Owner, entity.Comp);

        // Deleted along with the pod, rather than let out of it.
        if (TerminatingOrDeleted(entity.Owner))
            return;

        if (!_htnQuery.TryComp(entity.Owner, out var htnComponent) ||
            !CanWake(entity.Owner))
            return;

        _npcSystem.WakeNPC(entity.Owner, htnComponent);

        var ev = new KsSupplyPodPassengerDisembarkedEvent(entity.Comp.PodUid);
        RaiseLocalEvent(entity.Owner, ref ev);
    }

    /// <summary>
    ///     Waking for being healed back up would let a passenger think inside the pod again.
    /// </summary>
    [SubscribeLocalEvent(after: [typeof(HTNSystem)])] // HTNSystem subscribes NPCSystem.OnMobStateChange, which wakes it
    private void OnMobStateChanged(Entity<KsSupplyPodPassengerComponent> entity, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive ||
            !_htnQuery.TryComp(entity.Owner, out var htnComponent))
            return;

        _npcSystem.SleepNPC(entity.Owner, htnComponent);
    }

    /// <summary>
    ///     A passenger that died or went critical on the way stays asleep, as it would have anywhere else.
    /// </summary>
    private bool CanWake(EntityUid passengerUid)
        => !TryComp<MobStateComponent>(passengerUid, out var mobStateComponent) ||
           mobStateComponent.CurrentState == MobState.Alive;
}

/// <summary>
///     Raised on an NPC when the supply pod it rode in lets it out, awake.
/// </summary>
[ByRefEvent]
public readonly record struct KsSupplyPodPassengerDisembarkedEvent(EntityUid PodUid);
