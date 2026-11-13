// KS14: added in this fork
using Content.Server._KS14.NPC.Dangers;
using Content.Server._KS14.NPC.Grenades;
using Content.Server.Explosion.EntitySystems;
using Content.Shared.Explosion.Components;
using Content.Shared.Trigger.Components;
using Robust.Shared.Containers;
using Robust.Shared.Map;

namespace Content.Server.NPC.Pathfinding;

/// <summary>
///     Primed explosives as tile dangers (<see cref="NpcTileDangerKind.Blast"/>): every tile their blast would reach, round
///         where they will land if they are still in the air, so that a path goes round a grenade rather than up to it and
///         then away. Worked out with the fires, in <see cref="RefreshTileDangers"/>, so as often and only when asked.
/// </summary>
/// <remarks>
///     Only what the path is to cross. Getting out of a blast already started round the NPC is the grenade branch's
///         (<c>grenades.yml</c>), not the leave-danger one's, which only reacts to fire: see
///         <c>StandingInDangerPrecondition.Dangers</c>.
/// </remarks>
public sealed partial class PathfindingSystem
{
    /// <summary>
    ///     The most a blast is taken to reach, in tiles: anything bigger is not a grenade, and walking round it is no use.
    /// </summary>
    private const float MaxBlastDangerRadius = 8f;

    [Dependency] private ExplosionSystem _explosionSystem = default!;
    [Dependency] private NpcGrenadeSystem _npcGrenadeSystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;

    /// <summary>
    ///     Adds every primed explosive's reach to the dangers, and to <paramref name="hash"/>.
    /// </summary>
    private void AddBlastDangers(ref int hash)
    {
        var enumerator = EntityQueryEnumerator<ActiveTimerTriggerComponent, ExplosiveComponent, TransformComponent>();
        while (enumerator.MoveNext(out var explosiveUid, out _, out var explosiveComponent, out var transformComponent))
        {
            // Still in someone's hand or pocket: it goes off wherever it ends up, and they are the ones to worry.
            if (_containerSystem.IsEntityInContainer(explosiveUid))
                continue;

            var coordinates = _npcGrenadeSystem.TryGetLanding(explosiveUid, out var landing) ? landing : transformComponent.Coordinates;
            if (_transform.GetGrid(coordinates) is not { } gridUid || !_mapGridQuery.TryComp(gridUid, out var mapGridComponent))
                continue;

            var radius = MathF.Min(MaxBlastDangerRadius,
                _explosionSystem.IntensityToRadius(explosiveComponent.TotalIntensity,
                    explosiveComponent.IntensitySlope,
                    explosiveComponent.MaxIntensity));

            var centre = _maps.TileIndicesFor(gridUid, mapGridComponent, coordinates);
            AddBlastDanger(gridUid, centre, radius);
            hash += HashCode.Combine(gridUid, centre, (int) MathF.Ceiling(radius), NpcTileDangerKind.Blast);
        }
    }

    private void AddBlastDanger(EntityUid gridUid, Vector2i centre, float radius)
    {
        var reach = (int) MathF.Ceiling(radius);
        var radiusSquared = radius * radius;

        for (var x = -reach; x <= reach; x++)
        {
            for (var y = -reach; y <= reach; y++)
            {
                if (x * x + y * y <= radiusSquared)
                    AddTileDanger(gridUid, centre + new Vector2i(x, y), NpcTileDangerKind.Blast);
            }
        }
    }
}
