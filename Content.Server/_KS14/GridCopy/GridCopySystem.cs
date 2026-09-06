using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Numerics;
using Content.Shared.Mind.Components;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._KS14.GridCopy;

/// <summary>
///     Duplicates a whole grid (e.g. a ship) in-memory and pastes the copy back onto the same map.
///     Backs the <c>copygrid</c> admin command; kept as a system so the copy logic is reusable and testable.
///     <para>
///         Serialized to an in-memory buffer with <see cref="MapLoaderSystem.TrySaveGrid"/> and immediately
///         deserialized back with <see cref="MapLoaderSystem.TryLoadGrid"/>, which merges the copy onto the
///         (already running) source map and map-initializes it.
///     </para>
///     <para>
///         Limitations inherent to single-grid serialization:
///         <list type="bullet">
///             <item>Minded mobs/players are stripped from the copy (<see cref="StripMindedMobs"/>) so their
///             minds aren't duplicated.</item>
///             <item>References to entities on OTHER grids (docking links, cross-grid device/signal lists) can't
///             survive the copy; copying a docked grid makes the load throw and roll back, reported as a clean
///             failure rather than an unhandled exception.</item>
///             <item>Serialization raises the engine's pre-save hooks scoped to the whole source map, so copying can
///             have map-wide side effects (e.g. detaching ghost spectators, pruning dangling device-list references).</item>
///         </list>
///     </para>
/// </summary>
public sealed partial class GridCopySystem : EntitySystem
{
    [Dependency] private MapLoaderSystem _loader = default!;

    /// <summary>
    ///     Serializes <paramref name="grid"/> and loads a duplicate onto the same map, shifted by
    ///     <paramref name="offset"/> and rotated (about the grid's own origin) by <paramref name="rot"/>.
    /// </summary>
    /// <param name="copy">The newly created duplicate grid, on success.</param>
    /// <param name="error">A localized, user-facing error message, on failure.</param>
    public bool TryCopyGrid(
        Entity<MapGridComponent> grid,
        Vector2 offset,
        Angle rot,
        [NotNullWhen(true)] out Entity<MapGridComponent>? copy,
        [NotNullWhen(false)] out string? error)
    {
        copy = null;
        error = null;

        // A map entity is technically a grid too, but it can't be round-tripped as a standalone grid.
        if (HasComp<MapComponent>(grid))
        {
            error = Loc.GetString("cmd-copygrid-is-map");
            return false;
        }

        var xform = Transform(grid);
        var mapId = xform.MapID;
        if (mapId == MapId.Nullspace)
        {
            error = Loc.GetString("cmd-copygrid-no-map");
            return false;
        }

        // TryLoadGrid's merge path applies rotation to the grid's map-local position vector about the MAP origin
        // (pos = Rotate(localPos, rot) + offset), which would also translate the copy far from the original.
        // Compensate the offset so the copy instead rotates about its own origin, landing at localPos + offset.
        if (rot != Angle.Zero)
        {
            var localPos = xform.LocalPosition;
            offset += localPos - rot.RotateVec(localPos);
        }

        // Strip minded mobs for this one save only, so normal savemap/savegrid behaviour is unaffected.
        var writer = new StringWriter();
        _loader.OnIsSerializable += StripMindedMobs;
        bool saved;
        try
        {
            saved = _loader.TrySaveGrid(grid, writer);
        }
        finally
        {
            _loader.OnIsSerializable -= StripMindedMobs;
        }

        if (!saved)
        {
            error = Loc.GetString("cmd-copygrid-save-failed");
            return false;
        }

        // The load can throw (and roll back) if the source grid carries cross-grid links such as docking; surface
        // that as a clean failure instead of crashing the caller.
        using var reader = new StringReader(writer.ToString());
        try
        {
            if (!_loader.TryLoadGrid(mapId, reader, "copygrid", out copy, offset: offset, rot: rot))
            {
                error = Loc.GetString("cmd-copygrid-load-failed");
                return false;
            }
        }
        catch (Exception e)
        {
            error = Loc.GetString("cmd-copygrid-load-threw", ("reason", e.Message));
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Serialization veto: keeps minded mobs (players, ghosts, the invoking admin's own body) out of the copy.
    ///     Skipping an entity also skips its whole subtree, and with the mob gone nothing references its nullspace
    ///     mind, so no duplicate mind/objective/role entities are ever created.
    /// </summary>
    private void StripMindedMobs(Entity<MetaDataComponent> ent, ref bool serializable)
    {
        if (TryComp<MindContainerComponent>(ent.Owner, out var mind) && mind.HasMind)
            serializable = false;
    }
}
