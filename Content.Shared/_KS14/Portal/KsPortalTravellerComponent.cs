using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Portal;

/// <summary>
///     Put on an entity that is near one or more view portals, remembering which side of each portal's line it
///         was last on, so that <see cref="KsPortalTravelSystem"/> can tell when it crosses one.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(KsPortalTravelSystem))]
public sealed partial class KsPortalTravellerComponent : Component
{
    /// <summary>
    ///     Every portal whose doorway this entity may cross, with the sign of its position along that portal's
    ///         local Y axis when last looked at: 1, -1, or 0 when not known yet.
    /// </summary>
    /// <remarks>
    ///     All of them rather than only the nearest: two portals can be close enough that an entity coming out
    ///         of one is still near it when it reaches the other, as in a short looping hallway, and watching
    ///         only one would let it walk straight through the other.
    /// </remarks>
    [DataField, AutoNetworkedField]
    public Dictionary<EntityUid, int> Sides = [];
}
