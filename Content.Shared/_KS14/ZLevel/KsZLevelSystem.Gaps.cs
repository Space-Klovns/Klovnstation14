using System.Diagnostics.CodeAnalysis;
using Content.Shared._KS14.ZLevel.Transit;

namespace Content.Shared._KS14.ZLevel;

/// <summary>
///     Makes the maps between z-levels answer stack questions as though they were the z-level below them.
/// </summary>
/// <remarks>
///     A gap map is not in any stack - see <see cref="KsZLevelGapComponent"/> for why putting one in would
///         break floor numbering, elevator navigation and the light-from-above pass. The consequence is that
///         everything that navigates the stack would otherwise find nothing at all from a gap, and a rider on
///         an elevator would hear and see none of the world.
///     Rather than teaching audio leak, light leak, PVS and the renderer about gaps one by one, the resolve
///         happens here, in the primitives all four already call. A gap is its anchor plus an offset, and only
///         this file knows that.
/// </remarks>
public sealed partial class KsZLevelSystem : EntitySystem
{
    [Dependency] private EntityQuery<KsZLevelGapComponent> _gapQuery = default!;

    /// <summary>
    ///     Whether this map is the space between two z-levels rather than a z-level itself.
    /// </summary>
    public bool IsGap(EntityUid mapUid)
    {
        return _gapQuery.HasComponent(mapUid);
    }

    /// <summary>
    ///     Resolves a gap map to the z-level it is anchored to, and how far above that z-level it sits.
    /// </summary>
    /// <remarks>
    ///     Reads two components and allocates nothing, because <see cref="TryGetDepthBelow"/> promises the
    ///         same of itself and the audio system calls it from its parallel stream jobs.
    /// </remarks>
    /// <param name="offset">How far above the anchor's floor plane the gap sits, in z-levels.</param>
    /// <returns>False if this is not a gap map, or if its anchor has stopped being a z-level.</returns>
    private bool TryResolveGapAnchor(
        EntityUid mapUid,
        [NotNullWhen(true)] out Entity<KsZLevelComponent>? anchorEntity,
        out float offset)
    {
        anchorEntity = null;
        offset = 0f;

        if (!_gapQuery.TryGetComponent(mapUid, out var gapComponent) ||
            !_zLevelQuery.TryGetComponent(gapComponent.LowerZLevel, out var anchorZLevelComponent))
            return false;

        anchorEntity = (gapComponent.LowerZLevel, anchorZLevelComponent);
        offset = gapComponent.Progress * gapComponent.TotalDepth;

        return true;
    }

    /// <summary>
    ///     Substitutes a gap map for its anchor, so that the caller walks a real stack.
    /// </summary>
    /// <returns>Whether a substitution happened.</returns>
    private bool TrySubstituteGapAnchor(ref Entity<KsZLevelComponent?> entity)
    {
        if (!TryResolveGapAnchor(entity.Owner, out var anchorEntity, out _))
            return false;

        entity = (anchorEntity.Value.Owner, anchorEntity.Value.Comp);
        return true;
    }
}
