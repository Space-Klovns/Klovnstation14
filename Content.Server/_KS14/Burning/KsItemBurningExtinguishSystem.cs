using Content.Server.Chemistry.TileReactions;
using Content.Shared._KS14.Burning;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Burning;

/// <summary>
///     Puts burning items out with whatever puts tile fires out: a reagent with an
///         <see cref="ExtinguishTileReaction"/>, sprayed on the item or standing in a pool the item lies in.
///
///     Server only, as the tile reaction is. A burning item has no <c>Flammable</c> or <c>Reactive</c> component, so
///         neither the <c>Extinguish</c> entity effect nor a reagent's reactive effects ever reach it.
/// </summary>
public sealed partial class KsItemBurningExtinguishSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private KsItemBurningSystem _itemBurningSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;
    [Dependency] private EntityQuery<MapGridComponent> _mapGridQuery = default!;

    /// <summary>
    ///     A fire extinguisher's vapor, a thrown bucket, a puddle splash.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnReaction(Entity<KsItemBurningComponent> entity, ref ReactionEntityEvent args)
    {
        if (args.Method == ReactionMethod.Touch && Extinguishes(args.Reagent))
            _itemBurningSystem.Extinguish(entity.AsNullable());
    }

    /// <summary>
    ///     A water tile: nothing burns while it lies in a pool of something that puts fires out.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnBurnAttempt(Entity<KsItemBurningComponent> entity, ref KsItemBurnAttemptEvent args)
    {
        if (!args.Cancelled && IsInExtinguishingPool(entity.Owner))
            args.Cancelled = true;
    }

    private bool IsInExtinguishingPool(EntityUid uid)
    {
        var transformComponent = Transform(uid);

        // Parented to the grid, or it is inside something and not lying in anything.
        if (transformComponent.GridUid is not { } gridUid ||
            transformComponent.ParentUid != gridUid ||
            !_mapGridQuery.TryComp(gridUid, out var mapGridComponent))
            return false;

        var tileIndices = _mapSystem.TileIndicesFor(gridUid, mapGridComponent, transformComponent.Coordinates);
        var anchoredEnumerator = _mapSystem.GetAnchoredEntitiesEnumerator(gridUid, mapGridComponent, tileIndices);

        while (anchoredEnumerator.MoveNext(out var anchoredUid))
        {
            if (!_solutionContainerSystem.TryGetDrainableSolution(anchoredUid.Value, out _, out var solution))
                continue;

            foreach (var reagentQuantity in solution.Contents)
            {
                if (_prototypeManager.TryIndex<ReagentPrototype>(reagentQuantity.Reagent.Prototype, out var reagentPrototype) &&
                    Extinguishes(reagentPrototype))
                    return true;
            }
        }

        return false;
    }

    private static bool Extinguishes(ReagentPrototype reagentPrototype)
    {
        foreach (var tileReaction in reagentPrototype.TileReactions)
        {
            if (tileReaction is ExtinguishTileReaction)
                return true;
        }

        return false;
    }
}
