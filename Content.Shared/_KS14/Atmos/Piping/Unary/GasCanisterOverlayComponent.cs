using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Atmos.Piping.Unary;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true)] // KS14: Added fielddeltas
public sealed partial class GasCanisterOverlayComponent : Component
{
    /// <summary>
    ///     Length of this should be the same as SharedGasTileOverlaySystem.VisibleGasId
    /// </summary>
    [AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public byte[] AppearanceGasPercentages = []; // empty array is placeholder; it is properly initialised in ComponentInit

    /// <summary>
    ///     Networked total moles
    /// </summary>
    [AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float NetworkedMoles = 0f;

    /// <summary>
    ///     Arbitrary state of fire in the can not based on the value of same name
    ///         in atmos system.
    /// </summary>
    [AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public byte FireState = 0;
}
