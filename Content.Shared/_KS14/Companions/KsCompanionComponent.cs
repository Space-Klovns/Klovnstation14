using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Companions;

/// <summary>
///     On a companion: whose it is, and where it rides if it is attached. Added by the server's
///         <c>KsCompanionSystem</c> as it spawns one.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class KsCompanionComponent : Component
{
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public EntityUid? OwnerUid;

    /// <summary>
    ///     Its entry in the owner's <see cref="KsCompanionsComponent.Companions"/>.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public int Index;

    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public KsCompanionMode Mode;

    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public Vector2 Offset;

    /// <summary>
    ///     Server only: what a following companion goes after instead of its owner for now (a patient), or null.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? FollowOverride;

    /// <summary>
    ///     Server only: being taken away on purpose (the owner died, or lost the companions), so its going doesn't
    ///         count as being destroyed.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public bool Released;
}
