using Content.Server._Starlight.Plumbing.Components;
using Content.Server._Starlight.Plumbing.Nodes;
using Content.Server.NodeContainer.EntitySystems;
using Content.Shared._Starlight.Plumbing.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.NodeContainer;
using JetBrains.Annotations;

namespace Content.Server._Starlight.Plumbing.EntitySystems;

/// <summary>
///     Handles plumbing inlet behavior: actively pulls reagents from inlet nodes into a solution.
/// </summary>
[UsedImplicitly]
public sealed class PlumbingInletSystem : EntitySystem
{
    [Dependency] private readonly SharedSolutionContainerSystem _solutionSystem = default!;
    [Dependency] private readonly PlumbingPullSystem _pullSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlumbingInletComponent, PlumbingDeviceUpdateEvent>(OnInletUpdate);
    }

    private void OnInletUpdate(Entity<PlumbingInletComponent> ent, ref PlumbingDeviceUpdateEvent args)
    {
        // When the pill press is in mixing mode, don't pull from the normal inlet —
        // mixing inlets handle all pulling instead.
        if (TryComp<PlumbingPillPressComponent>(ent.Owner, out var pillPress) && pillPress.MixingEnabled)
            return;

        // Get our solution
        if (!_solutionSystem.TryGetSolution(ent.Owner, ent.Comp.SolutionName, out var solutionEnt, out var solution))
            return;
            
        // Make sure there is room in pull in reagents 
        if (solution.AvailableVolume <= 0)
            return;

        // Get node container to find the inlet node
        if (!TryComp<NodeContainerComponent>(ent.Owner, out var nodeContainer))
            return;

        // Get the inlet node directly
        if (!nodeContainer.Nodes.TryGetValue(ent.Comp.InletName, out var node))
            return;

        if (node is not PlumbingNode plumbingNode || plumbingNode.PlumbingNet == null)
            return;

        var (_, nextIndex) = _pullSystem.PullFromNetwork(ent.Owner, plumbingNode.PlumbingNet, solutionEnt.Value, ent.Comp.TransferAmount, ent.Comp.RoundRobinIndex);
        ent.Comp.RoundRobinIndex = nextIndex;
    }
}
