namespace Content.Shared._KS14.Atmos;

/// <summary>
///     Class to store atmos constants for KS14.
/// </summary>
public static class KsAtmospherics
{
    /// <summary>
    ///     Zipion reaction begins when you reach this temperature or lower
    /// </summary>
    public const float ZipionProductionThresholdTemperature = 200f;
    /// <summary>
    ///     Zipion reaction rate - 1/x of the plasma is converted into Zipion each tick
    /// </summary>
    public const float ZipionProductionConversionRate = 40f;
    /// <summary>
    ///     The default molar flow at which molar equipment maxes out at, in mol/s.
    /// </summary>
    public const float MaxMolarFlow = 400;
}
