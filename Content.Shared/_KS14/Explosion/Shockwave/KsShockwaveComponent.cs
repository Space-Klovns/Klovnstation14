using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Explosion.Shockwave;

/*
    The original version of this source code was ported from
        https://github.com/RMC-14/RMC-14/ at commit 2066df33076c46e67bed4770d7c14ebf107c643b
*/

[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class KsShockwaveComponent : Component
{
    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan StartTime;

    /// <summary>
    ///     The rate at which the wave fades, lower values means it's active for longer.
    /// </summary>
    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float FalloffPower = 40.0f;

    /// <summary>
    ///     How sharp the wave distortion is. Higher values make the wave more pronounced.
    /// </summary>
    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float Sharpness = 10.0f;

    /// <summary>
    ///     Width of the wave.
    /// </summary>
    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float Width = 0.8f;
}
