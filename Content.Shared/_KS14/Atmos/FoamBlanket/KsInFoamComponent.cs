namespace Content.Shared._KS14.Atmos.FoamBlanket;

/// <summary>
///     Something flammable that is touching at least one foam blanket. While it is, it cannot catch fire, and if it
///         is already burning it goes out (see <see cref="Content.Shared._KS14.Atmos.Flammable.KsBurnAttemptEvent"/>).
/// </summary>
/// <remarks>
///     Kept by each side from its own contacts, so it is never networked: the server's decides, and the client's
///         follows whatever the client predicts. Only flammable entities get it, since nothing else reads it.
///     Never saved: the contacts that make it come back on their own once a map is loaded.
/// </remarks>
[RegisterComponent, UnsavedComponent]
[Access(typeof(SharedKsFoamBlanketSystem), Other = AccessPermissions.Read)]
public sealed partial class KsInFoamComponent : Component
{
    /// <summary>
    ///     The blankets it is touching. It is removed once this is empty.
    /// </summary>
    [ViewVariables]
    public readonly HashSet<EntityUid> Blankets = [];
}
