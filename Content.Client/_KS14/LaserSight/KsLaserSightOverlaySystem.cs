using System.Numerics;
using Content.Shared._KS14.IoC;
using Content.Shared._KS14.LaserSight;
using Content.Shared._KS14.LockOn;
using Content.Shared.Physics;
using Robust.Client.Graphics;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Client._KS14.LaserSight;

/// <summary>
///     One laser beam, resolved for this frame. Shared by the world pass and the emissive pass so each entity is
///         only raycast once per frame.
/// </summary>
public readonly record struct KsLaserSegment(
    EntityUid Source,
    MapId MapId,
    Vector2 Origin,
    Angle Rotation,
    float Length,
    Color Color,
    float Width,
    float GlowWidth,
    float GlowAlpha,
    float Intensity,
    float MaxLength,
    float FadeStart);

/// <summary>
///     Resolves every visible laser sight once per frame and registers the overlays that draw them.
/// </summary>
public sealed partial class KsLaserSightOverlaySystem : EntitySystem
{
    /// <summary>
    ///     A cached raycast is reused for at most this long, because doors and walls can change under a still beam.
    /// </summary>
    private static readonly TimeSpan RaycastReuseTime = TimeSpan.FromSeconds(0.1);

    /// <summary>
    ///     A cached raycast is reused while the origin moved less than this (metres)...
    /// </summary>
    private const float RaycastReuseOriginEpsilon = 0.02f;

    /// <summary>
    ///     ...and the direction vector differs by less than this.
    /// </summary>
    private const float RaycastReuseDirectionEpsilon = 0.005f;

    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private SystemCollectionHookManager _systemCollectionHookManager = default!;
    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private EntityQuery<KsTargetLockOnComponent> _lockOnQuery = default!;
    [Dependency] private EntityQuery<KsLaserSightClientStateComponent> _clientStateQuery = default!;

    private readonly List<KsLaserSegment> _segments = [];

    /// <summary>
    ///     The beams to draw this frame, on the eye's map and every other map (z-level) near the eye's x/y.
    /// </summary>
    public IReadOnlyList<KsLaserSegment> Segments => _segments;

    public override void Initialize()
    {
        base.Initialize();

        _systemCollectionHookManager.HookAction(OnDependenciesReady);
    }

