using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Portal;

/// <summary>
///     Put on an entity that is near a view portal, remembering which portal it is watching and which side of
///         the portal's line it was last on, so that <see cref="KsPortalTravelSystem"/> can tell when it crosses.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(KsPortalTravelSystem))]
public sealed partial class KsPortalTravellerComponent : Component
{
    /// <summary>
    ///     The portal whose doorway this entity may cross.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Portal;

    /// <summary>
    ///     Sign of the entity's position along the portal's local Y axis when last looked at: 1, -1, or 0 when
    ///         not known yet.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int Side;
}
