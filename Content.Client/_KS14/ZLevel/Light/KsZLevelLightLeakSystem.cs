using System.Numerics;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Light;
using Content.Shared._KS14.ZLevel;
using Content.Shared._KS14.ZLevel.Light;
using Robust.Client.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._KS14.ZLevel.Light;

/// <summary>
///     Shines light down the z-level stack: a light also lights the floors below it, through the gaps in the
///         floor it stands on, dimmed by how far down they are and how far across it had to reach them.
/// </summary>
/// <remarks>
///     There is no way to hand the engine a light with a height, so each gap a light can reach gets a
///         client-side stand-in light of its own on the z-level below, sized and dimmed by
///         <see cref="KsZLevelLightLeak"/> so that it arrives at the brightness the real one falls off to over
///         that drop, and dimmed again by how much of the light got across its own floor to that gap.
///     Going through the gaps is the whole point. Lighting the floor below directly would put a lit room under
///         every lit room, and while the floors above hide most of that, looking down a hole would show a
///         evenly lit room rather than the shaft of light that should be there.
///     This only ever goes downwards. Upwards would light solid floor from underneath with nothing drawn over
///         it to hide it, and a client is not sent the z-level above it in the first place.
/// </remarks>
public sealed partial class KsZLevelLightLeakSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private KsZLevelLightBufferSystem _lightBufferSystem = default!;
    [Dependency] private KsZLevelSystem _zLevelSystem = default!;
    [Dependency] private PointLightSystem _pointLightSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<KsZLevelComponent> _zLevelQuery = default!;
    [Dependency] private EntityQuery<KsZLevelLeakedLightComponent> _leakedLightQuery = default!;
    [Dependency] private EntityQuery<KsZLevelLightLeakComponent> _leakQuery = default!;
    [Dependency] private EntityQuery<MapComponent> _mapQuery = default!;
    [Dependency] private EntityQuery<PointLightComponent> _pointLightQuery = default!;
    [Dependency] private EntityQuery<TransformComponent> _transformQuery = default!;

    private static readonly EntProtoId StandInPrototype = "KsZLevelLeakedLight";

    /// <summary>
    ///     How far a light has to move before the gaps under it are looked for again, in tiles.
    /// </summary>
    private const float SearchMoveTolerance = 0.25f;

    /// <summary>
    ///     Whether stand-ins were wanted last frame, so that a mode change tears them down once rather than
    ///         sweeping every entity every frame for the rest of the round.
    /// </summary>
    private bool _wereWanted;
    private float _levelHeight;
    private int _maximumLevels;
    private int _maximumHoles;
    private float _energyMultiplier;
    private TimeSpan _searchInterval;

    /// <summary>
    ///     Every light this frame has to look at, gathered before any of them are touched.
    /// </summary>
    /// <remarks>
    ///     An entity query enumerates the very dictionary its component lives in, and spawning a stand-in puts
    ///         a new light into that dictionary. Doing it mid-enumeration throws the moment the next light
    ///         comes up, so the walk has to finish before any of the work starts.
    /// </remarks>
    private readonly List<EntityUid> _lightsToCheck = [];

    /// <summary>Scratch space, refilled for each light in turn.</summary>
    private readonly List<Entity<KsZLevelComponent>> _levelsBelow = [];
    private readonly List<(Vector2 Position, float Distance)> _candidateHoles = [];
    private readonly List<(MapId MapId, Vector2 Position, float Radius, float Energy)> _wantedStandIns = [];

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, KsCCVars.ZLevelLightLeakHeight, value => _levelHeight = value, true);
        Subs.CVar(_configurationManager, KsCCVars.ZLevelLightLeakLevels, value => _maximumLevels = value, true);
        Subs.CVar(_configurationManager, KsCCVars.ZLevelLightLeakMaximumHoles, value => _maximumHoles = value, true);
        Subs.CVar(_configurationManager, KsCCVars.ZLevelLightLeakEnergy, value => _energyMultiplier = value, true);
        Subs.CVar(
            _configurationManager,
            KsCCVars.ZLevelLightLeakSearchInterval,
            value => _searchInterval = TimeSpan.FromSeconds(value),
            true
        );
    }

    /// <summary>
    ///     Removes every stand-in, for when they stop being the way light crosses z-levels.
    /// </summary>
    private void ReleaseAllStandIns()
    {
        // FrameUpdate stops running before it could take these down itself, so switching away has to.
        var query = AllEntityQuery<KsZLevelLightLeakComponent>();
        while (query.MoveNext(out var uid, out _))
            RemCompDeferred<KsZLevelLightLeakComponent>(uid);
    }

    /// <summary>
    ///     The component going is what deletes the stand-ins, whether that is this system dropping it or the
    ///         light it belongs to being deleted outright.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnLeakShutdown(Entity<KsZLevelLightLeakComponent> entity, ref ComponentShutdown args)
    {
        foreach (var standInUid in entity.Comp.StandIns)
        {
            if (!TerminatingOrDeleted(standInUid))
                Del(standInUid);
        }

        entity.Comp.StandIns.Clear();
    }

    /// <summary>
    ///     Driven per frame rather than per tick: the stand-ins are a rendering matter and have nothing to say
    ///         to the simulation.
    /// </summary>
    /// <remarks>
    ///     This is also the only hook that always runs. A tick update is skipped outright whenever the client
    ///         is not predicting, and re-run from the top on every rollback tick when it is - neither of which
    ///         has anything to do with how often the lighting needs to be right.
    /// </remarks>
    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        // Checked here rather than on the cvar callbacks because both cvars behind it live on another system,
        //      and one flag covers every way of switching these off.
        var wanted = _lightBufferSystem.WantsStandIns;

        if (wanted != _wereWanted)
        {
            _wereWanted = wanted;

            if (!wanted)
                ReleaseAllStandIns();
        }

        if (!wanted)
            return;

        _lightsToCheck.Clear();

        var query = EntityQueryEnumerator<PointLightComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out _))
        {
            // A stand-in is not a light in its own right. Without this every leak would leak again on the
            //      z-level it lands on, and a stack would light itself all the way to the bottom.
            if (_leakedLightQuery.HasComponent(uid))
                continue;

            _lightsToCheck.Add(uid);
        }

        foreach (var uid in _lightsToCheck)
        {
            // Gathered a moment ago rather than now, so it may have gone in between.
            if (_pointLightQuery.TryGetComponent(uid, out var lightComponent) &&
                _transformQuery.TryGetComponent(uid, out var transformComponent))
            {
                UpdateLight((uid, lightComponent, transformComponent));
            }
        }
    }

    private void UpdateLight(Entity<PointLightComponent, TransformComponent> light)
    {
        // Wanted before the component is ensured, so that a light with nowhere to shine never grows one.
        var leakComponent = _leakQuery.CompOrNull(light.Owner);
        GetWantedStandIns(light, ref leakComponent);

        if (_wantedStandIns.Count == 0)
        {
            // Switched off, pocketed, carried off the stack, turned down too far to reach, or simply standing
            //      on a floor with no gaps in it - all of which end the same way.
            // Taken now rather than deferred: deferred removals are flushed on the tick, and a light that has
            //      gone out should not keep lighting the z-level below it until the next one.
            if (leakComponent != null)
                RemComp<KsZLevelLightLeakComponent>(light.Owner);

            return;
        }

        leakComponent ??= EnsureComp<KsZLevelLightLeakComponent>(light.Owner);
        var standIns = leakComponent.StandIns;

        // Trim the gaps it no longer reaches, from the end - the ones it lost are always the far ones.
        while (standIns.Count > _wantedStandIns.Count)
        {
            var lastIndex = standIns.Count - 1;
            var trimmedUid = standIns[lastIndex];
            standIns.RemoveAt(lastIndex);

            if (!TerminatingOrDeleted(trimmedUid))
                Del(trimmedUid);
        }

        // The stand-ins carry no mask rotation of their own, so the source's is folded into the rotation of
        //      the entity itself - which comes to the same thing, since they auto-rotate.
        var maskRotation = SharedPointLightSystem.GetMaskWorldRotation(
            light.Comp1,
            _transformSystem.GetWorldRotation(light.Comp2)
        );

        for (var index = 0; index < _wantedStandIns.Count; index++)
        {
            var (mapId, position, radius, energy) = _wantedStandIns[index];

            if (index == standIns.Count)
                standIns.Add(SpawnStandIn(light.Comp1));
            else if (TerminatingOrDeleted(standIns[index]))
                standIns[index] = SpawnStandIn(light.Comp1); // Its z-level went away under it, most likely.

            ApplyStandIn(standIns[index], light.Comp1, mapId, position, maskRotation, radius, energy);
        }

        // Cheap to compare and expensive to apply - resolving a mask is a prototype lookup and a texture
        //      fetch, which is not something to do for every light on the station every frame.
        if (leakComponent.AppliedMask == light.Comp1.LightMask)
            return;

        leakComponent.AppliedMask = light.Comp1.LightMask;

        foreach (var standInUid in standIns)
        {
            if (_pointLightQuery.TryGetComponent(standInUid, out var standInLight))
                _pointLightSystem.SetMask(light.Comp1.LightMask, standInLight);
        }
    }

    /// <summary>
    ///     Fills <see cref="_wantedStandIns"/> with what this light should be casting through each gap under
    ///         it onto each z-level below, and leaves it empty if it should be casting nothing at all.
    /// </summary>
    private void GetWantedStandIns(
        Entity<PointLightComponent, TransformComponent> light,
        ref KsZLevelLightLeakComponent? leakComponent)
    {
        _wantedStandIns.Clear();

        if (_maximumLevels <= 0 || _maximumHoles <= 0 || !light.Comp1.Enabled || light.Comp1.ContainerOccluded)
            return;

        if (light.Comp2.MapUid is not { } mapUid || !_zLevelQuery.HasComponent(mapUid))
            return;

        _levelsBelow.Clear();
        if (!_zLevelSystem.TryGetZLevelsBelow(mapUid, _levelsBelow) || _levelsBelow.Count == 0)
            return;

        var radius = light.Comp1.Radius;
        var falloff = light.Comp1.Falloff;
        var curveFactor = light.Comp1.CurveFactor;
        var worldPosition = _transformSystem.GetWorldPosition(light.Comp2);
        var mapId = light.Comp2.MapID;

        // Ensured only once it is known there is somewhere for the light to go, so that a lamp in a sealed
        //      room never carries one.
        leakComponent ??= EnsureComp<KsZLevelLightLeakComponent>(light.Owner);
        RefreshHoles(leakComponent, mapId, worldPosition, radius);

        if (leakComponent.Holes.Count == 0)
            return;

        // TryGetZLevelsBelow reports the stack bottom-up, so the z-level directly below this one is its last
        //      entry and the walk downwards runs backwards through it.
        var depth = 0f;
        var levels = 0;

        for (var index = _levelsBelow.Count - 1; index >= 0 && levels < _maximumLevels; index--, levels++)
        {
            var level = _levelsBelow[index];

            // Stepping down into a z-level crosses that z-level's own Depth, the same way a fall through it
            //      does - so a deeper z-level is further from the light in exactly the way it looks.
            depth += level.Comp.Depth;

            if (!KsZLevelLightLeak.TryGetLeakedLight(
                    radius,
                    light.Comp1.Energy,
                    falloff,
                    curveFactor,
                    depth * _levelHeight,
                    out var leakedRadius,
                    out var leakedEnergy))
            {
                // Everything under this one is further still, so none of it sees the light either.
                return;
            }

            // A stack is rebuilt wholesale from network state, so a member can briefly be something that is
            //      not a map. Skipping it costs a frame of leaked light rather than a stand-in in nullspace.
            if (!_mapQuery.TryGetComponent(level.Owner, out var mapComponent))
                continue;

            foreach (var holePosition in leakComponent.Holes)
            {
                // How much of the light got across its own floor to that gap in the first place. Directly
                //      under the light this is exactly one, so a gap underfoot reproduces the plain solve.
                var holeFactor = KsZLevelLightLeak.GetHoleFactor(
                    (holePosition - worldPosition).Length(),
                    radius,
                    falloff,
                    curveFactor
                );

                if (holeFactor <= 0f)
                    continue;

                _wantedStandIns.Add((
                    mapComponent.MapId,
                    holePosition,
                    leakedRadius,
                    leakedEnergy * holeFactor * _energyMultiplier
                ));
            }
        }
    }

    /// <summary>
    ///     Looks for the gaps in the floor under a light, if anything has changed that could have moved them.
    /// </summary>
    /// <remarks>
    ///     Scanning every tile within a light's radius is far too much to do for every light on the station
    ///         every frame, and almost every light is bolted to a wall and never moves - so the answer is kept
    ///         and only worked out again when the light moves, changes reach, or the interval runs out.
    /// </remarks>
    private void RefreshHoles(KsZLevelLightLeakComponent leakComponent, MapId mapId, Vector2 worldPosition, float radius)
    {
        if (leakComponent.Searched &&
            leakComponent.SearchMapId == mapId &&
            MathHelper.CloseTo(leakComponent.SearchRadius, radius) &&
            leakComponent.SearchPosition.EqualsApprox(worldPosition, SearchMoveTolerance) &&
            _gameTiming.CurTime < leakComponent.NextSearch)
        {
            return;
        }

        leakComponent.Searched = true;
        leakComponent.SearchMapId = mapId;
        leakComponent.SearchRadius = radius;
        leakComponent.SearchPosition = worldPosition;
        leakComponent.NextSearch = _gameTiming.CurTime + _searchInterval;
        leakComponent.Holes.Clear();

        // Off every grid, so the light is hanging over open space and shines straight down from where it is.
        if (!_mapSystem.TryFindGridAt(mapId, worldPosition, out var gridUid, out var mapGridComponent))
        {
            leakComponent.Holes.Add(worldPosition);
            return;
        }

        _candidateHoles.Clear();

        var searchArea = Box2.CenteredAround(worldPosition, new Vector2(radius * 2f, radius * 2f));
        var tiles = _mapSystem.GetTilesIntersecting(gridUid, mapGridComponent, searchArea, ignoreEmpty: false);

        foreach (var tileRef in tiles)
        {
            if (!_zLevelSystem.IsTileTransparent(tileRef.Tile))
                continue;

            var candidate = _mapSystem.GridTileToWorldPos(gridUid, mapGridComponent, tileRef.GridIndices);
            var distance = (candidate - worldPosition).Length();

            // The scan is a box and the light is a circle, so the corners have to be dropped by hand.
            if (distance > radius)
                continue;

            _candidateHoles.Add((candidate, distance));
        }

        // Nearest first, which is also brightest first, so capping the list keeps the gaps that matter.
        _candidateHoles.Sort(static (first, second) => first.Distance.CompareTo(second.Distance));

        var wanted = Math.Min(_candidateHoles.Count, _maximumHoles);
        for (var index = 0; index < wanted; index++)
            leakComponent.Holes.Add(_candidateHoles[index].Position);
    }

    private EntityUid SpawnStandIn(PointLightComponent sourceLight)
    {
        var standInUid = Spawn(StandInPrototype);

        // Left on, it would attach itself to whatever grid it ends up over and then ride along with it, while
        //      the light it stands for sits on another z-level and has not moved at all.
        _transformQuery.GetComponent(standInUid).GridTraversal = false;

        // The cached mask below only catches changes, so a fresh stand-in has to be given one to start with.
        _pointLightSystem.SetMask(sourceLight.LightMask, _pointLightQuery.GetComponent(standInUid));

        return standInUid;
    }

    private void ApplyStandIn(
        EntityUid standInUid,
        PointLightComponent sourceLight,
        MapId mapId,
        Vector2 worldPosition,
        Angle maskRotation,
        float radius,
        float energy)
    {
        if (!_pointLightQuery.TryGetComponent(standInUid, out var standInLight))
            return;

        var standInTransform = _transformQuery.GetComponent(standInUid);

        if (standInTransform.MapID != mapId ||
            !_transformSystem.GetWorldPosition(standInTransform).EqualsApprox(worldPosition))
        {
            _transformSystem.SetMapCoordinates(standInUid, new MapCoordinates(worldPosition, mapId));
        }

        if (!_transformSystem.GetWorldRotation(standInTransform).EqualsApprox(maskRotation))
            _transformSystem.SetWorldRotation(standInUid, maskRotation);

        // Every one of these drops out on its own when the value has not moved, so there is nothing gained by
        //      checking first.
        _pointLightSystem.SetRadius(standInUid, radius, standInLight);
        _pointLightSystem.SetEnergy(standInUid, energy, standInLight);
        _pointLightSystem.SetColor(standInUid, sourceLight.Color, standInLight);
        _pointLightSystem.SetSoftness(standInUid, sourceLight.Softness, standInLight);

        // Deliberately not the source's offset: a stand-in stands at the gap the light came through, and that
        //      position is already exactly where it should be.
        _pointLightSystem.SetOffset(standInUid, Vector2.Zero, standInLight);

        // Kept identical because the solve assumes them: the stand-in reproduces the real light's brightness
        //      by riding the same curve, not by being given a different one.
        _pointLightSystem.SetFalloff(standInUid, sourceLight.Falloff, standInLight);
        _pointLightSystem.SetCurveFactor(standInUid, sourceLight.CurveFactor, standInLight);

        // Shadows are cast by the z-level it lands on rather than the one it came from, which is right: what
        //      is in the way up there already decided whether the light reached the gap at all.
        _pointLightSystem.SetCastShadows(standInUid, sourceLight.CastShadows, standInLight);

        _pointLightSystem.SetEnabled(standInUid, true, standInLight);
    }
}
