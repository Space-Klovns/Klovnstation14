using Content.Shared._KS14.Atmos.FoamBlanket;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using JetBrains.Annotations;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Atmos.FoamBlanket;

/// <summary>
///     Spends the reagent on laying a foam blanket over the tile, or topping up the one there.
/// </summary>
[UsedImplicitly]
[DataDefinition]
public sealed partial class KsDeployFoamBlanketTileReaction : ITileReaction
{
    [DataField]
    public EntProtoId Prototype = "KsFoamBlanket";

    public FixedPoint2 TileReact(TileRef tile,
        ReagentPrototype reagent,
        FixedPoint2 reactVolume,
        IEntityManager entityManager,
        List<ReagentData>? data)
    {
        if (reactVolume <= FixedPoint2.Zero || tile.Tile.IsEmpty)
            return FixedPoint2.Zero;

        var used = entityManager.System<SharedKsFoamBlanketSystem>().Deploy(Prototype, tile, (float)reactVolume);
        return FixedPoint2.Min(FixedPoint2.New(used), reactVolume);
    }
}
