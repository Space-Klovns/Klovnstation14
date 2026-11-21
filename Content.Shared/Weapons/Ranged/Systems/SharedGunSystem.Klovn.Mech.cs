using Content.Shared.Mech.Components;

namespace Content.Shared.Weapons.Ranged.Systems;

public abstract partial class SharedGunSystem
{
    [Dependency] private EntityQuery<MechComponent> _ksMechQuery = default!;

    /// <summary>
    ///     The entity whose client predicts a shot fired by <paramref name="shooterUid"/>: a mech's pilot for the mech,
    ///         and otherwise the shooter itself. A mech's weapon shoots as the mech, which no player is attached to, so
    ///         what is kept from the predicting client (the server's gunshot, its muzzle flash) or shown only to it (an
    ///         empty-gun popup), and whose ping a shot is compensated by, goes by this instead.
    /// </summary>
    public EntityUid? KsGetPredictingUser(EntityUid? shooterUid)
    {
        if (shooterUid is { } mechUid &&
            _ksMechQuery.TryComp(mechUid, out var mechComponent) &&
            mechComponent.PilotSlot?.ContainedEntity is { } pilotUid)
        {
            return pilotUid;
        }

        return shooterUid;
    }
}
