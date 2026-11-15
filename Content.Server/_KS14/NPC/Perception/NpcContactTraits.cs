namespace Content.Server._KS14.NPC.Perception;

/// <summary>
///     What an NPC made of a hostile the last time it saw it: what a squad deciding how to go in after it, once it is out of
///         sight, goes by. Never read from the hostile itself while it is out of sight.
/// </summary>
[Flags]
public enum NpcContactTraits : byte
{
    None = 0,

    /// <summary>
    ///     These were worked out from a sighting. Without it, nothing is known: a hostile only ever called out.
    /// </summary>
    Seen = 1 << 0,

    /// <summary>
    ///     It had a gun to hand.
    /// </summary>
    Armed = 1 << 1,

    /// <summary>
    ///     A flash would have blinded it.
    /// </summary>
    Flashable = 1 << 2,

    /// <summary>
    ///     It breathes, and was not on internals: gas would get it.
    /// </summary>
    Breathes = 1 << 3,

    /// <summary>
    ///     Smoke does not stop its eyes.
    /// </summary>
    SeesThroughSmoke = 1 << 4,
}
