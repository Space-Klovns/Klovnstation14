using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Squad;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.CombatMode;
using Content.Shared.Interaction;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Suppression;

/// <summary>
///     Has an NPC fire at a spot rather than a target: where a hostile just went, to keep its head down. NPC ranged combat
///         only shoots at entities, so this is a path of its own. It only ever loops over NPCs that are firing right now.
///     <para>
///         Each round keeps off friends as ranged combat's do (<see cref="NpcSquadFireLaneSystem.TryGetClearShot"/>): at the
///             spot, or else as near it as is clear, anywhere the hostile could be by now (<see cref="GetSpotRadius"/>); with
///             nowhere clear, not at all.
///     </para>
/// </summary>
public sealed partial class NpcSuppressiveFireSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private GunSystem _gunSystem = default!;
    [Dependency] private NpcPerceptionSystem _npcPerceptionSystem = default!;
    [Dependency] private NpcSquadFireLaneSystem _npcSquadFireLaneSystem = default!;
    [Dependency] private RotateToFaceSystem _rotateToFaceSystem = default!;
    [Dependency] private SharedCombatModeSystem _sharedCombatModeSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<NpcSuppressiveFireComponent> _suppressiveFireQuery = default!;

    /// <summary>
    ///     How close to facing the spot the NPC has to be to fire, as for a target in NPC ranged combat.
    /// </summary>
    private static readonly Angle AimTolerance = Angle.FromDegrees(30);

    /// <summary>
    ///     How near the spot a hostile that just ducked out of sight is at the least, in tiles: a tile about the corner it went
    ///         round, not on a point, even if it was standing still.
    /// </summary>
    public const float MinSpotRadius = 1f;

    /// <summary>
    ///     How far from the spot it could be at the most, in tiles: beyond that, fire there is not keeping it down.
    /// </summary>
    public const float MaxSpotRadius = 2.5f;

    /// <summary>
    ///     How long it is taken to keep going the way it was going, out of sight, before it would have stopped.
    /// </summary>
    private const float SpotGrowthTime = 1.5f;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _gameTiming.CurTime;

        var enumerator = EntityQueryEnumerator<NpcSuppressiveFireComponent, TransformComponent>();
        while (enumerator.MoveNext(out var uid, out var suppressiveFireComponent, out var transformComponent))
        {
            // Or the spot went with whatever it was on, say a shuttle that left: it would only fire at the middle of the map.
            //      Or the hostile it is keeping down is gone from the world altogether: nobody is there to keep down.
            if (now >= suppressiveFireComponent.EndTime ||
                !suppressiveFireComponent.Coordinates.IsValid(EntityManager) ||
                suppressiveFireComponent.TargetUid is { } targetUid && TerminatingOrDeleted(targetUid) ||
                !_gunSystem.TryGetGun(uid, out var gunEntity))
            {
                Stop(uid);
                continue;
            }

            var worldPosition = _transformSystem.GetMapCoordinates(transformComponent);
            var targetPosition = _transformSystem.ToMapCoordinates(suppressiveFireComponent.Coordinates);
            var goalRotation = (targetPosition.Position - worldPosition.Position).ToWorldAngle();

            if (!_rotateToFaceSystem.TryRotateTo(uid, goalRotation, frameTime, AimTolerance, double.MaxValue, transformComponent) ||
                !_gunSystem.CanShoot(gunEntity.Comp) ||
                gunEntity.Comp.NextFire > now)
                continue;

            var spotRadius = GetSpotRadius(uid, suppressiveFireComponent.TargetUid, now);

            if (_npcSquadFireLaneSystem.TryGetClearShot(uid, worldPosition, targetPosition, spotRadius, out var shot))
                _gunSystem.AttemptShoot(uid, gunEntity, _transformSystem.ToCoordinates(shot));
        }
    }

    /// <summary>
    ///     How far from the spot, in tiles, a round still does the job, for <paramref name="uid"/> keeping
    ///         <paramref name="targetUid"/> down: wherever it could be by now. As far as it could have gone at the speed it was
    ///         going since it was last seen, within <see cref="MinSpotRadius"/> and <see cref="MaxSpotRadius"/>: the spot is
    ///         where it went, not where it is, even if it has since come back into sight. 0, the spot itself, for no hostile,
    ///         or one this NPC knows nothing of.
    /// </summary>
    public float GetSpotRadius(EntityUid uid, EntityUid? targetUid, TimeSpan now)
    {
        if (targetUid is not { } hostileUid || !_npcPerceptionSystem.TryGetContact(uid, hostileUid, out var contact))
            return 0f;

        var bodyRadius = _npcSquadFireLaneSystem.GetBodyRadius(hostileUid);
        var gone = MathF.Min((float) (now - contact.LastSeen).TotalSeconds, SpotGrowthTime);
        return Math.Clamp(bodyRadius + contact.LastKnownVelocity.Length() * MathF.Max(gone, 0f), MinSpotRadius, MaxSpotRadius);
    }

    /// <summary>
    ///     Whether <paramref name="uid"/> is firing at a spot.
    /// </summary>
    public bool IsFiring(EntityUid uid)
    {
        return _suppressiveFireQuery.HasComp(uid);
    }

    /// <summary>
    ///     Has <paramref name="uid"/> fire at <paramref name="coordinates"/> for <paramref name="duration"/>: where
    ///         <paramref name="targetUid"/> went, if given, so anywhere it could be by now does as well where a friend is in the
    ///         way; or the spot itself, with none.
    /// </summary>
    public void Start(EntityUid uid, EntityCoordinates coordinates, TimeSpan duration, EntityUid? targetUid = null)
    {
        var suppressiveFireComponent = EnsureComp<NpcSuppressiveFireComponent>(uid);
        suppressiveFireComponent.Coordinates = coordinates;
        suppressiveFireComponent.EndTime = _gameTiming.CurTime + duration;
        suppressiveFireComponent.TargetUid = targetUid;

        _sharedCombatModeSystem.SetInCombatMode(uid, true);
    }

    /// <summary>
    ///     Stops <paramref name="uid"/> firing. Does nothing for one that is not.
    /// </summary>
    public void Stop(EntityUid uid)
    {
        if (!IsFiring(uid))
            return;

        // Deferred: this is also called from inside Update's enumerator.
        RemCompDeferred<NpcSuppressiveFireComponent>(uid);
        _sharedCombatModeSystem.SetInCombatMode(uid, false);
    }
}
