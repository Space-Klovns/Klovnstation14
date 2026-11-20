using System.Numerics;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Damage.Systems;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.MedicalBeam;

/// <summary>
///     Works <see cref="KsMedicalBeamGunComponent"/>. Firing never shoots: the attempt is cancelled on both sides, and
///         the server locks the beam onto the living mob clicked, friend or not, or lets it go if there is none. The
///         server heals and checks range and sight; the beam is networked for the clients to draw. Two beams that
///         cross both stop, and <see cref="KsMedicalBeamsCrossedEvent"/> sets off the explosion on the server.
/// </summary>
/// <remarks>
///     Ported from tgstation's <c>code/modules/projectiles/guns/special/medbeam.dm</c> at
///         e6c9da8511c11d6bb696a306685885e972a079ca. Crossing is tested on the holder-to-target segments rather than
///         tg's shared tiles. Left out: the mech-mounted version, and the explosion's flash (<c>flash_range = 8</c>).
/// </remarks>
public sealed partial class KsMedicalBeamSystem : EntitySystem
{
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private ExamineSystemShared _examineSystem = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private EntityQuery<MobStateComponent> _mobStateQuery = default!;

    private readonly HashSet<Entity<MobStateComponent>> _candidates = new();

    [SubscribeLocalEvent]
    private void OnShotAttempted(Entity<KsMedicalBeamGunComponent> gun, ref ShotAttemptedEvent args)
    {
        // It never shoots, on either side.
        args.Cancel();

        if (_netManager.IsClient)
            return;

        var gunComponent = args.Used.Comp;
        var targetUid = PickTarget(gun, args.User, gunComponent);

        // Fired at no one it can beam: let go, quietly (tg's process_fire).
        if (targetUid == null)
        {
            Stop(gun, lost: false);
            return;
        }

        // At the one it is on already: it stays on.
        if (targetUid == gun.Comp.Target)
            return;

        // At another: only once the last one has had its moment (KsMedicalBeamGunComponent.RetargetCooldown).
        var curTime = _gameTiming.CurTime;
        if (curTime < gun.Comp.NextRetarget)
            return;

        gun.Comp.NextRetarget = curTime + gun.Comp.RetargetCooldown;
        Start(gun, args.User, targetUid.Value);
    }

    /// <summary>
    ///     Whether the beam can be on this target for this user: any living mob, friend or not (tg's <c>isliving</c>),
    ///         in range and in sight.
    /// </summary>
    public bool CanBeam(Entity<KsMedicalBeamGunComponent> gun, EntityUid userUid, EntityUid targetUid)
    {
        return targetUid != userUid &&
               _mobStateQuery.TryComp(targetUid, out var mobStateComponent) &&
               mobStateComponent.CurrentState != MobState.Dead &&
               _examineSystem.InRangeUnOccluded(userUid, targetUid, gun.Comp.MaxRange);
    }

    /// <summary>
    ///     The mob clicked if the beam can go on it, and none if it can't: a click on a mob is never passed to one beside
    ///         it. A click on no mob takes the nearest mob it can beam close to it.
    /// </summary>
    private EntityUid? PickTarget(Entity<KsMedicalBeamGunComponent> gun, EntityUid userUid, GunComponent gunComponent)
    {
        if (gunComponent.Target is { } clickedUid && _mobStateQuery.HasComp(clickedUid))
            return CanBeam(gun, userUid, clickedUid) ? clickedUid : null;

        if (gunComponent.ShootCoordinates is not { } coordinates)
            return null;

        var mapCoordinates = _transformSystem.ToMapCoordinates(coordinates);
        _candidates.Clear();
        _entityLookupSystem.GetEntitiesInRange(mapCoordinates, gun.Comp.AimAssistRadius, _candidates);

        EntityUid? bestUid = null;
        var bestDistance = float.MaxValue;
        foreach (var candidate in _candidates)
        {
            if (!CanBeam(gun, userUid, candidate.Owner))
                continue;

            var distance = (_transformSystem.GetWorldPosition(candidate.Owner) - mapCoordinates.Position).LengthSquared();
            if (distance >= bestDistance)
                continue;

            bestUid = candidate.Owner;
            bestDistance = distance;
        }

        _candidates.Clear();
        return bestUid;
    }

    private void Start(Entity<KsMedicalBeamGunComponent> gun, EntityUid userUid, EntityUid targetUid)
    {
        gun.Comp.Target = targetUid;
        gun.Comp.User = userUid;
        gun.Comp.NextTick = _gameTiming.CurTime + gun.Comp.TickInterval;
        Dirty(gun);

        _audioSystem.PlayPvs(gun.Comp.StartSound, gun.Owner);
        _popupSystem.PopupEntity(KsPopupMessage.Create("ks-medical-beam-locked", ("target", targetUid)), userUid, userUid);
    }

