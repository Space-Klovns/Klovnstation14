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
        // Get our solution
        if (!_solutionSystem.TryGetSolution(ent.Owner, ent.Comp.SolutionName, out var solutionEnt, out var solution))
            return;
            
        // Make sure there is room in pull in reagents 
        if (solution.AvailableVolume <= 0)
            return;

        // Get node container to find all inlet nodes
        if (!TryComp<NodeContainerComponent>(ent.Owner, out var nodeContainer))
            return;

        // Pull from all inlet nodes (any node starting with the inlet prefix)
        foreach (var (nodeName, node) in nodeContainer.Nodes)
        {
            if (!nodeName.StartsWith(ent.Comp.InletPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            if (node is not PlumbingNode plumbingNode || plumbingNode.PlumbingNet == null)
                continue;

            if (solution.AvailableVolume <= 0)
                break;

            var (_, nextIndex) = _pullSystem.PullFromNetwork(ent.Owner, plumbingNode.PlumbingNet, solutionEnt.Value, ent.Comp.TransferAmount, ent.Comp.RoundRobinIndex);
            ent.Comp.RoundRobinIndex = nextIndex;
        }
    }
}
