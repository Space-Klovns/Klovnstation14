namespace Content.Shared._KS14.Weather;

/// <summary>
///     On a weather status effect: what it leaves on the screen of a player standing out in it, as if on a visor. Read by
///         the client's visor overlay, scaled by how far the weather has faded in or out. Only the client uses it, from
///         the weather's prototype.
/// </summary>
[RegisterComponent]
public sealed partial class KsVisorPrecipitationComponent : Component
{
    /// <summary>
    ///     Raindrops hitting the visor per second, at full strength.
    /// </summary>
    [DataField]
    public float DropsPerSecond;

    /// <summary>
    ///     Wet snowflakes sticking to the visor per second, at full strength. Each melts into a drop.
    /// </summary>
    [DataField]
    public float FlakesPerSecond;

    /// <summary>
    ///     How densely the fine mist of tiny droplets covers the visor, 0 to 1, once it has built up.
    /// </summary>
    [DataField]
    public float Mist;

    /// <summary>
    ///     Scales the size of the drops it leaves: above 1 for heavy rain's fat drops.
    /// </summary>
    [DataField]
    public float DropScale = 1f;
}
