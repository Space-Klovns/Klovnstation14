// KS14: added in this fork
using Content.Shared._KS14.Doors;
using Content.Shared.Access.Components;
using Content.Shared.Doors;
using Content.Shared.Doors.Components;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;

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

    /// <summary>
    ///     A door bolted or unbolted is shut to everyone or open to them again: see
    ///         <see cref="PathfindingBreadcrumbFlag.Bolted"/>. Nothing physical changes, so nothing else rebuilds the
    ///         chunk.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnDoorBoltsChanged(Entity<DoorBoltComponent> entity, ref DoorBoltsChangedEvent args)
    {
        DirtyChunkAt(entity.Owner);
    }

    /// <summary>
    ///     As <see cref="OnDoorBoltsChanged"/>, for welding: see <see cref="PathfindingBreadcrumbFlag.Welded"/>.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnWeldableChanged(Entity<WeldableComponent> entity, ref WeldableChangedEvent args)
    {
        DirtyChunkAt(entity.Owner);
    }

    /// <summary>
    ///     A door on emergency access lets everyone through, so it no longer counts as restricting access.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnEmergencyAccessChanged(Entity<AirlockComponent> entity, ref KsAirlockEmergencyAccessChangedEvent args)
    {
        DirtyChunkAt(entity.Owner);
    }

    private void DirtyChunkAt(EntityUid uid)
    {
        var coordinates = _transform.GetMoverCoordinates(uid);
        DirtyChunk(coordinates.EntityId, coordinates);
    }
}
