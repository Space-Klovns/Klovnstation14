using Content.Server._KS14.NPC.Hands;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Squad.Tactics;
using Content.Server._KS14.NPC.Systems;
using Content.Server._KS14.NPC.Voices;
using Content.Server.Hands.Systems;
using Content.Shared._Trauma.Hands;
using Content.Shared.Explosion.Components;
using Content.Shared.Throwing;
using Content.Shared.Trigger;
using Content.Shared.Trigger.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Grenades;

/// <summary>
///     Has an NPC throw a grenade. It all happens at once rather than as a chain of tasks, and the fuse is only lit once the
///         grenade has left its hand, so nothing can leave a live grenade in its hand or back in its belt. When it goes off,
///         the thrower and the squadmates it tells go in on the spot (<see cref="NpcGrenadePusherComponent"/>). HTN does the
///         going. One that cooks its frags (<see cref="NpcGrenadeThrowerComponent.Cooks"/>) is the exception that lights it
///         first and holds it: see <c>NpcGrenadeSystem.Cooking.cs</c>.
/// </summary>
public sealed partial class NpcGrenadeSystem : EntitySystem
{
    [Dependency] private HandsSystem _handsSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private NpcHandsSystem _npcHandsSystem = default!;
    [Dependency] private NpcSensorSystem _npcSensorSystem = default!;
    [Dependency] private NpcSquadSystem _npcSquadSystem = default!;
    [Dependency] private NpcSquadTacticsSystem _npcSquadTacticsSystem = default!;
    [Dependency] private NpcVoiceSystem _npcVoiceSystem = default!;
    [Dependency] private PredictedHandsSystem _predictedHandsSystem = default!;
    [Dependency] private TriggerSystem _triggerSystem = default!;

    [Dependency] private EntityQuery<ExplosiveComponent> _explosiveQuery = default!;
    [Dependency] private EntityQuery<NpcGrenadeCookComponent> _grenadeCookQuery = default!;
    [Dependency] private EntityQuery<NpcGrenadePusherComponent> _grenadePusherQuery = default!;
    [Dependency] private EntityQuery<NpcGrenadePushComponent> _grenadePushQuery = default!;
    [Dependency] private EntityQuery<NpcGrenadeThrowerComponent> _grenadeThrowerQuery = default!;
    [Dependency] private EntityQuery<NpcThrownGrenadeComponent> _thrownGrenadeQuery = default!;
    [Dependency] private EntityQuery<ThrownItemComponent> _thrownItemQuery = default!;

    /// <summary>
    ///     Takes <paramref name="grenadeUid"/> out, throws it at <paramref name="coordinates"/>, lights it, and gets the
    ///         weapon back in hand. A throw that does not happen leaves the grenade unlit, back where it was.
    /// </summary>
    public bool TryThrow(EntityUid uid, EntityUid grenadeUid, EntityCoordinates coordinates)
    {
        if (!_npcHandsSystem.TryTakeOut(uid, grenadeUid, out var takenOut))
            return false;

        var thrown = _predictedHandsSystem.ThrowHeldItem(uid, coordinates) && !_handsSystem.IsHolding(uid, grenadeUid);

        if (thrown)
        {
            var thrownGrenadeComponent = EnsureComp<NpcThrownGrenadeComponent>(grenadeUid);
            thrownGrenadeComponent.ThrowerUid = uid;
            thrownGrenadeComponent.Coordinates = coordinates;

            _triggerSystem.ActivateTimerTrigger(grenadeUid, uid);

            // Said as it goes, by a voice that says it: what it is, so the squad knows whether to look away or get down.
            _npcVoiceSystem.TrySpeakCallout(uid, _explosiveQuery.HasComp(grenadeUid) ? NpcCallout.ThrowFrag : NpcCallout.ThrowOther);
        }

        _npcHandsSystem.PutBack(uid, takenOut);
        return thrown;
    }

