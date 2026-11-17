namespace Content.Server.Atmos.Components;

public sealed partial class HeatExchangerComponent
{
    /// <summary>
    ///     The share of its heat transfer a radiator manages for latent heat, condensing or boiling what passes it.
    /// </summary>
    /// <remarks>
    ///     A radiator brings each tick's small packet of gas all the way to its surroundings' temperature, so its rated
    ///         coefficients never limit sensible heat. Latent heat is far more, and at full strength the radiators of a
    ///         condenser would condense at their surroundings' temperature exactly, leaving the liquid no margin below its
    ///         line's saturation pressure: the pressure drop along the condenser then boils it again before it reaches the
    ///         valve. Limited, a condenser runs warmer than its surroundings, as a real one does, and its liquid leaves
    ///         subcooled. The default gives about 1 kW/K at the default heat scale.
    /// </remarks>
    [DataField]
    public float KsLatentHeatTransferScale = 1f / 64f;
}
