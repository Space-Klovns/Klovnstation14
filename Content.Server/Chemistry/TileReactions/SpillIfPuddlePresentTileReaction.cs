using Content.Server.Fluids.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using JetBrains.Annotations;
using Robust.Shared.Map;

namespace Content.Server.Chemistry.TileReactions
{
    [UsedImplicitly]
    [DataDefinition]
    public sealed partial class SpillIfPuddlePresentTileReaction : ITileReaction
    {
        public FixedPoint2 TileReact(TileRef tile,
            ReagentPrototype reagent,
            FixedPoint2 reactVolume,
            IEntityManager entityManager,
            List<ReagentData>? data)
        {
            var spillSystem = entityManager.System<PuddleSystem>();
            if (!spillSystem.TryGetPuddle(tile, out _))
                return FixedPoint2.Zero;

            // KS14 start: spill at the temperature of the liquid being reacted, not the puddle's: the reagent brings its
            //     own heat, so the puddle ends at the mean of the two. The caller says it through
            //     KsBeginTileReactionTemperature; with no scope open this falls back to upstream's 293.15 K default.
            var spilledSolution = new Solution(reagent.ID, reactVolume, data);
            if (spillSystem.KsTileReactionSourceTemperature is { } sourceTemperature)
                spilledSolution.Temperature = sourceTemperature;
            // KS14 end

            return spillSystem.TrySpillAt(tile, spilledSolution /* KS14: was new Solution(reagent.ID, reactVolume, data) */, out _, sound: false, tileReact: false)
                ? reactVolume
                : FixedPoint2.Zero;
        }
    }
}
