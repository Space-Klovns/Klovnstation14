using Content.Shared._KS14.SupplyPod;
using Content.Shared._KS14.Trail;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Random;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Server._KS14.SupplyPod;

/// <summary>
///     Kept you waiting, huh?
/// </summary>
public sealed partial class SupplyPodSystem : SharedSupplyPodSystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private AudioSystem _audioSystem = default!;
    [Dependency] private TransformSystem _transformSystem = default!;
    [Dependency] private AppearanceSystem _appearanceSystem = default!;
    [Dependency] private KsTrailSystem _trailSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SupplyPodComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SupplyPodComponent, ComponentShutdown>(OnShutdown);
    }

    /// <summary>
    ///     A pod that dies before it lands (admin deletion, gibbed grid, whatever) would otherwise
    ///         leave its trail hanging in the air forever, since nothing else knows about it.
    /// </summary>
    private void OnShutdown(Entity<SupplyPodComponent> entity, ref ComponentShutdown args)
    {
        ReleaseTrail(entity.Comp);
    }

    /// <summary>
    ///     Hands the trail off to its own death timer and forgets about it. The pod is usually
    ///         deleted the moment it lands, so the trail has to outlive it on its own.
    /// </summary>
    private void ReleaseTrail(SupplyPodComponent supplyPodComponent)
    {
        if (supplyPodComponent.TrailEntity is not { } trailUid)
            return;

        supplyPodComponent.TrailEntity = null;

        if (TerminatingOrDeleted(trailUid))
            return;

        _trailSystem.StartFade(trailUid, _gameTiming.CurTime);
        EnsureComp<TimedDespawnComponent>(trailUid).Lifetime = (float)supplyPodComponent.TrailLingerDuration.TotalSeconds;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _gameTiming.CurTime;
        var eqe = EntityQueryEnumerator<ActiveSupplyPodComponent, SupplyPodComponent>();

        while (eqe.MoveNext(out var uid, out var activeSupplyPodComponent, out var supplyPodComponent))
        {
            // Pre-impact
            if (curTime >= activeSupplyPodComponent.FallSoundTime)
            {
                _audioSystem.PlayPvs(
                    supplyPodComponent.FallSound,
                    activeSupplyPodComponent.DestinationCoordinates
                );

                activeSupplyPodComponent.FallSoundTime = TimeSpan.MaxValue;
                Dirty(uid, activeSupplyPodComponent);
            }

            if (curTime < activeSupplyPodComponent.LaunchFinishTime)
                continue;

            // Impact

            activeSupplyPodComponent.LaunchFinishTime = TimeSpan.MaxValue;
            _audioSystem.PlayPvs(
                supplyPodComponent.ImpactSound,
                activeSupplyPodComponent.DestinationCoordinates
            );

            _appearanceSystem.SetData(uid, SupplyPodVisuals.Landed, true);

            ReleaseTrail(supplyPodComponent);

            RemComp(uid, activeSupplyPodComponent);
        }
    }

    private void OnMapInit(Entity<SupplyPodComponent> entity, ref MapInitEvent args)
    {
        var curTime = _gameTiming.CurTime;
        var transformComponent = Transform(entity);

        var activeComponent = EnsureComp<ActiveSupplyPodComponent>(entity.Owner);
        activeComponent.DestinationCoordinates = transformComponent.Coordinates;
        activeComponent.LaunchFinishTime = curTime + entity.Comp.FallDuration;
        activeComponent.FallSoundTime = curTime + entity.Comp.FallSoundDelay;
        activeComponent.Angle = _robustRandom.NextAngle(-entity.Comp.AngularDeviation, entity.Comp.AngularDeviation);
        Dirty(entity.Owner, activeComponent);

        _transformSystem.SetLocalRotation(entity.Owner, activeComponent.Angle, xform: transformComponent);
        _appearanceSystem.SetData(entity.Owner, SupplyPodVisuals.Landed, false);

        SpawnTrail(entity, activeComponent, transformComponent, curTime);
    }

    /// <summary>
    ///     Lays the trail out along the exact axis the client animates the pod down, so the pod
    ///         appears to carve it out as it falls.
    /// </summary>
    private void SpawnTrail(
        Entity<SupplyPodComponent> entity,
        ActiveSupplyPodComponent activeComponent,
        TransformComponent transformComponent,
        TimeSpan curTime)
    {
        if (entity.Comp.TrailProto is not { } trailProto)
            return;

        // Spawning on the pod's own coordinates parents the trail to whatever the pod is on,
        // so it rides the grid instead of hanging in worldspace.
        var trailUid = Spawn(trailProto, transformComponent.Coordinates);
        _transformSystem.SetLocalRotation(trailUid, activeComponent.Angle);

        if (!TryComp<KsTrailComponent>(trailUid, out var trailComponent))
        {
            Log.Error($"Supply pod trail prototype '{trailProto}' has no {nameof(KsTrailComponent)}.");
            Del(trailUid);
            return;
        }

        trailComponent.Length = (int)MathF.Ceiling(entity.Comp.Height / trailComponent.Spacing);
        trailComponent.RevealStartTime = curTime;
        trailComponent.RevealDuration = entity.Comp.FallDuration;

        // Pointing the trail at the pod itself makes the reveal track the pod's animated position
        // rather than a schedule, so the two stay locked together however late the client starts
        // animating the descent.
        trailComponent.SourceEntity = entity.Owner;

        Dirty(trailUid, trailComponent);

        entity.Comp.TrailEntity = trailUid;
    }
}
