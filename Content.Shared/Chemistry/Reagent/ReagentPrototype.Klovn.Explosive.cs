namespace Content.Shared.Chemistry.Reagent;

public sealed partial class ReagentPrototype
{
    /// <summary>
    ///     How much one unit of this reagent adds to the explosion of a container that holds it, against welding fuel's 1.
    ///         0 for anything that doesn't explode.
    /// </summary>
    /// <seealso cref="KsCookOffTemperature"/>
    [DataField("ksExplosivePower")]
    public float KsExplosivePower;

    /// <summary>
    ///     A container's contents explode on their own once they are hotter than this, in kelvin, if this reagent is explosive.
    ///         Null for never.
    /// </summary>
    [DataField("ksCookOffTemperature")]
    public float? KsCookOffTemperature;
}
