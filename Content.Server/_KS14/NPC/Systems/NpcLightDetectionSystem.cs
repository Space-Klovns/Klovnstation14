using Content.Server._KS14.NPC.Components;
using Content.Server._KS14.NPC.Utility;
using Content.Shared._KS14.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Robust.Shared.ComponentTrees;
using Robust.Shared.Configuration;
using Robust.Shared.Light;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Systems;

/// <summary>
///     How well lit NPC targets are, for <see cref="KsCCVars.NpcLightDetection"/>. Each target's light level is
///         computed at most once per tick and cached on it in <see cref="NpcLightLevelCacheComponent"/>, so every
///         NPC asking about the same target that tick shares one computation.
/// </summary>
/// <remarks>
///     The engine's light level is the map's ambient light plus the lights about, and on a server the ambient is not what
///         a player sees: a map's day and night (<see cref="LightCycleComponent"/>) is only applied to its colour on
///         clients, and a roof (<see cref="RoofComponent"/>, <see cref="ImplicitRoofComponent"/>) only shuts it out of
///         the client's lighting. Left so, every target on a planet read as in broad day, at night or indoors alike.
///         So the ambient the engine added is taken back out and the one a client would draw put in its place
///         (<see cref="GetSeenAmbient"/>).
/// </remarks>
public sealed partial class NpcLightDetectionSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private LightLevelSystem _lightLevelSystem = default!;
    [Dependency] private MetaDataSystem _metaDataSystem = default!;
    [Dependency] private SharedGameTicker _gameTicker = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedRoofSystem _roofSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<ImplicitRoofComponent> _implicitRoofQuery = default!;
    [Dependency] private EntityQuery<LightCycleComponent> _lightCycleQuery = default!;
    [Dependency] private EntityQuery<NpcLightLevelCacheComponent> _lightLevelCacheQuery = default!;
    [Dependency] private EntityQuery<LightTreeComponent> _lightTreeQuery = default!;
    [Dependency] private EntityQuery<MapComponent> _mapQuery = default!;
    [Dependency] private EntityQuery<MapGridComponent> _mapGridQuery = default!;
    [Dependency] private EntityQuery<MapLightComponent> _mapLightQuery = default!;
    [Dependency] private EntityQuery<RoofComponent> _roofQuery = default!;

    private bool _cvarEnabled;
    private bool _warnedNoLightTree;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, KsCCVars.NpcLightDetection, value => _cvarEnabled = value, invokeImmediately: true);
    }

    /// <summary>
    ///     How lit <paramref name="targetUid"/> looks to <paramref name="observerUid"/>: fully, within
    ///         <paramref name="proximityRange"/> tiles, where however dark it is makes no difference; otherwise
    ///         <see cref="GetLightLevel"/>.
    /// </summary>
    public float GetPerceivedLightLevel(EntityUid observerUid, EntityUid targetUid, float proximityRange)
    {
        if (!_cvarEnabled)
            return 1f;

        var observerCoordinates = _transformSystem.GetMapCoordinates(observerUid);
        var targetCoordinates = _transformSystem.GetMapCoordinates(targetUid);

        if (NpcCoordinates.IsWithin(observerCoordinates, targetCoordinates, proximityRange))
            return 1f;

        return GetLightLevel(targetUid);
    }

    /// <summary>
    ///     How lit <paramref name="targetUid"/> is, from 0 (dark) to 1. Always 1 while light detection is off, so
    ///         callers can use it unconditionally - and whenever there is no light tree to compute it from.
    /// </summary>
    public float GetLightLevel(EntityUid targetUid)
    {
        if (!_cvarEnabled)
            return 1f;

        var currentTick = _gameTiming.CurTick;

        // Only ever written once the checks below have passed, so a hit needs none of them.
        if (_lightLevelCacheQuery.TryComp(targetUid, out var cacheComponent) && cacheComponent.ComputedTick == currentTick)
            return cacheComponent.Level;

        // Not on a map - in nullspace, say - so there is no light to be in, and nothing wrong with the config either.
        if (Transform(targetUid).MapUid is not { } mapUid)
            return 1f;

        // Asked of the target's map rather than of the light tree system: IsAvailable only reports the current value
        //      of lookup.enable_server_light_tree, but the tree is built at startup, so turning that cvar on mid-round
        //      claims a tree that does not exist. Without one, every target would read as pitch dark. A map only has a
        //      light tree if the tree really was built.
        if (!_lightTreeQuery.HasComp(mapUid))
        {
            if (!_warnedNoLightTree)
            {
                Log.Warning($"{KsCCVars.NpcLightDetection.Name} is on, but the server has no light tree to compute light " +
                    $"levels from; set lookup.enable_server_light_tree = true in the server config before startup. " +
                    $"Treating every target as lit.");
                _warnedNoLightTree = true;
            }

            return 1f;
        }

        // Nowhere to compute from: nothing to hide in, so lit.
        var level = _lightLevelSystem.TryCalculateLightColor(targetUid, out var color)
            ? _lightLevelSystem.ColorToLevel(WithSeenAmbient(targetUid, mapUid, color))
            : 1f;

        cacheComponent ??= EnsureComp<NpcLightLevelCacheComponent>(targetUid);
        cacheComponent.Level = level;
        cacheComponent.ComputedTick = currentTick;
        return level;
    }

    /// <summary>
    ///     <paramref name="color"/>, the engine's light at <paramref name="targetUid"/>, with the ambient it added swapped
    ///         for the one a client draws there (<see cref="GetSeenAmbient"/>). Light adds up, so what is left after the
    ///         map's own ambient is taken out is the lights about.
    /// </summary>
    private Color WithSeenAmbient(EntityUid targetUid, EntityUid mapUid, Color color)
    {
        // Lighting off: the engine says white, as the renderer draws everything lit, and so it is.
        if (!_mapQuery.TryComp(mapUid, out var mapComponent) || !mapComponent.LightingEnabled)
            return color;

        var addedAmbient = _mapLightQuery.CompOrNull(mapUid)?.AmbientLightColor ?? MapLightComponent.DefaultColor;
        var seenAmbient = GetSeenAmbient(targetUid, mapUid, addedAmbient);
        if (seenAmbient == addedAmbient)
            return color;

        return new Color(
            MathF.Max(0f, color.R - addedAmbient.R) + seenAmbient.R,
            MathF.Max(0f, color.G - addedAmbient.G) + seenAmbient.G,
            MathF.Max(0f, color.B - addedAmbient.B) + seenAmbient.B,
            color.A);
    }

    /// <summary>
    ///     The ambient light a client draws at <paramref name="targetUid"/>: the map's at this time of its day and night,
    ///         as <c>Content.Client.Light.LightCycleSystem</c> works it out; and under a roof, the roof's colour over that,
    ///         as <c>RoofOverlay</c> draws it.
    /// </summary>
    private Color GetSeenAmbient(EntityUid targetUid, EntityUid mapUid, Color mapAmbient)
    {
        var ambient = mapAmbient;

        if (_lightCycleQuery.TryComp(mapUid, out var cycleComponent) && cycleComponent.Running)
        {
            var time = (float) _gameTiming.CurTime
                .Add(cycleComponent.Offset)
                .Subtract(_gameTicker.RoundStartTimeSpan)
                .Subtract(_metaDataSystem.GetPauseTime(mapUid))
                .TotalSeconds;

            ambient = SharedLightCycleSystem.GetColor((mapUid, cycleComponent), cycleComponent.OriginalColor, time);
        }

        // Drawn only on a map with ambient light of its own (RoofOverlay.Draw): with none, the ambient is already black.
        if (_mapLightQuery.HasComp(mapUid) && GetRoofColor(targetUid) is { } roofColor)
            ambient = Color.InterpolateBetween(ambient, roofColor.WithAlpha(1f), roofColor.A);

        return ambient;
    }

    /// <summary>
    ///     The colour of the roof over <paramref name="targetUid"/>'s tile, if there is one: all of a grid that is roofed
    ///         throughout, or the tile's own.
    /// </summary>
    private Color? GetRoofColor(EntityUid targetUid)
    {
        var transformComponent = Transform(targetUid);
        if (transformComponent.GridUid is not { } gridUid || !_mapGridQuery.TryComp(gridUid, out var gridComponent))
            return null;

        if (_implicitRoofQuery.TryComp(gridUid, out var implicitRoofComponent))
            return implicitRoofComponent.Color;

        if (!_roofQuery.TryComp(gridUid, out var roofComponent))
            return null;

        var tile = _mapSystem.TileIndicesFor(gridUid, gridComponent, transformComponent.Coordinates);
        return _roofSystem.GetColor((gridUid, gridComponent, roofComponent), tile);
    }
}
