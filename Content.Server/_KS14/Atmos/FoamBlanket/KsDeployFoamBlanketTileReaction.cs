using Content.Shared._KS14.Atmos.FoamBlanket;
using Content.Shared._KS14.TileEffects;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Atmos.FoamBlanket;

/// <summary>
///     Spends the reagent on laying a foam blanket over the tile, or topping up the one there.
/// </summary>
public sealed partial class KsDeployFoamBlanketTileReaction : KsTileReaction
{
    [Dependency] private SharedKsFoamBlanketSystem _foamBlanketSystem = default!;

    [DataField]
    public EntProtoId Prototype = "KsFoamBlanket";

    protected override FixedPoint2 React(TileRef tile, ReagentPrototype reagent, FixedPoint2 reactVolume, List<ReagentData>? data)
    {
        if (reactVolume <= FixedPoint2.Zero || tile.Tile.IsEmpty)
            return FixedPoint2.Zero;

        var used = _foamBlanketSystem.Deploy(Prototype, tile, (float)reactVolume);
        return FixedPoint2.Min(FixedPoint2.New(used), reactVolume);
    }
}
