using Content.Server.Fluids.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Payload.Components;

namespace Content.Server.Payload.EntitySystems;

public sealed partial class PayloadSystem : EntitySystem
{
    [Dependency] private PuddleSystem _puddleSystem = default!;

    private bool TryGetBeakerSolution(EntityUid? beaker, out Entity<SolutionComponent>? soln, out Solution? solution)
    {
        soln = null;
        solution = null;

        if (beaker is not EntityUid beakerUid
            || !TryComp(beakerUid, out FitsInDispenserComponent? comp)
            || !_solutionContainerSystem.TryGetSolution(beakerUid, comp.Solution, out soln, out solution)
            || solution.Volume == 0)
        {
            soln = null;
            solution = null;
            return false;
        }

        return true;
    }

    private void HandleSingleBeakerChemicalPayload(Entity<ChemicalPayloadComponent> entity, Entity<SolutionComponent> soln)
    {
        _solutionContainerSystem.UpdateChemicals(soln);
        TrySpillChemicalPayload(entity, soln);
    }

    private void TrySpillChemicalPayload(Entity<ChemicalPayloadComponent> entity, Entity<SolutionComponent> soln)
    {
        if (!entity.Comp.Spill)
            return;

        var spilled = _solutionContainerSystem.SplitSolution(soln, soln.Comp.Solution.Volume);
        _puddleSystem.TrySplashSpillAt(entity, Transform(entity.Owner).Coordinates, spilled, out _);
    }
}
