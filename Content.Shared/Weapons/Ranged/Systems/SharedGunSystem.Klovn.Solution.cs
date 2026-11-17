using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Weapons.Ranged.Components;

namespace Content.Shared.Weapons.Ranged.Systems;

public abstract partial class SharedGunSystem
{
    [Dependency] private SharedSolutionContainerSystem _ksSolutionContainerSystem = default!;
    [Dependency] private SharedVaporSystem _ksVaporSystem = default!;

    /// <summary>
    ///     Fills a solution gun's shot from the gun, as the server alone used to. Shared, so that the shooter's own
    ///         predicted shot carries the same reagents as the server's and its vapor reacts with tiles as that one does.
    /// </summary>
    private void KsFillSolutionShot(Entity<SolutionAmmoProviderComponent> gun, EntityUid shotUid)
    {
        if (_ksSolutionContainerSystem.TryGetSolution(gun.Owner, gun.Comp.SolutionId, out var solution))
            _ksVaporSystem.TryAddSolution(shotUid, solution.Value, gun.Comp.FireCost);
    }
}
