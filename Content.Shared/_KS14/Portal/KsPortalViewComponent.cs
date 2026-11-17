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
    ///     The sensor fixture that nominates travellers has to match this, so it is only ever changed through
    ///         <see cref="KsPortalTravelSystem.SetWidth"/>, which resizes the sensor with it. Read-only in the VV
    ///         window for the same reason: it writes by reflection, past <c>[Access]</c>, and nothing hears about
    ///         it on the server. <c>vvwrite /entity/&lt;uid&gt;/KsPortalView/Width &lt;value&gt;</c> does work,
    ///         because the system registers that path to go through <see cref="KsPortalTravelSystem.SetWidth"/>.
    ///     The 3-wide box in the prototype is only a placeholder for the default width; the sensor is sized to the
    ///         real one when the portal is map-initialised.
    /// </remarks>
    [DataField, AutoNetworkedField]
    [Access(typeof(KsPortalTravelSystem))]
    [ViewVariables(VVAccess.ReadOnly)]
    public float Width = 1f;

    /// <summary>
    ///     Whether the portal shows its destination and carries entities, and is sent the destination's
    ///         surroundings by PVS.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Enabled = true;

    /// <summary>
    ///     Whether the doorway has no direction of its own and is instead turned square to whoever uses it: to
    ///         the viewer when drawn, and to where a traveller first came near it when crossed.
    /// </summary>
    /// <remarks>
    ///     For portals that are always at rotation zero and are walked into from any side, such as the hand
    ///         teleporter's. Only the doorway turns: where a point ends up still follows the two portals' real
    ///         rotations, which for a pair of such portals is a plain translation, so the view through it looks
    ///         right from every side.
    /// </remarks>
    [DataField, AutoNetworkedField]
    [Access(typeof(KsPortalTravelSystem))]
    public bool FacesViewer;

    /// <summary>
    ///     Whether a player made this portal, in which case <see cref="Enabled"/> follows
    ///         <see cref="Content.Shared._KS14.CCVar.KsCCVars.PortalPlayerViews"/>, and the stock portal behaviour takes over while
    ///         it is off.
    /// </summary>
    [DataField, AutoNetworkedField]
    [Access(typeof(KsPortalTravelSystem))]
    public bool PlayerMade;
}