    /// <summary>
    ///     Turns the beam off. With <paramref name="lost"/> the holder is warned, as when sight or range is lost; put
    ///         down, it just stops (tg's <c>LoseTarget</c> and <c>beam_died</c>).
    /// </summary>
    public void Stop(Entity<KsMedicalBeamGunComponent> gun, bool lost)
    {
        if (gun.Comp.Target == null)
            return;

        var userUid = gun.Comp.User;
        gun.Comp.Target = null;
        gun.Comp.User = null;
        Dirty(gun);

        if (!lost || userUid is not { } uid || TerminatingOrDeleted(uid))
            return;

        _audioSystem.PlayPvs(gun.Comp.LostSound, gun.Owner);
        _popupSystem.PopupEntity(KsPopupMessage.Create("ks-medical-beam-lost"), uid, uid, PopupType.MediumCaution);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_netManager.IsClient)
            return;

        var curTime = _gameTiming.CurTime;
        var query = EntityQueryEnumerator<KsMedicalBeamGunComponent>();
        while (query.MoveNext(out var uid, out var beamComponent))
        {
            if (beamComponent.Target is not { } targetUid)
                continue;

            var gun = new Entity<KsMedicalBeamGunComponent>(uid, beamComponent);

            // Put down, or held by someone else now: it just stops.
            if (beamComponent.User is not { } userUid || !_handsSystem.IsHolding(userUid, uid))
            {
                Stop(gun, lost: false);
                continue;
            }

            if (TerminatingOrDeleted(targetUid) || !CanBeam(gun, userUid, targetUid))
            {
                Stop(gun, lost: true);
                continue;
            }

            if (curTime < beamComponent.NextTick)
                continue;

            beamComponent.NextTick = curTime + beamComponent.TickInterval;

            // Checked when it heals, as tg's los_check is (mid_los_check), so a beam swept across another for a moment
            //     can get away with it.
            if (TryCross(gun, userUid, targetUid))
                continue;

            // The sparkle only on someone hurt, as tg's only shows while there is something to heal.
            if (beamComponent.HealEffect is { } healEffect && _damageableSystem.GetTotalDamage(targetUid) > FixedPoint2.Zero)
                Spawn(healEffect, new EntityCoordinates(targetUid, Vector2.Zero));

            foreach (var (group, amount) in beamComponent.Heal)
                _damageableSystem.HealEvenly(targetUid, -amount, group, origin: userUid);
        }
    }

    /// <summary>
    ///     Whether this beam crosses another, held by someone else; if so both stop, and the crossing is raised on this
    ///         gun for the server to blow up.
    /// </summary>
    private bool TryCross(Entity<KsMedicalBeamGunComponent> gun, EntityUid userUid, EntityUid targetUid)
    {
        var userCoordinates = _transformSystem.GetMapCoordinates(userUid);
        var targetCoordinates = _transformSystem.GetMapCoordinates(targetUid);
        if (userCoordinates.MapId != targetCoordinates.MapId)
            return false;

        var otherQuery = EntityQueryEnumerator<KsMedicalBeamGunComponent>();
        while (otherQuery.MoveNext(out var otherUid, out var otherComponent))
        {
            if (otherUid == gun.Owner ||
                otherComponent.Target is not { } otherTargetUid ||
                otherComponent.User is not { } otherUserUid ||
                otherUserUid == userUid || // tg: beams from one origin never cross
                TerminatingOrDeleted(otherTargetUid) ||
                TerminatingOrDeleted(otherUserUid))
                continue;

            var otherUserCoordinates = _transformSystem.GetMapCoordinates(otherUserUid);
            var otherTargetCoordinates = _transformSystem.GetMapCoordinates(otherTargetUid);
            if (otherUserCoordinates.MapId != userCoordinates.MapId || otherTargetCoordinates.MapId != userCoordinates.MapId)
                continue;

            if (!KsMedicalBeamMath.TryGetCrossing(userCoordinates.Position, targetCoordinates.Position,
                    otherUserCoordinates.Position, otherTargetCoordinates.Position,
                    sharedTarget: otherTargetUid == targetUid, out var crossing))
                continue;

            var other = new Entity<KsMedicalBeamGunComponent>(otherUid, otherComponent);
            Stop(gun, lost: true);
            Stop(other, lost: true);

            var ev = new KsMedicalBeamsCrossedEvent(otherUid, userUid, otherUserUid, new MapCoordinates(crossing, userCoordinates.MapId));
            RaiseLocalEvent(gun, ref ev);
            return true;
        }

        return false;
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsMedicalBeamGunComponent> gun, ref ComponentShutdown args)
    {
        if (_netManager.IsServer && !TerminatingOrDeleted(gun.Owner))
            Stop(gun, lost: false);
    }
}
