using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.MovementIllusion;

/// <summary>
///     These things will move ETERNALLY
/// </summary>
[RegisterComponent, NetworkedComponent]
[UnsavedComponent]
public sealed partial class MovementIllusionBanishedComponent : Component
{
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public Vector2 Velocity = Vector2.Zero;
}
