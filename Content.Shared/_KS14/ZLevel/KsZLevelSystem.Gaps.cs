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
    ///     The next slice of airspace something falling out of this one reaches.
    /// </summary>
    /// <remarks>
    ///     The airspace above a z-level is divided between the gaps crossing it, so "the next z-level down"
    ///         is not the whole answer for a fall: dropping out of the bottom of one z-level lands in the
    ///         <em>topmost</em> slice of the one below, which is a gap if anything is crossing there.
    ///     Separate from <see cref="TryGetAdjacentZLevel"/> on purpose. That one answers about floors - it is
    ///         what an elevator uses to pick the next floor to travel to - and a gap is not a floor. This one
    ///         answers about airspace, and only a fall wants it.
    /// </remarks>
    /// <param name="rising">Up out of this slice when true, down out of it when false.</param>
    public bool TryGetAdjacentFallSlice(
        Entity<KsZLevelComponent> sliceEntity,
        bool rising,
        [NotNullWhen(true)] out Entity<KsZLevelComponent>? adjacentEntity)
    {
        adjacentEntity = null;

        // Leaving a gap: the next slice of the same airspace, or out of that airspace entirely.
        if (_gapQuery.TryGetComponent(sliceEntity.Owner, out var gapComponent))
        {
            var planeAltitude = SharedKsZLevelGapSystem.GetPlaneAltitude((sliceEntity.Owner, gapComponent));

            if (TryGetNeighbouringGap(gapComponent.LowerZLevel, planeAltitude, rising, out adjacentEntity))
                return true;

            // Nothing else in this airspace, so out of it: down onto the floor it is anchored to, or up
            //      through the ceiling onto the z-level above.
            var beyondUid = rising ? gapComponent.UpperZLevel : gapComponent.LowerZLevel;
            if (!_zLevelQuery.TryGetComponent(beyondUid, out var beyondComponent))
                return false;

            adjacentEntity = (beyondUid, beyondComponent);
            return true;
        }

        // Rising out of a z-level's own airspace is handled by the stack: a gap above it is reached by
        //      crossing into it part way up, which only a descent does here.
        if (!TryGetAdjacentZLevel(sliceEntity!, rising, out var zLevelEntity))
            return false;

        if (rising)
        {
            adjacentEntity = zLevelEntity;
            return true;
        }

        // Dropping into the airspace above the z-level below: the topmost slice of it, which is the highest
        //      thing crossing there if anything is.
        if (TryGetNeighbouringGap(zLevelEntity.Value.Owner, float.PositiveInfinity, rising: false, out adjacentEntity))
            return true;

        adjacentEntity = zLevelEntity;
        return true;
    }

    /// <summary>
    ///     The gap nearest a given altitude in a z-level's airspace, on one side of it.
    /// </summary>
    private bool TryGetNeighbouringGap(
        EntityUid anchorUid,
        float fromAltitude,
        bool rising,
        [NotNullWhen(true)] out Entity<KsZLevelComponent>? gapZLevelEntity)
    {
        gapZLevelEntity = null;
        var bestAltitude = rising ? float.PositiveInfinity : float.NegativeInfinity;

        var enumerator = EntityQueryEnumerator<KsZLevelGapComponent>();
        while (enumerator.MoveNext(out var candidateUid, out var candidateComponent))
        {
            if (candidateComponent.LowerZLevel != anchorUid)
                continue;

            var candidateAltitude = SharedKsZLevelGapSystem.GetPlaneAltitude((candidateUid, candidateComponent));

            if (rising ? candidateAltitude <= fromAltitude : candidateAltitude >= fromAltitude)
                continue;

            if (rising ? candidateAltitude >= bestAltitude : candidateAltitude <= bestAltitude)
                continue;

            if (!_zLevelQuery.TryGetComponent(candidateUid, out var candidateZLevelComponent))
                continue;

            bestAltitude = candidateAltitude;
            gapZLevelEntity = (candidateUid, candidateZLevelComponent);
        }

        return gapZLevelEntity != null;
    }

    /// <summary>
    ///     How far above its anchor's floor plane a slice of airspace sits, and which z-level that is.
    /// </summary>
    /// <returns>Zero and the slice itself when it is an ordinary z-level rather than a gap.</returns>
    public float GetSlicePlaneAltitude(EntityUid mapUid)
    {
        return _gapQuery.TryGetComponent(mapUid, out var gapComponent)
            ? SharedKsZLevelGapSystem.GetPlaneAltitude((mapUid, gapComponent))
            : 0f;
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
