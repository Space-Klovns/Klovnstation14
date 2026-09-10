using Content.Shared._KS14.SupplyPod;
using Content.Shared._KS14.Trail;
using Content.Shared.Lock;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
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
    [Dependency] private LockSystem _lockSystem = default!;
    [Dependency] private SharedEntityStorageSystem _entityStorageSystem = default!;

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

            // A pod that finished rising is at the top of its arc, not on the ground. It turns
            // around up there and comes straight back down onto its dropoff.
            if (activeSupplyPodComponent.Ascending)
            {
                BeginDescent((uid, supplyPodComponent), activeSupplyPodComponent, curTime);
                continue;
            }

            // Impact

            activeSupplyPodComponent.LaunchFinishTime = TimeSpan.MaxValue;
            _audioSystem.PlayPvs(
                supplyPodComponent.ImpactSound,
                activeSupplyPodComponent.DestinationCoordinates
            );

            _appearanceSystem.SetData(uid, SupplyPodVisuals.Landed, true);

            supplyPodComponent.Landed = true;
            Dirty(uid, supplyPodComponent);

            ReleaseTrail(supplyPodComponent);

            RemComp(uid, activeSupplyPodComponent);
        }
    }

    private void OnMapInit(Entity<SupplyPodComponent> entity, ref MapInitEvent args)
    {
        // Pods spawned onto the ground start sitting there instead of falling in. Everything a
        // descent sets up is deferred until something actually launches them.
        if (HasComp<UnlaunchedSupplyPodComponent>(entity.Owner))
        {
            SetUpGrounded(entity);
            return;
        }

        BeginDescent(entity, EnsureComp<ActiveSupplyPodComponent>(entity.Owner), _gameTiming.CurTime);
    }

    #region Grounded pods

    /// <summary>
    ///     Spawns a pod sitting on the ground in its reversed pose, waiting to be launched.
    /// </summary>
    /// <remarks>
    ///     The pod has to know it is grounded before <see cref="MapInitEvent"/> runs, otherwise it
    ///         drops itself in from orbit the instant it exists - hence the uninitialized spawn.
    /// </remarks>
    public EntityUid SpawnUnlaunchedPod(EntProtoId protoId, EntityCoordinates coordinates)
    {
        var podUid = EntityManager.CreateEntityUninitialized(protoId, coordinates);
        EnsureComp<UnlaunchedSupplyPodComponent>(podUid);
        EntityManager.InitializeAndStartEntity(podUid);

        return podUid;
    }

    private void SetUpGrounded(Entity<SupplyPodComponent> entity)
    {
        // Landed, but with no impact behind it: the pod sits at ground draw depth without the
        // rubble it never threw up.
        entity.Comp.Landed = true;
        Dirty(entity);

        _appearanceSystem.SetData(entity.Owner, SupplyPodVisuals.Landed, false);
        _appearanceSystem.SetData(entity.Owner, SupplyPodVisuals.Reversed, true);

        // Pods normally arrive locked and are cracked open by their landing trigger. One that never
        // lands would stay sealed forever, so it is handed over openable. Plenty of pods - cruise
        // missiles, say - have nothing to unlock at all.
        if (TryComp<LockComponent>(entity.Owner, out var lockComponent))
            _lockSystem.Unlock(entity.Owner, null, lockComponent);

        // Cargo pods carry a despawn timer meant to clean up after a delivery. A pod waiting to be
        // loaded must not quietly evaporate; the landing trigger adds the timer back on arrival.
        RemComp<TimedDespawnComponent>(entity.Owner);
    }

    /// <summary>
    ///     Sends a grounded pod back up. It rises to <see cref="SupplyPodComponent.Height"/>, turns
    ///         around, and drops onto <paramref name="dropoffCoordinates"/> - or back onto the tile
    ///         it left from, when none is given.
    /// </summary>
    public bool TryLaunchPod(Entity<SupplyPodComponent?> entity, EntityCoordinates? dropoffCoordinates = null)
    {
        if (!Resolve(entity.Owner, ref entity.Comp, logMissing: false)
            || !HasComp<UnlaunchedSupplyPodComponent>(entity.Owner))
            return false;

        var supplyPodComponent = entity.Comp;

        RemComp<UnlaunchedSupplyPodComponent>(entity.Owner);

        var transformComponent = Transform(entity.Owner);
        var launchCoordinates = transformComponent.Coordinates;

        // Whatever is standing in the pod rides along, and nothing gets at the cargo mid-flight.
        if (TryComp<EntityStorageComponent>(entity.Owner, out var entityStorageComponent))
            _entityStorageSystem.CloseStorage(entity.Owner, entityStorageComponent);

        if (TryComp<LockComponent>(entity.Owner, out var lockComponent))
            _lockSystem.Lock(entity.Owner, null, lockComponent);

        _audioSystem.PlayPvs(supplyPodComponent.LaunchSound, launchCoordinates);

        var curTime = _gameTiming.CurTime;

        // Built and filled in before it is added, because adding it starts it, and whatever
        // listens for the launch has to be told this leg goes up rather than down.
        var activeComponent = EntityManager.ComponentFactory.GetComponent<ActiveSupplyPodComponent>();
        activeComponent.Ascending = true;
        activeComponent.DropoffCoordinates = dropoffCoordinates ?? launchCoordinates;
        activeComponent.DestinationCoordinates = launchCoordinates;
        activeComponent.LaunchFinishTime = curTime + supplyPodComponent.LaunchDuration;

        // The fall sound belongs to the way down.
        activeComponent.FallSoundTime = TimeSpan.MaxValue;
        activeComponent.Angle = _robustRandom.NextAngle(-supplyPodComponent.AngularDeviation, supplyPodComponent.AngularDeviation);

        AddComp(entity.Owner, activeComponent);
        Dirty(entity.Owner, activeComponent);

        _transformSystem.SetLocalRotation(entity.Owner, activeComponent.Angle, xform: transformComponent);

        supplyPodComponent.Landed = false;
        Dirty(entity.Owner, supplyPodComponent);

        // Nothing is burning on the way up - no trail, and no engine glow either. Both belong to
        // the pod coming back down, and are set up by BeginDescent when it turns around.

        return true;
    }

    #endregion

    /// <summary>
    ///     Points an already-active pod down at its destination and starts the descent. Used both by
    ///         a freshly spawned pod and by the second leg of a launched one.
    /// </summary>
    private void BeginDescent(
        Entity<SupplyPodComponent> entity,
        ActiveSupplyPodComponent activeComponent,
        TimeSpan curTime)
    {
        var transformComponent = Transform(entity.Owner);
        var wasAscending = activeComponent.Ascending;

        // A pod coming back from a launch has to be moved to its dropoff first; a freshly spawned
        // one is already standing on its own impact point.
        if (activeComponent.DropoffCoordinates is { } dropoffCoordinates)
        {
            _transformSystem.SetCoordinates(entity.Owner, transformComponent, dropoffCoordinates);
            activeComponent.DropoffCoordinates = null;
        }

        activeComponent.Ascending = false;
        activeComponent.DestinationCoordinates = transformComponent.Coordinates;
        activeComponent.LaunchFinishTime = curTime + entity.Comp.FallDuration;
        activeComponent.FallSoundTime = curTime + entity.Comp.FallSoundDelay;
        activeComponent.Angle = _robustRandom.NextAngle(-entity.Comp.AngularDeviation, entity.Comp.AngularDeviation);
        Dirty(entity.Owner, activeComponent);

        _transformSystem.SetLocalRotation(entity.Owner, activeComponent.Angle, xform: transformComponent);
        _appearanceSystem.SetData(entity.Owner, SupplyPodVisuals.Landed, false);

        // tgstation's backToNonReverseIcon(): a pod only flies fins-down on the way up.
        _appearanceSystem.SetData(entity.Owner, SupplyPodVisuals.Reversed, false);

        entity.Comp.Landed = false;
        Dirty(entity);

        // Only the second leg gets here already active; a freshly spawned pod had its launch
        // announced by the component starting up.
        if (wasAscending)
            RaiseLaunched(entity.Owner, ascending: false);

        SpawnTrail(entity, activeComponent, transformComponent, curTime, entity.Comp.FallDuration);
    }

    /// <summary>
    ///     Lays the trail out along the exact axis the client animates the pod along, so the pod
    ///         appears to carve it out as it flies.
    /// </summary>
    private void SpawnTrail(
        Entity<SupplyPodComponent> entity,
        ActiveSupplyPodComponent activeComponent,
        TransformComponent transformComponent,
        TimeSpan curTime,
        TimeSpan revealDuration)
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
        trailComponent.RevealDuration = revealDuration;

        // Pointing the trail at the pod itself makes the reveal track the pod's animated position
        // rather than a schedule, so the two stay locked together however late the client starts
        // animating the descent.
        trailComponent.SourceEntity = entity.Owner;

        Dirty(trailUid, trailComponent);

        entity.Comp.TrailEntity = trailUid;
    }
}
