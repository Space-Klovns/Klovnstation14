// KS14: added in this fork
using Content.Server._KS14.NPC.Dangers;
using Content.Server._KS14.NPC.Grenades;
using Content.Server.Explosion.EntitySystems;
using Content.Shared.Explosion.Components;
using Content.Shared.Flash;
using Content.Shared.Trigger.Components.Effects;
using Content.Shared.Trigger.Components;
using Robust.Shared.Containers;
using Robust.Shared.Map;

namespace Content.Server.NPC.Pathfinding;

/// <summary>
///     Primed explosives and flashes as tile dangers (<see cref="NpcTileDangerKind.Blast"/>, <see cref="NpcTileDangerKind.Flash"/>):
///         every tile their blast or flash would reach, round where they will land if they are still in the air, so that a
///         path goes round a grenade rather than up to it and then away. Worked out with the fires, in
///         <see cref="RefreshTileDangers"/>, so as often and only when asked. A flash is no danger to one it cannot blind,
///         which <see cref="GetAvoidedDangers"/> leaves it out for.
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
    [Dependency] private SharedFlashSystem _flashSystem = default!;

    /// <summary>
    ///     Whether there is any flash danger about, as of the last refresh: only then is anyone asked whether it can be
    ///         blinded, which raises an event through its gear.
    /// </summary>
    private bool _anyFlashDanger;

    /// <summary>
    ///     Who cannot be blinded, as of the last refresh, by NPC: asked at most once a refresh each, and only while a flash is
    ///         primed, as steering asks what an NPC avoids every tick.
    /// </summary>
    private readonly Dictionary<EntityUid, bool> _flashProof = new();

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
            AddDiscDanger(gridUid, centre, radius, NpcTileDangerKind.Blast);
            hash += HashCode.Combine(gridUid, centre, (int) MathF.Ceiling(radius), NpcTileDangerKind.Blast);
        }

        _anyFlashDanger = false;
        _flashProof.Clear();

        var flashEnumerator = EntityQueryEnumerator<ActiveTimerTriggerComponent, FlashOnTriggerComponent, TransformComponent>();
        while (flashEnumerator.MoveNext(out var flashUid, out _, out var flashOnTriggerComponent, out var transformComponent))
        {
            if (_containerSystem.IsEntityInContainer(flashUid))
                continue;

            var coordinates = _npcGrenadeSystem.TryGetLanding(flashUid, out var landing) ? landing : transformComponent.Coordinates;
            if (_transform.GetGrid(coordinates) is not { } gridUid || !_mapGridQuery.TryComp(gridUid, out var mapGridComponent))
                continue;

            var radius = MathF.Min(MaxBlastDangerRadius, flashOnTriggerComponent.Range);
            var centre = _maps.TileIndicesFor(gridUid, mapGridComponent, coordinates);
            AddDiscDanger(gridUid, centre, radius, NpcTileDangerKind.Flash);
            hash += HashCode.Combine(gridUid, centre, (int) MathF.Ceiling(radius), NpcTileDangerKind.Flash);
            _anyFlashDanger = true;
        }
    }

    /// <summary>
    ///     <paramref name="dangers"/> without <see cref="NpcTileDangerKind.Flash"/> if <paramref name="uid"/> cannot be blinded.
    /// </summary>
    private NpcTileDangerKind WithoutHarmlessFlash(EntityUid uid, NpcTileDangerKind dangers)
    {
        if ((dangers & NpcTileDangerKind.Flash) == 0x0 || !_anyFlashDanger)
            return dangers;

        if (!_flashProof.TryGetValue(uid, out var flashProof))
        {
            flashProof = !_flashSystem.CanFlash(uid);
            _flashProof[uid] = flashProof;
        }

        return flashProof ? dangers & ~NpcTileDangerKind.Flash : dangers;
    }

    private void AddDiscDanger(EntityUid gridUid, Vector2i centre, float radius, NpcTileDangerKind danger)
    {
        var reach = (int) MathF.Ceiling(radius);
        var radiusSquared = radius * radius;

        for (var x = -reach; x <= reach; x++)
        {
            for (var y = -reach; y <= reach; y++)
            {
                if (x * x + y * y <= radiusSquared)
                    AddTileDanger(gridUid, centre + new Vector2i(x, y), danger);
            }
        }
    }
}
