namespace Content.Client._KS14.Portal;

/// <summary>
///     Attached to a view portal whose doorway light is currently being carried out of its linked portal,
///         holding the stand-in lights doing it.
/// </summary>
/// <remarks>
///     Client-side only, and added and removed by <see cref="KsPortalLightTransferSystem"/> alone: its presence
///         means "this portal has stand-ins that need cleaning up", which is the only reason it exists.
/// </remarks>
[RegisterComponent]
[Access(typeof(KsPortalLightTransferSystem))]
public sealed partial class KsPortalLightTransferComponent : Component
{
    /// <summary>
    ///     The stand-ins at the linked portal, brightest first.
    /// </summary>
    [ViewVariables]
    public List<EntityUid> StandIns = [];
}
