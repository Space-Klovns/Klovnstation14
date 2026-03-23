using System.Numerics;

namespace Content.Server._KS14.MovementIllusion;

/// <summary>
///     These things will move ETERNALLY
/// </summary>
[RegisterComponent]
[UnsavedComponent]
public sealed partial class MovementIllusionBanishedComponent : Component
{
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan DeleteTime = TimeSpan.MinValue;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public Vector2 Velocity = Vector2.Zero;
}
