using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Maps;
using JetBrains.Annotations;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Chemistry.TileReactions;

/// <summary>
///     Uses a reagent up to turn the floor it lands on into another tile. Open space has nothing to pave.
/// </summary>
[UsedImplicitly]
[DataDefinition]
public sealed partial class KsSetTileTileReaction : ITileReaction
{
    [DataField(required: true)]
    public ProtoId<ContentTileDefinition> Tile;

    /// <summary>
    ///     Units it takes to turn one tile.
    /// </summary>
    [DataField]
    public FixedPoint2 Usage = FixedPoint2.New(1);

    public FixedPoint2 TileReact(TileRef tile,
        ReagentPrototype reagent,
        FixedPoint2 reactVolume,
        IEntityManager entityManager,
        List<ReagentData>? data)
    {
        var turfSystem = entityManager.System<TurfSystem>();
        if (reactVolume < Usage || turfSystem.IsSpace(tile) || turfSystem.GetContentTileDefinition(tile).ID == Tile)
            return FixedPoint2.Zero;

        var replacement = IoCManager.Resolve<IPrototypeManager>().Index(Tile);
        return entityManager.System<TileSystem>().ReplaceTile(tile, replacement) ? Usage : FixedPoint2.Zero;
    }
}
