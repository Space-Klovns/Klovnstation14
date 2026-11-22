using Content.Shared.Weapons.Ranged.Components;

namespace Content.Shared.Clumsy;

// Partial: whether a clumsy shooter's gun can backfire, for systems outside this one, kept apart from the upstream code.
public sealed partial class ClumsySystem
{
    /// <summary>
    ///     Whether <paramref name="shooter"/>'s clumsiness can make <paramref name="gun"/> backfire on them at all.
    /// </summary>
    public bool KsCanBackfire(Entity<ClumsyComponent?> shooter, GunComponent gun)
    {
        return Resolve(shooter, ref shooter.Comp, logMissing: false) &&
            shooter.Comp.ClumsyGuns &&
            !gun.ClumsyProof &&
            shooter.Comp.ClumsyDefaultCheck > 0f;
    }
}
