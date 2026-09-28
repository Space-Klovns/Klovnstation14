using Content.Server._KS14.NPC.Systems;

namespace Content.Server._KS14.NPC.Components;

/// <summary>
///     Gives an NPC a reaction time: a target it has only just spotted is not engaged until it has been in sight
///         for <see cref="ReactionTime"/>, unless the NPC is already alert. See <see cref="NpcReactionTimeSystem"/>.
/// </summary>
[RegisterComponent]
[Access(typeof(NpcReactionTimeSystem))]
public sealed partial class NpcReactionTimeComponent : Component
{
    /// <summary>
    ///     How long a target has to be in sight before a non-alert NPC reacts to it.
    /// </summary>
    [DataField]
    public TimeSpan ReactionTime = TimeSpan.FromSeconds(0.6);

    /// <summary>
    ///     A target out of sight for longer than this counts as newly spotted when it reappears.
    /// </summary>
    [DataField]
    public TimeSpan ForgetTime = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     How much longer a dimly lit target takes to react to, with light detection on
    ///         (see <see cref="NpcLightDetectionSystem"/>): the reaction time is scaled by
    ///         <c>1 + DarknessReactionScale * (1 - light level)</c>, so at 1 a target in near-darkness takes about
    ///         twice as long. 0 ignores light.
    /// </summary>
    [DataField]
    public float DarknessReactionScale = 1f;

    /// <summary>
    ///     Within this many tiles, how dark a target is makes no difference to the reaction time.
    /// </summary>
    [DataField]
    public float DarknessProximityRange = 2.5f;

    /// <summary>
    ///     Each target currently being tracked.
    /// </summary>
    [ViewVariables]
    public Dictionary<EntityUid, NpcSighting> Sightings = new();
}

/// <summary>
///     When a target was first and most recently seen, and whether the NPC has reacted to it yet. Once it has, it
///         stays reacted until it forgets the target: a target it is already fighting does not become a surprise
///         again by stepping into shadow.
/// </summary>
public record struct NpcSighting(TimeSpan FirstSeen, TimeSpan LastSeen, bool Reacted);
