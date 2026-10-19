using Content.Server.Chemistry.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Map.Components;
using Robust.Shared.Spawners;

namespace Content.Server.Chemistry.EntitySystems;

internal sealed partial class VaporSystem
{
    /// <summary>
    ///     A vapor running out of time hands what it still carries to the tile it ends on.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnVaporDespawn(Entity<VaporComponent> entity, ref TimedDespawnEvent args)
    {
        KsReactRemainder(entity);
    }

    /// <summary>
    ///     Hands everything the vapor still carries to the tile it is on, through its reagents' tile reactions, rather
    ///         than it vanishing with the vapor. Whatever no reaction takes is lost with it, as before.
    /// </summary>
    /// <remarks>
    ///     A vapor gives each tile it crosses only a share of what it carries, so without this, a spray that hits
    ///         nothing loses the rest when it expires or stops against a wall.
    /// </remarks>
    private void KsReactRemainder(Entity<VaporComponent> entity)
    {
        if (!entity.Comp.Active ||
            !TryComp<SolutionComponent>(entity, out var solutionComponent) ||
            solutionComponent.Solution.Volume <= FixedPoint2.Zero)
            return;

        var transformComponent = Transform(entity);
        if (!TryComp(transformComponent.GridUid, out MapGridComponent? gridComponent))
            return;

        var tile = _map.GetTileRef(transformComponent.GridUid.Value, gridComponent, transformComponent.Coordinates);
        var contents = solutionComponent.Solution;
        using var temperatureScope = _puddleSystem.KsBeginTileReactionTemperature(contents.Temperature);

        foreach (var reagentQuantity in contents.Contents.ToArray())
        {
            if (reagentQuantity.Quantity <= FixedPoint2.Zero)
                continue;

            var reagent = ProtoMan.Index<ReagentPrototype>(reagentQuantity.Reagent.Prototype);
            var reaction = reagent.ReactionTile(tile, reagentQuantity.Quantity, EntityManager, reagentQuantity.Reagent.Data);
            if (reaction > reagentQuantity.Quantity)
                reaction = reagentQuantity.Quantity;

            _solutionContainer.RemoveReagent((entity.Owner, solutionComponent), reagentQuantity.Reagent, reaction);
        }
    }
}
