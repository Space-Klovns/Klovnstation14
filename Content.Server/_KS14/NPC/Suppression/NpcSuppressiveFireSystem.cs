using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.CombatMode;
using Content.Shared.Interaction;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Suppression;

/// <summary>
///     Has an NPC fire at a spot rather than a target: where a hostile just went, to keep its head down. NPC ranged combat
///         only shoots at entities, so this is a path of its own. It only ever loops over NPCs that are firing right now.
/// </summary>
public sealed partial class NpcSuppressiveFireSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private GunSystem _gunSystem = default!;
    [Dependency] private RotateToFaceSystem _rotateToFaceSystem = default!;
    [Dependency] private SharedCombatModeSystem _sharedCombatModeSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<NpcSuppressiveFireComponent> _suppressiveFireQuery = default!;

    /// <summary>
    ///     How close to facing the spot the NPC has to be to fire, as for a target in NPC ranged combat.
    /// </summary>
    private static readonly Angle AimTolerance = Angle.FromDegrees(30);

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _gameTiming.CurTime;

        var enumerator = EntityQueryEnumerator<NpcSuppressiveFireComponent, TransformComponent>();
        while (enumerator.MoveNext(out var uid, out var suppressiveFireComponent, out var transformComponent))
        {
            // Or the spot went with whatever it was on, say a shuttle that left: it would only fire at the middle of the map.
            if (now >= suppressiveFireComponent.EndTime ||
                !suppressiveFireComponent.Coordinates.IsValid(EntityManager) ||
                !_gunSystem.TryGetGun(uid, out var gunEntity))
            {
                Stop(uid);
                continue;
            }

            var worldPosition = _transformSystem.GetWorldPosition(transformComponent);
            var targetPosition = _transformSystem.ToMapCoordinates(suppressiveFireComponent.Coordinates).Position;
            var goalRotation = (targetPosition - worldPosition).ToWorldAngle();

            if (_rotateToFaceSystem.TryRotateTo(uid, goalRotation, frameTime, AimTolerance, double.MaxValue, transformComponent) &&
                _gunSystem.CanShoot(gunEntity.Comp))
                _gunSystem.AttemptShoot(uid, gunEntity, suppressiveFireComponent.Coordinates);
        }
    }

    /// <summary>
    ///     Whether <paramref name="uid"/> is firing at a spot.
    /// </summary>
    public bool IsFiring(EntityUid uid)
    {
        return _suppressiveFireQuery.HasComp(uid);
    }

    /// <summary>
    ///     Has <paramref name="uid"/> fire at <paramref name="coordinates"/> for <paramref name="duration"/>.
    /// </summary>
    public void Start(EntityUid uid, EntityCoordinates coordinates, TimeSpan duration)
    {
        var suppressiveFireComponent = EnsureComp<NpcSuppressiveFireComponent>(uid);
        suppressiveFireComponent.Coordinates = coordinates;
        suppressiveFireComponent.EndTime = _gameTiming.CurTime + duration;

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
