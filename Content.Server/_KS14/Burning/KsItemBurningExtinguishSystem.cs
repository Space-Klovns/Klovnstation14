using Content.Server.Chemistry.TileReactions;
using Content.Shared._KS14.Burning;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.EntityEffects.Effects.Atmos;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Burning;

/// <summary>
///     Puts burning items out with whatever puts other fires out: a reagent that extinguishes, sprayed on the item
///         or standing in a pool the item lies in.
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
    ///     A fire extinguisher's shot or a spray bottle's vapor hitting the item, a thrown bucket, a puddle splash.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnReaction(Entity<KsItemBurningComponent> entity, ref ReactionEntityEvent args)
    {
        if (args.Method == ReactionMethod.Touch && Extinguishes(args.Reagent))
            _itemBurningSystem.Extinguish(entity.AsNullable());
    }

    /// <summary>
    ///     Nothing burns while it lies in a pool of something that puts fires out, such as a water tile.
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

    /// <summary>
    ///     Whether the reagent puts fires out: tile fires, like water, or whoever it touches, like firefighting foam.
    /// </summary>
    private static bool Extinguishes(ReagentPrototype reagentPrototype)
    {
        foreach (var tileReaction in reagentPrototype.TileReactions)
        {
            if (tileReaction is ExtinguishTileReaction)
                return true;
        }

        if (reagentPrototype.ReactiveEffects == null)
            return false;

        foreach (var reactiveEffects in reagentPrototype.ReactiveEffects.Values)
        {
            if (!reactiveEffects.Methods.Contains(ReactionMethod.Touch))
                continue;

            foreach (var effect in reactiveEffects.Effects)
            {
                if (effect is Extinguish)
                    return true;
            }
        }

        return false;
    }
}