    private void OnDependenciesReady(IDependencyCollection dependencyCollection)
    {
        var worldOverlay = new KsLaserSightOverlay();
        dependencyCollection.InjectDependencies(worldOverlay, oneOff: true);
        _overlayManager.AddOverlay(worldOverlay);

        var emissiveOverlay = new KsLaserSightEmissiveOverlay();
        dependencyCollection.InjectDependencies(emissiveOverlay, oneOff: true);
        _overlayManager.AddOverlay(emissiveOverlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlayManager.RemoveOverlay<KsLaserSightOverlay>();
        _overlayManager.RemoveOverlay<KsLaserSightEmissiveOverlay>();
    }

    /// <summary>
    ///     Whether any beam is on <paramref name="mapId"/>. The z-level renderer runs every overlay once per level map
    ///         with that map's depth-scaled eye, so each overlay draws only the segments of the map it was called for.
    /// </summary>
    public bool HasSegmentsOn(MapId mapId)
    {
        for (var i = 0; i < _segments.Count; i++)
        {
            if (_segments[i].MapId == mapId)
                return true;
        }

        return false;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        _segments.Clear();

        var eye = _eyeManager.CurrentEye;
        var eyeMapId = eye.Position.MapId;
        if (eyeMapId == MapId.Nullspace)
            return;

        // Lasers on other z-level maps sit at the same x/y as the eye, so the main eye's x/y bounds serve them too.
        var viewBounds = _eyeManager.GetWorldViewport();
        var curTime = _gameTiming.CurTime;

        var enumerator = EntityQueryEnumerator<KsLaserSightComponent, TransformComponent>();
        while (enumerator.MoveNext(out var uid, out var laserComponent, out var transformComponent))
        {
            // Any map: a z-level pass draws other levels' maps, at the same x/y as the eye.
            if (!laserComponent.Enabled)
                continue;

            if (laserComponent.Color.A <= 0f)
                continue;

            if (laserComponent.BlinkWhileLocking
                && _lockOnQuery.TryComp(uid, out var lockOnComponent)
                && lockOnComponent.Target != null
                && curTime < lockOnComponent.LockEndTime)
            {
                // Square wave, on for the first half of each period.
                var phase = (float)(curTime.TotalSeconds * laserComponent.BlinkFrequency);
                if (phase - MathF.Floor(phase) >= 0.5f)
                    continue;
            }

            var (worldPosition, worldRotation) = _transformSystem.GetWorldPositionRotation(transformComponent);
            var origin = KsLaserSightMath.GetOrigin(worldPosition, worldRotation, laserComponent.Offset);

            // The beam cannot reach the screen unless its origin is within MaxLength of the view.
            if (!viewBounds.Enlarged(laserComponent.MaxLength).Contains(origin))
                continue;

            var beamRotation = worldRotation + laserComponent.AngleOffset;
            var length = laserComponent.MaxLength;

            if (laserComponent.StopAtObstacles && length > 0f)
            {
                var direction = KsLaserSightMath.GetDirection(worldRotation, laserComponent.AngleOffset);
                length = ResolveLength(
                    uid,
                    transformComponent.MapID,
                    origin,
                    direction,
                    laserComponent.MaxLength,
                    curTime);
            }

            if (length <= 0f)
                continue;

            _segments.Add(new KsLaserSegment(
                uid,
                transformComponent.MapID,
                origin,
                beamRotation,
                length,
                laserComponent.Color,
                laserComponent.Width,
                laserComponent.GlowWidth,
                laserComponent.GlowAlpha,
                laserComponent.Intensity,
                laserComponent.MaxLength,
                laserComponent.FadeStart));
        }
    }

    /// <summary>
    ///     The beam length cut by the nearest obstacle, reusing the entity's cached raycast while it is still good.
    /// </summary>
    private float ResolveLength(
        EntityUid uid,
        MapId mapId,
        Vector2 origin,
        Vector2 direction,
        float maxLength,
        TimeSpan curTime)
    {
        if (!_clientStateQuery.TryComp(uid, out var stateComponent))
            stateComponent = AddComp<KsLaserSightClientStateComponent>(uid);

        if (stateComponent.Valid
            && stateComponent.MapId == mapId
            && stateComponent.MaxLength == maxLength
            && curTime - stateComponent.ResolvedAt < RaycastReuseTime
            && Vector2.DistanceSquared(stateComponent.Origin, origin) < RaycastReuseOriginEpsilon * RaycastReuseOriginEpsilon
            && Vector2.DistanceSquared(stateComponent.Direction, direction) < RaycastReuseDirectionEpsilon * RaycastReuseDirectionEpsilon)
        {
            return stateComponent.Length;
        }

        var length = maxLength;
        var ray = new CollisionRay(origin, direction, (int)CollisionGroup.Opaque);
        // returnOnFirstHit: true would return whichever fixture the broadphase tree visits first, not the
        //      nearest, so the beam could run on past a closer obstacle. Take the minimum of every hit.
        foreach (var result in _physicsSystem.IntersectRay(
                     mapId,
                     ray,
                     length,
                     ignoredEnt: uid,
                     returnOnFirstHit: false))
        {
            length = MathF.Min(length, result.Distance);
        }

        stateComponent.MapId = mapId;
        stateComponent.Origin = origin;
        stateComponent.Direction = direction;
        stateComponent.MaxLength = maxLength;
        stateComponent.Length = length;
        stateComponent.ResolvedAt = curTime;
        stateComponent.Valid = true;

        return length;
    }

    /// <summary>
    ///     The beam as a quad extending along the facing direction from its origin.
    /// </summary>
    public static Box2Rotated GetBeamBox(in KsLaserSegment segment, float width)
    {
        return KsLaserSightMath.GetBeamBox(segment.Origin, segment.Rotation, segment.Length, width);
    }
}
