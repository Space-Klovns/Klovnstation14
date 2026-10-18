using Content.Server._KS14.Atmos.FoamBlanket;
using Content.Server.Chemistry.TileReactions;
using Content.Shared._KS14.Chemistry.SolutionHeatExchange;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Robust.Shared.Map;

namespace Content.Server.Fluids.EntitySystems;

public sealed partial class SmokeSystem
{
    [Dependency] private PuddleSystem _puddleSystem = default!;
    [Dependency] private KsSolutionHeatExchangeSystem _heatExchangeSystem = default!;

    /// <summary>
    ///     Equalises the temperature of the smoke's solution with that of a puddle on the tile, if any.
    /// </summary>
    private void EqualizeTemperatureWithPuddle(Entity<SolutionComponent> smokeSolution, TileRef tile)
    {
        if (!_puddleSystem.TryGetCachedPuddle(tile, out Entity<PuddleComponent> puddle))
            return;

        if (!_solutionContainerSystem.ResolveSolution(puddle.Owner, puddle.Comp.SolutionName, ref puddle.Comp.Solution, out _))
            return;

        _heatExchangeSystem.TryEqualizeTemperature(smokeSolution, puddle.Comp.Solution.Value);
    }

    /// <summary>
    ///     <see cref="ReagentPrototype.ReactionTile"/>, but only reports what <see cref="ExtinguishTileReaction"/>
    ///         and <see cref="KsDeployFoamBlanketTileReaction"/> consumed. Smoke has always ignored the other
    ///         reactions' return values (spill, clean and so on), and draining the smoke by them would change how it
    ///         behaves; boiled-off water has to leave it, and so does foam laid as a blanket, or one cloud could lay
    ///         blankets forever.
    /// </summary>
    private FixedPoint2 ReactionTileConsumingExtinguish(TileRef tile, ReagentPrototype reagent, FixedPoint2 reactVolume, List<ReagentData>? data)
    {
        var removed = FixedPoint2.Zero;
        var extinguished = FixedPoint2.Zero;

        if (tile.Tile.IsEmpty)
            return extinguished;

        foreach (var tileReaction in reagent.TileReactions)
        {
            var reactionRemoved = tileReaction.TileReact(tile, reagent, reactVolume - removed, EntityManager, data);
            removed += reactionRemoved;

            if (tileReaction is ExtinguishTileReaction or KsDeployFoamBlanketTileReaction)
                extinguished += reactionRemoved;

            if (removed >= reactVolume)
                break;
        }

        return extinguished;
    }
}
