using Content.Client._KS14.Fluids;
using Content.Shared._KS14.Chemistry.PredictedTileReactions;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Map;

namespace Content.Client._KS14.Chemistry.TileReactions;

/// <summary>
///     The client's prediction of the server's <c>SpillTileReaction</c>: spills the reagent onto the tile, for this
///         client's own vapor only. Named exactly as the server's, so the client reads it from the same
///         <c>tileReactions</c> entry (see <see cref="KsTileReactionListSerializer"/>).
/// </summary>
[DataDefinition]
public sealed partial class SpillTileReaction : ITileReaction
{
    public FixedPoint2 TileReact(TileRef tile,
        ReagentPrototype reagent,
        FixedPoint2 reactVolume,
        IEntityManager entityManager,
        List<ReagentData>? data)
    {
        if (!entityManager.System<KsPredictedTileReactionSystem>().ShouldReact)
            return FixedPoint2.Zero;

        // At upstream's default temperature, as the server's spills.
        return entityManager.System<KsPredictedSpillSystem>().TrySpill(tile, new Solution(reagent.ID, reactVolume, data), onlyIfPuddlePresent: false)
            ? reactVolume
            : FixedPoint2.Zero;
    }
}
