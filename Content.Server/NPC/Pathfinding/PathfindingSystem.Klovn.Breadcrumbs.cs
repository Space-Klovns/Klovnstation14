// KS14: added in this fork
using Content.Server._KS14.NPC.Pushing;
using Content.Shared.Doors.Components;
using Content.Shared.NPC;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Server.NPC.Pathfinding;

public sealed partial class PathfindingSystem
{
    [Dependency] private NpcPushSystem _npcPushSystem = default!;

    /// <summary>
    /// What crossing a tile blocked only by things that can be pushed out of the way costs, on top of walking it, for a
    /// path that may push (<see cref="PathFlags.Pushing"/>): about as far again as is worth walking round instead. A shove
    /// is on a cooldown and often fails, so clearing the way takes a few seconds.
    /// </summary>
    private const float PushCost = 8f;

    /// <summary>
    /// What an entity on a tile adds to every point of the tile it collides with. Worked out once per tile, where it was
    /// worked out again for each of the tile's <see cref="SharedPathfindingSystem.SubStep"/> squared points: only
    /// whether the entity covers a point differs from point to point.
    /// </summary>
    private readonly record struct TileEntity(
        FixturesComponent Fixtures,
        Transform LocalTransform,
        PathfindingBreadcrumbFlag Flags,
        bool BlockedBesidesDoors,
        float Damage,
        bool Pushable);

    /// <summary>
    /// How many points across, of a tile's <see cref="SharedPathfindingSystem.SubStep"/>, a mob needs to get through:
    /// three quarters of a tile, for a mob about 0.7 tiles across.
    /// </summary>
    private const int MobWidthInPoints = 3;

    /// <summary>
    /// Blocks the rest of a tile that something only partly blocks, if what it leaves free is too narrow for a mob. A
    /// closet - half a tile wide, and loose, so pushed about - a little off the middle of its tile leaves half a tile
    /// beside it, which the navmesh kept as floor: paths ran past the closet there, where no mob fits, and NPCs walked
    /// into it, stuck, until they gave up. (Centred, it leaves a quarter of a tile either side, too thin for the navmesh
    /// to link to the next tile anyway.) A tile is left as it is if any <see cref="MobWidthInPoints"/> square of its
    /// points is free, as a thin window along one edge leaves it; and if what blocks it is a door, which steering opens
    /// or forces on its own terms. Only within the tile: a gap made by things on two tiles next to each other is not seen,
    /// nor the part of something that reaches into the next tile, as only the tile its centre is on counts it.
    /// </summary>
    private static void BlockNarrowGaps(PathfindingBreadcrumb[,] points, int tileX, int tileY)
    {
        var originX = tileX * SubStep;
        var originY = tileY * SubStep;
        var blocker = new PathfindingData(PathfindingBreadcrumbFlag.None, 0, 0, 0f);
        var anyBlocked = false;
        var anyFree = false;
        var allPushable = true;

        for (var x = 0; x < SubStep; x++)
        {
            for (var y = 0; y < SubStep; y++)
            {
                var data = points[originX + x, originY + y].Data;
                if (!IsBlocked(data))
                {
                    anyFree = true;
                    continue;
                }

                if ((data.Flags & PathfindingBreadcrumbFlag.Door) != 0x0)
                    return;

                anyBlocked = true;
                allPushable &= (data.Flags & PathfindingBreadcrumbFlag.Pushable) != 0x0;
                blocker.Flags |= data.Flags;
                blocker.CollisionLayer |= data.CollisionLayer;
                blocker.CollisionMask |= data.CollisionMask;
                blocker.Damage = MathF.Max(blocker.Damage, data.Damage);
            }
        }

        if (!anyBlocked || !anyFree)
            return;

        for (var x = 0; x + MobWidthInPoints <= SubStep; x++)
        {
            for (var y = 0; y + MobWidthInPoints <= SubStep; y++)
            {
                if (IsFreeSquare(points, originX + x, originY + y))
                    return;
            }
        }

        // Pushing the closet aside clears the tile only if nothing fixed is blocking any of it too.
        if (!allPushable)
            blocker.Flags &= ~PathfindingBreadcrumbFlag.Pushable;

        // One poly for the whole tile, rather than one for what was blocked and more for what was too narrow.
        for (var x = 0; x < SubStep; x++)
        {
            for (var y = 0; y < SubStep; y++)
            {
                points[originX + x, originY + y].Data = blocker;
            }
        }
    }

