using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Portal;

/// <summary>
///     A two-sided doorway to the entity this one is linked to (<see cref="Content.Shared.Teleportation.Components.LinkedEntityComponent"/>).
///         The client draws the area around the linked portal inside the doorway, and
///         <see cref="KsPortalTravelSystem"/> carries entities across it seamlessly.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class KsPortalViewComponent : Component
{
    /// <summary>
    ///     Length, in world units along this portal's local X axis, of the doorway segment centred on the portal.
    ///         The destination is shown behind this segment and behind its shadow as seen from the viewer, like
    ///         an occluder's, from either side; an entity whose centre crosses the segment travels.
    /// </summary>
    /// <remarks>
    ///     The sensor fixture that nominates travellers is resized to this width by
    ///         <see cref="KsPortalTravelSystem.RefreshSensor"/> when the portal is map-initialised (server side; the client gets the fixture by state);
    ///         the 3-wide box in the prototype is only a placeholder for the default width. Changing the width
    ///         on a live portal needs a call to <see cref="KsPortalTravelSystem.RefreshSensor"/> afterwards.
    /// </remarks>
    [DataField, AutoNetworkedField]
    public float Width = 1f;

    /// <summary>
    ///     Whether the portal shows its destination and carries entities, and is sent the destination's
    ///         surroundings by PVS.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Enabled = true;
}
