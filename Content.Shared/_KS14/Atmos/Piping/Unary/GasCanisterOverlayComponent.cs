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

    /// <summary>
    ///     How far up its window the canister's liquid shows, from 0 to <see cref="byte.MaxValue"/>.
    /// </summary>
    [AutoNetworkedField, ViewVariables]
    public byte LiquidFill;

    /// <summary>
    ///     How far up its window the canister's ice shows, under the liquid.
    /// </summary>
    [AutoNetworkedField, ViewVariables]
    public byte SolidFill;

    /// <summary>
    ///     The liquid's colour: its reagents' colours, weighted by how much of each there is.
    /// </summary>
    [AutoNetworkedField, ViewVariables]
    public Color LiquidColor = Color.White;

    /// <summary>
    ///     Units of liquid and ice that fill the window. Physically a canister holds far more (1500 L at 5 mL/u), which
    ///         would never show, so the window reads as a gauge instead.
    /// </summary>
    [DataField]
    public float FullWindowUnits = 1000f;
}