    private static bool IsBlocked(PathfindingData data)
    {
        return data.CollisionLayer != 0 || data.CollisionMask != 0;
    }

    private static bool IsFreeSquare(PathfindingBreadcrumb[,] points, int cornerX, int cornerY)
    {
        for (var x = 0; x < MobWidthInPoints; x++)
        {
            for (var y = 0; y < MobWidthInPoints; y++)
            {
                if (IsBlocked(points[cornerX + x, cornerY + y].Data))
                    return false;
            }
        }

        return true;
    }

    private TileEntity GetTileEntity(EntityUid uid, FixturesComponent fixturesComponent, TransformComponent transformComponent)
    {
        var flags = PathfindingBreadcrumbFlag.None;

        // Flag only readers that actually restrict. An airlock's own reader is empty and defers to its door electronics
        //      board, which decides - unless the door's own reader is switched off (access wire cut), which lets anyone
        //      through whatever the board says. An emag clears the board's lists instead, and emergency access lets
        //      everyone through. All of them rebuild the chunk: see PathfindingSystem.Klovn.Access.cs
        if (_accessReaderQuery.TryGetComponent(uid, out var ownAccessReaderComponent) &&
            ownAccessReaderComponent.Enabled &&
            !(_airlockQuery.TryGetComponent(uid, out var airlockComponent) && airlockComponent.EmergencyAccess) &&
            _accessReaderSystem.GetMainAccessReader(uid, out var mainAccessReader) &&
            mainAccessReader.Value.Comp.Enabled &&
            (mainAccessReader.Value.Comp.AccessKeys.Count > 0 || mainAccessReader.Value.Comp.AccessLists.Count > 0))
        {
            flags |= PathfindingBreadcrumbFlag.Access;
        }

        // Something anchored and solid here that is not a door - a window under its shutters, a grille - so opening every
        //      door here still leaves no way through.
        var isDoor = _doorQuery.TryGetComponent(uid, out var doorComponent);
        var blockedBesidesDoors = !isDoor && transformComponent.Anchored;

        if (isDoor)
        {
            flags |= PathfindingBreadcrumbFlag.Door;

            // Doors nobody can get through: bolted shut. Not one bolted on its way open - an access breaker drops the
            //      bolts as the door starts opening, while it is still solid, and a rebuild then walled off the way an
            //      NPC had just forced, sending it off the other way until the door had opened and the next rebuild.
            if (_doorBoltQuery.TryGetComponent(uid, out var doorBoltComponent) && doorBoltComponent.BoltsDown &&
                doorComponent!.State is not (DoorState.Emagging or DoorState.Opening or DoorState.Open))
                flags |= PathfindingBreadcrumbFlag.Bolted;

            if (doorComponent!.State == DoorState.Welded)
                flags |= PathfindingBreadcrumbFlag.Welded;
        }

        if (_climbableQuery.HasComponent(uid))
            flags |= PathfindingBreadcrumbFlag.Climb;

        var damage = _destructibleQuery.TryGetComponent(uid, out var destructibleComponent)
            ? _destructible.DestroyedAt(uid, destructibleComponent).Float()
            : 0f;

        // Loose, and nothing says it cannot be pushed: see NpcPushSystem. Doors are anchored, so never are.
        var pushable = !transformComponent.Anchored && _npcPushSystem.IsPushable(uid, pusherUid: null);

        return new TileEntity(fixturesComponent,
            new Transform(transformComponent.LocalPosition, transformComponent.LocalRotation),
            flags,
            blockedBesidesDoors,
            damage,
            pushable);
    }
}
