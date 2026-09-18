using System.Numerics;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.ZLevel;
using Content.Shared._KS14.ZLevel.Audio;
using Robust.Client.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Client._KS14.ZLevel.Audio;

/// <summary>
///     Lets sound carry between z-levels, through the gaps in the floor between them.
/// </summary>
/// <remarks>
///     The engine silences anything on another map outright, so this takes over the whole of its per-stream
///         processing through the hook it provides for exactly that, and differs from it in one place: where it
///         would zero the gain for being on the wrong map, this asks whether that map is a z-level near enough
///         to be heard through.
///     A sound does not come through the floor, it comes through a hole in it - so what is looked for is a gap
///         near the sound, and what the listener hears is the gap, at the distance the sound travelled to bend
///         through it. Everything else about the stream is the engine's own behaviour, reproduced.
/// </remarks>
public sealed partial class KsZLevelAudioSystem : EntitySystem
{
    [Dependency] private AudioSystem _audioSystem = default!;
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private KsZLevelSystem _zLevelSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<KsZLevelComponent> _zLevelQuery = default!;
    [Dependency] private EntityQuery<MapComponent> _mapQuery = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;

    private bool _enabled;
    private float _levelHeight;
    private int _maximumLevels;
    private float _searchRange;
    private float _crossingOcclusion;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, KsCCVars.ZLevelAudioLeakEnabled, value => _enabled = value, true);
        Subs.CVar(_configurationManager, KsCCVars.ZLevelAudioLeakHeight, value => _levelHeight = value, true);
        Subs.CVar(_configurationManager, KsCCVars.ZLevelAudioLeakLevels, value => _maximumLevels = value, true);
        Subs.CVar(_configurationManager, KsCCVars.ZLevelAudioLeakSearchRange, value => _searchRange = value, true);
        Subs.CVar(_configurationManager, KsCCVars.ZLevelAudioLeakOcclusion, value => _crossingOcclusion = value, true);

        // Single-target by contract, and asserted as such by the engine. Nothing else in the fork may take it.
        _audioSystem.ProcessStreamOverride += OnProcessStream;
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _audioSystem.ProcessStreamOverride -= OnProcessStream;
    }

    /// <summary>
    ///     Stands in for AudioSystem.ProcessStream, which the engine skips entirely while this is attached.
    /// </summary>
    /// <remarks>
    ///     Everything here other than the z-level branch is the engine's own behaviour, and has to stay that
    ///         way - this is a copy of a private method, so an engine bump is the moment to read it again and
    ///         check the two have not drifted.
    /// </remarks>
    private void OnProcessStream(
        EntityUid entity,
        AudioComponent component,
        TransformComponent transformComponent,
        MapCoordinates listener)
    {
        if (!component.Started)
        {
            component.Started = true;
            component.StartPlaying();
        }

        // Global audio has no position, so it has nowhere to leak from and nothing to leak through.
        if (component.Global)
        {
            if (transformComponent.MapID != MapId.Nullspace && listener.MapId != transformComponent.MapID)
            {
                component.Gain = 0f;
                return;
            }

            component.Volume = component.Params.Volume;
            return;
        }

        var parentUid = transformComponent.ParentUid;
        component.Volume = component.Params.Volume;

        // Grid audio is positioned by its grid rather than by itself.
        var worldPosition = (component.Flags & AudioFlags.GridAudio) != 0x0
            ? _mapSystem.GetGridPosition(parentUid)
            : _transformSystem.GetWorldPosition(entity);

        var crossings = 0;

        // Where the engine gives up on anything off the listener's map, this asks whether it is somewhere in
        //      the same stack with a gap in the floor between the two.
        if (listener.MapId != transformComponent.MapID)
        {
            if (!TryGetApparentPosition(transformComponent.MapID, worldPosition, listener, out var apparentPosition, out crossings))
            {
                component.Gain = 0f;
                return;
            }

            worldPosition = apparentPosition;
        }

        var delta = worldPosition - listener.Position;
        var distance = delta.Length();

        // Out of range, so clipped here rather than left to the mixer. Still playing, just silent.
        if (_audioSystem.GetAudioDistance(distance) > component.MaxDistance)
        {
            component.Gain = 0f;
            return;
        }

        if (distance > 0f && distance < 0.01f)
        {
            worldPosition = listener.Position;
            delta = Vector2.Zero;
            distance = 0f;
        }

        if ((component.Flags & AudioFlags.NoOcclusion) == AudioFlags.NoOcclusion)
        {
            component.Occlusion = 0f;
        }
        else
        {
            // The floor it came through counts as something in the way, on top of whatever else is: a sound
            //      that reached you around the lip of a hole is dulled even with a clear run across the room.
            component.Occlusion =
                _audioSystem.GetOcclusion(listener, delta, distance, parentUid) + crossings * _crossingOcclusion;
        }

        component.Position = worldPosition;

        if (_physicsQuery.TryGetComponent(parentUid, out var physicsComponent))
            component.Velocity = _physicsSystem.GetMapLinearVelocity(parentUid, physicsComponent);
    }

    /// <summary>
    ///     Where a sound on another z-level appears to come from, if it can be heard at all.
    /// </summary>
    /// <param name="crossings">How many z-levels it carried through, for the muffling that costs it.</param>
    private bool TryGetApparentPosition(
        MapId sourceMapId,
        Vector2 sourcePosition,
        MapCoordinates listener,
        out Vector2 apparentPosition,
        out int crossings)
    {
        apparentPosition = default;
        crossings = 0;

        if (!_enabled || _maximumLevels <= 0)
            return false;

        if (!_mapSystem.TryGetMap(sourceMapId, out var sourceMapUid) ||
            !_mapSystem.TryGetMap(listener.MapId, out var listenerMapUid))
            return false;

        // The floor being crossed always belongs to the upper of the two z-levels - the same plane an entity
        //      falls through, and for the same reason.
        MapId upperMapId;
        float depth;

        if (TryGetDepthBetween(listenerMapUid.Value, sourceMapUid.Value, out depth, out crossings))
            upperMapId = listener.MapId; // The sound is below; it comes up through the listener's own floor.
        else if (TryGetDepthBetween(sourceMapUid.Value, listenerMapUid.Value, out depth, out crossings))
            upperMapId = sourceMapId; // The sound is above; it falls through its own floor.
        else
            return false;

        if (!TryGetHole(upperMapId, sourcePosition, listener.Position, out var holePosition))
            return false;

        apparentPosition = KsZLevelAudioLeak.GetApparentPosition(
            sourcePosition,
            holePosition,
            listener.Position,
            depth * _levelHeight
        );

        return true;
    }

    /// <summary>
    ///     How far below <paramref name="upperMapUid"/> the other z-level sits, and how many floors that is.
    /// </summary>
    private bool TryGetDepthBetween(EntityUid upperMapUid, EntityUid lowerMapUid, out float depth, out int crossings)
    {
        depth = 0f;
        crossings = 0;

        if (upperMapUid == lowerMapUid || !_zLevelQuery.HasComponent(upperMapUid))
            return false;

        if (!_zLevelSystem.TryGetDepthBelow(upperMapUid, lowerMapUid, out depth, out crossings))
            return false;

        return crossings <= _maximumLevels;
    }

    /// <summary>
    ///     Finds the gap in <paramref name="upperMapId"/>'s floor that a sound would most easily carry through.
    /// </summary>
    /// <remarks>
    ///     "Most easily" is the shortest bent path - along the upper floor to the gap, then down through it -
    ///         which is the one a listener would pick out as where the sound is coming from.
    ///     The search is deliberately kept near the sound rather than spanning everything between the two: a
    ///         sound that had to bend half a room sideways to escape stops reading as having come through a
    ///         hole at all.
    /// </remarks>
    private bool TryGetHole(MapId upperMapId, Vector2 sourcePosition, Vector2 listenerPosition, out Vector2 holePosition)
    {
        holePosition = sourcePosition;

        // Already over something the sound carries through - a gap, a grating, or the edge of everything -
        //      so there is nothing to search for and nothing to bend around.
        if (_zLevelSystem.IsFloorTransparentAt(upperMapId, sourcePosition))
            return true;

        // Only the grid the sound is standing on is searched. Anywhere off it is open by definition, and would
        //      have been caught above.
        if (!_mapSystem.TryFindGridAt(upperMapId, sourcePosition, out var gridUid, out var mapGridComponent))
            return false;

        var searchArea = Box2.CenteredAround(sourcePosition, new Vector2(_searchRange * 2f, _searchRange * 2f));
        var bestPathLength = float.MaxValue;
        var found = false;

        var tiles = _mapSystem.GetTilesIntersecting(gridUid, mapGridComponent, searchArea, ignoreEmpty: false);

        foreach (var tileRef in tiles)
        {
            if (!_zLevelSystem.IsTileTransparent(tileRef.Tile))
                continue;

            var candidate = _mapSystem.GridTileToWorldPos(gridUid, mapGridComponent, tileRef.GridIndices);

            // Measured flat, because which of the two is worth bending towards does not depend on a drop both
            //      candidates pay equally.
            var pathLength = (sourcePosition - candidate).Length() + (candidate - listenerPosition).Length();
            if (pathLength >= bestPathLength)
                continue;

            bestPathLength = pathLength;
            holePosition = candidate;
            found = true;
        }

        return found;
    }
}
