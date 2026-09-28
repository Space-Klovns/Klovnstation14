// KS14: added in this fork
using Content.Shared.Access.Components;

namespace Content.Server.NPC.Pathfinding;

public sealed partial class PathfindingSystem
{
    /// <summary>
    ///     A reader changing who it lets through - reprogrammed, emagged, or its access wire cut - changes nothing
    ///         physical, so nothing else rebuilds the chunk under it and its door keeps the
    ///         <see cref="PathfindingBreadcrumbFlag.Access"/> flag it was built with.
    /// </summary>
    /// <remarks>
    ///     The reader may be a door electronics board inside the door, so its coordinates are taken from whatever it
    ///         is in rather than from its own transform.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnAccessReaderConfigurationChanged(EntityUid uid, AccessReaderComponent component, AccessReaderConfigurationChangedEvent args)
    {
        var coordinates = _transform.GetMoverCoordinates(uid);
        DirtyChunk(coordinates.EntityId, coordinates);
    }
}