    /// <summary>
    ///     Where <paramref name="grenadeUid"/> is coming down, if it is a grenade an NPC threw and it is still in the air, or
    ///         one it is cooking (<see cref="NpcGrenadeThrowerComponent.Cooks"/>) for a throw it has yet to make.
    /// </summary>
    public bool TryGetLanding(EntityUid grenadeUid, out EntityCoordinates coordinates)
    {
        coordinates = default;

        if (!_thrownGrenadeQuery.TryComp(grenadeUid, out var thrownGrenadeComponent))
            return false;

        // In the air, or still in the thrower's hand while it is cooked for that throw: either way it is coming down there.
        // A cooked one is in a container, so the blast tile dangers (PathfindingSystem.Klovn.Blast.cs) leave it out until it
        //   is thrown, and then it is in the air: the squad is warned of where it lands from the moment the cook starts
        //   (TargetDangerRangeCon, grenade.yml) and goes round it from the moment it leaves the hand.
        if (!_thrownItemQuery.HasComp(grenadeUid) &&
            !(_grenadeCookQuery.TryComp(thrownGrenadeComponent.ThrowerUid, out var cookComponent) && cookComponent.GrenadeUid == grenadeUid))
            return false;

        coordinates = thrownGrenadeComponent.Coordinates;
        return true;
    }

    /// <summary>
    ///     Sends <paramref name="entity"/> in on <paramref name="coordinates"/>, in place of anything it was already going in
    ///         on. Does nothing for an NPC without <see cref="NpcGrenadePusherComponent"/>.
    /// </summary>
    public void StartPush(Entity<NpcGrenadePusherComponent?> entity, EntityCoordinates coordinates)
    {
        if (!_grenadePusherQuery.Resolve(entity.Owner, ref entity.Comp, logMissing: false))
            return;

        var grenadePushComponent = EnsureComp<NpcGrenadePushComponent>(entity.Owner);
        grenadePushComponent.Coordinates = coordinates;
        grenadePushComponent.EndTime = _gameTiming.CurTime + entity.Comp.Duration;

        _npcSensorSystem.RequestReplan(entity.Owner);
    }

    /// <summary>
    ///     Where <paramref name="uid"/> should be going in on right now, if anywhere.
    /// </summary>
    public bool TryGetPush(EntityUid uid, out EntityCoordinates coordinates)
    {
        coordinates = default;

        if (!_grenadePushQuery.TryComp(uid, out var grenadePushComponent) || _gameTiming.CurTime >= grenadePushComponent.EndTime)
            return false;

        coordinates = grenadePushComponent.Coordinates;
        return true;
    }

    /// <summary>
    ///     <paramref name="uid"/> has got where it was going. Does nothing for one that was not.
    /// </summary>
    public void StopPush(EntityUid uid)
    {
        RemComp<NpcGrenadePushComponent>(uid);
    }

    [SubscribeLocalEvent]
    private void OnThrownGrenadeTriggered(Entity<NpcThrownGrenadeComponent> entity, ref TriggerEvent args)
    {
        var throwerUid = entity.Comp.ThrowerUid;
        var coordinates = entity.Comp.Coordinates;

        // Deferred: the rest of the grenade's trigger effects are still being raised.
        RemCompDeferred<NpcThrownGrenadeComponent>(entity);

        if (!Exists(throwerUid))
            return;

        StartPush(throwerUid, coordinates);

        // A squad that pushes in a fight may go in on a hostile caught by it.
        _npcSquadTacticsSystem.NoteBlast(throwerUid, coordinates);

        if (!_grenadeThrowerQuery.TryComp(throwerUid, out var grenadeThrowerComponent) || !grenadeThrowerComponent.CallsOutBlasts)
            return;

        var ev = new NpcGrenadeBlastCalloutEvent(throwerUid, coordinates);
        _npcSquadSystem.CallOut(throwerUid, ref ev);
    }

    /// <summary>
    ///     Picked up - caught, or a dud taken off the floor - it is no longer the throw the NPC made: thrown again, it is not
    ///         coming down where the NPC threw it, and going off, it is not the NPC's blast to push in on.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnThrownGrenadeInserted(Entity<NpcThrownGrenadeComponent> entity, ref EntGotInsertedIntoContainerMessage args)
    {
        RemCompDeferred<NpcThrownGrenadeComponent>(entity);
    }

    [SubscribeLocalEvent]
    private void OnBlastCallout(Entity<NpcGrenadePusherComponent> entity, ref NpcGrenadeBlastCalloutEvent args)
    {
        StartPush((entity.Owner, entity.Comp), args.Coordinates);
    }
}
