namespace Content.Shared._KS14.ZLevel.Light;

/// <summary>
///     How light is carried from one z-level to the ones below it.
/// </summary>
/// <remarks>
///     Two approaches exist because they fail differently, and which one reads better is a judgement that has
///         to be made with both of them on screen. <see cref="Both"/> is there to make that comparison
///         possible, not because running both is ever what you want.
/// </remarks>
public enum KsZLevelLightLeakMode : byte
{
    /// <summary>
    ///     Not at all.
    /// </summary>
    Off,

    /// <summary>
    ///     Stand-in point lights placed at the gaps in the floor, each one solved from the real light's radius
    ///         and energy.
    /// </summary>
    /// <remarks>
    ///     Sharp and physically derived, but costs an entity per gap per light, and cannot carry light
    ///         downwards from a z-level the client was never sent.
    /// </remarks>
    StandIns,

    /// <summary>
    ///     The finished light map of the z-level above, masked by its own floor and composited into this one.
    /// </summary>
    /// <remarks>
    ///     Softer, indifferent to how many lights there are, and works in both directions - but costs an extra
    ///         render pass per z-level.
    /// </remarks>
    Buffer,

    /// <summary>
    ///     Both at once. For comparing them, not for shipping.
    /// </summary>
    Both,
}
