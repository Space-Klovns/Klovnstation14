using Content.Shared._KS14.Chemistry.PredictedTileReactions;
using Content.Shared._KS14.TileEffects;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Atmos.FoamBlanket;

/// <summary>
///     Spends the reagent on laying a foam blanket over the tile, or topping up the one there.
/// </summary>
/// <remarks>
///     Shared, so that a client's own predicted vapor lays its blankets too: as placeholders, which it keeps until the
///         server's arrive (see <see cref="KsPredictedTileReactionSystem"/>).
/// </remarks>
public sealed partial class KsDeployFoamBlanketTileReaction : KsTileReaction
{
    [Dependency] private SharedKsFoamBlanketSystem _foamBlanketSystem = default!;
    [Dependency] private KsPredictedTileReactionSystem _predictedTileReactionSystem = default!;

    [DataField]
    public EntProtoId Prototype = "KsFoamBlanket";

    protected override FixedPoint2 React(TileRef tile, ReagentPrototype reagent, FixedPoint2 reactVolume, List<ReagentData>? data)
    {
        if (reactVolume <= FixedPoint2.Zero || tile.Tile.IsEmpty || !_predictedTileReactionSystem.ShouldReact)
            return FixedPoint2.Zero;

        var energyTaken = reagent.SpecificHeat * (float)reactVolume;
        var used = _foamBlanketSystem.Deploy(Prototype, tile, (float)reactVolume, energy: energyTaken,
            playSound: !_predictedTileReactionSystem.IsPredicting, placeholder: _predictedTileReactionSystem.IsPredicting);
        return FixedPoint2.Min(FixedPoint2.New(used), reactVolume);
    }
}
