namespace Content.Server._KS14.NPC.Hiding;

/// <summary>
///     Lets an NPC that is hurt sometimes get into a closet to use its meds, and stay in until they have worked or a hostile
///         is nearly on top of it, rather than falling back somewhere in the open. See <see cref="NpcHidingSystem"/>, and
///         the closet branch of operative healing.
/// </summary>
[RegisterComponent]
public sealed partial class NpcClosetHealerComponent : Component
{
    /// <summary>
    ///     The chance, from 0 to 1, that it heals in a closet, when there is one it can get into. Rolled once a heal.
    /// </summary>
    [DataField]
    public float Chance = 0.25f;

    /// <summary>
    ///     How near a hostile has to come, in tiles, for it to get out: near enough to hear.
    /// </summary>
    [DataField]
    public float ExitRange = 2f;

    /// <summary>
    ///     The furthest a closet may be, in tiles.
    /// </summary>
    [DataField]
    public float SearchRange = 8f;

    /// <summary>
    ///     The longest it stays in, whatever else.
    /// </summary>
    [DataField]
    public TimeSpan MaxTime = TimeSpan.FromSeconds(40);

    /// <summary>
    ///     How long it may be in without its meds having started to work before it gives up waiting for them, and gets out.
    /// </summary>
    [DataField]
    public TimeSpan MedsGrace = TimeSpan.FromSeconds(6);
}
