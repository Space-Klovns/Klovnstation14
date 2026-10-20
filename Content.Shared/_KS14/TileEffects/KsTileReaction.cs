using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.TileEffects;

/// <summary>
///     A tile reaction that is also a <see cref="KsTileEffect"/>, so its <see cref="DependencyAttribute"/> fields are
///         injected rather than looked up through the <see cref="IEntityManager"/> on every reaction.
/// </summary>
/// <remarks>
///     Nothing initialises tile reactions when prototypes load, so each one injects itself the first time it reacts.
///         Listed under a reagent's <c>tileEffects</c>, it reacts whatever of the reagent the effects before it left.
/// </remarks>
public abstract partial class KsTileReaction : KsTileEffect, ITileReaction
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private SharedPuddleSystem _puddleSystem = default!;

    /// <summary>
    ///     Reacts up to <paramref name="reactVolume"/> of <paramref name="reagent"/> with the tile.
    /// </summary>
    /// <returns>How much of it was used up, at most <paramref name="reactVolume"/>.</returns>
    protected abstract FixedPoint2 React(TileRef tile, ReagentPrototype reagent, FixedPoint2 reactVolume, List<ReagentData>? data);

    public FixedPoint2 TileReact(TileRef tile,
        ReagentPrototype reagent,
        FixedPoint2 reactVolume,
        IEntityManager entityManager,
        List<ReagentData>? data = null)
    {
        if (!IsInitialized)
            entityManager.System<KsTileEffectSystem>().EnsureInitialized(this);

        return React(tile, reagent, reactVolume, data);
    }

    /// <remarks>
    ///     A reaction is by volume, not by time, so <paramref name="scale"/> does not change how much reacts. The
    ///         reaction sees the solution's temperature as its source's, as it does when a puddle's tile reactions run.
    /// </remarks>
    public override bool Execute(TileRef tileRef, float scale, ref KsTileEffectReagentData reagentData)
    {
        if (reagentData.Solution is not { } solution ||
            reagentData.Reagent is not { } reagent ||
            !_prototypeManager.TryIndex<ReagentPrototype>(reagent.Prototype, out var reagentPrototype))
            return false;

        var available = solution.GetReagentQuantity(reagent) - FixedPoint2.New(reagentData.RemovedVolume);
        if (available <= FixedPoint2.Zero)
            return false;

        FixedPoint2 used;
        using (_puddleSystem.KsBeginTileReactionTemperature(solution.Temperature))
            used = React(tileRef, reagentPrototype, available, reagent.Data);

        if (used <= FixedPoint2.Zero)
            return false;

        reagentData.RemovedVolume += (float)used;
        return true;
    }
}
