using Content.Server._Starlight.Plumbing.Components;
using Content.Server._Starlight.Plumbing.Nodes;
using Content.Server.NodeContainer.EntitySystems;
using Content.Shared._Starlight.Plumbing.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using JetBrains.Annotations;

namespace Content.Server._Starlight.Plumbing.EntitySystems;

/// <summary>
///     Handles plumbing tank machine behavior: actively trys to pull all reagents from its inlet network.
///     Other machines can pull from this tank via its outlets.
/// </summary>
[UsedImplicitly]
public sealed class PlumbingTankSystem : EntitySystem
{
    [Dependency] private readonly SharedSolutionContainerSystem _solutionSystem = default!;
    [Dependency] private readonly NodeContainerSystem _nodeContainer = default!;
    [Dependency] private readonly PlumbingPullSystem _pullSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlumbingTankComponent, PlumbingDeviceUpdateEvent>(OnTankUpdate);
    }

    private void OnTankUpdate(Entity<PlumbingTankComponent> ent, ref PlumbingDeviceUpdateEvent args)
    {
        // Get our tank solution
        if (!_solutionSystem.TryGetSolution(ent.Owner, ent.Comp.SolutionName, out var tankSolutionEnt, out var tankSolution))
            return;

        // Check if we have space (MaxVolume == 0 means unlimited). Changed to make chemmaster plumbing work
        if (tankSolution.MaxVolume != FixedPoint2.Zero && tankSolution.AvailableVolume <= 0)
            return;

        // Get the inlet node
        if (!_nodeContainer.TryGetNode<PlumbingNode>(ent.Owner, ent.Comp.InletName, out var inletNode))
            return;

        if (inletNode.PlumbingNet == null)
            return;

        var (_, nextIndex) = _pullSystem.PullFromNetwork(ent.Owner, inletNode.PlumbingNet, tankSolutionEnt.Value, ent.Comp.TransferAmount, ent.Comp.RoundRobinIndex);
        ent.Comp.RoundRobinIndex = nextIndex;
    }
}
