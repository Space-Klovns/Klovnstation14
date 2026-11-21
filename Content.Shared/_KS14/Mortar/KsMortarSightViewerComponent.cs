using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Mortar;

/// <summary>
///     On someone looking through a mortar's sight: the eye they see their aim through, for each mortar whose window they
///         have open. Sent to that player alone, so nobody else learns whose eye is whose. Kept by the server's mortar
///         system, which adds it when a sight opens and removes it with the last one.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class KsMortarSightViewerComponent : Component
{
    public override bool SendOnlyToOwner => true;

    /// <summary>
    ///     The eye, by mortar.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public Dictionary<EntityUid, EntityUid> Eyes = new();
}
