using Robust.Shared.Player;

namespace Content.Server._KS14.Portal;

/// <summary>
///     The sessions <see cref="KsPortalPvsSystem"/> has view-subscribed to the portal this one is linked to, so
///         that they can be taken off it again.
/// </summary>
[RegisterComponent, Access(typeof(KsPortalPvsSystem))]
public sealed partial class KsPortalViewSubscriptionsComponent : Component
{
    /// <summary>
    ///     The portal this one is linked to, whose surroundings the sessions are sent.
    /// </summary>
    [ViewVariables]
    public EntityUid Target = EntityUid.Invalid;

    /// <summary>
    ///     The entity parented to <see cref="Target"/> that the sessions are actually view-subscribed to. It
    ///         carries an eye with a reduced PVS range, so only a window's worth of the destination is sent.
    /// </summary>
    [ViewVariables]
    public EntityUid Viewer = EntityUid.Invalid;

    [ViewVariables]
    public readonly HashSet<ICommonSession> Sessions = new();
}
