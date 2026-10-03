// KS14: added in this fork
using Content.Shared.Doors.Components;
using Content.Shared.NPC;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Server.NPC.Pathfinding;

public sealed partial class PathfindingSystem
{
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
        float Damage);

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

            // Doors nobody can get through.
            if (_doorBoltQuery.TryGetComponent(uid, out var doorBoltComponent) && doorBoltComponent.BoltsDown)
                flags |= PathfindingBreadcrumbFlag.Bolted;

            if (doorComponent!.State == DoorState.Welded)
                flags |= PathfindingBreadcrumbFlag.Welded;
        }

        if (_climbableQuery.HasComponent(uid))
            flags |= PathfindingBreadcrumbFlag.Climb;

        var damage = _destructibleQuery.TryGetComponent(uid, out var destructibleComponent)
            ? _destructible.DestroyedAt(uid, destructibleComponent).Float()
            : 0f;

        return new TileEntity(fixturesComponent,
            new Transform(transformComponent.LocalPosition, transformComponent.LocalRotation),
            flags,
            blockedBesidesDoors,
            damage);
    }
}
