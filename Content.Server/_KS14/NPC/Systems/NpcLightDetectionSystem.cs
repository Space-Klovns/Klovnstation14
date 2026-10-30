using Content.Server._KS14.NPC.Components;
using Content.Shared._KS14.CCVar;
using Robust.Shared.ComponentTrees;
using Robust.Shared.Configuration;
using Robust.Shared.Light;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Systems;

/// <summary>
///     How well lit NPC targets are, for <see cref="KsCCVars.NpcLightDetection"/>. Each target's light level is
///         computed at most once per tick and cached on it in <see cref="NpcLightLevelCacheComponent"/>, so every
///         NPC asking about the same target that tick shares one computation.
/// </summary>
public sealed partial class NpcLightDetectionSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private LightLevelSystem _lightLevelSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<NpcLightLevelCacheComponent> _lightLevelCacheQuery = default!;
    [Dependency] private EntityQuery<LightTreeComponent> _lightTreeQuery = default!;

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

        if (observerCoordinates.MapId == targetCoordinates.MapId &&
            (observerCoordinates.Position - targetCoordinates.Position).LengthSquared() <= proximityRange * proximityRange)
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
        if (!_lightLevelSystem.TryCalculateLightLevel(targetUid, out var level))
            level = 1f;

        cacheComponent ??= EnsureComp<NpcLightLevelCacheComponent>(targetUid);
        cacheComponent.Level = level;
        cacheComponent.ComputedTick = currentTick;
        return level;
    }
}
