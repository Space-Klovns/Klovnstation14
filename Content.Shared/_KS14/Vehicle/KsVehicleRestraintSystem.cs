using Content.Shared.Cuffs.Components;

namespace Content.Shared._KS14.Vehicle;

/// <summary>
///     Keeps restrained entities from driving. A vehicle that needs no hands (an office chair) doesn't otherwise ask,
///         so a prisoner buckled to one could roll off in it.
/// </summary>
public sealed partial class KsVehicleRestraintSystem : EntitySystem
{
    /// <remarks>
    ///     Cuffing calls <c>UpdateCanMove</c>, which re-runs this for a current operator and stops them driving.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnCanOperateVehicle(Entity<CuffableComponent> entity, ref KsCanOperateVehicleEvent args)
    {
        if (!entity.Comp.CanStillInteract)
            args.Cancelled = true;
    }
}
