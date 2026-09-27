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
    ///     When each target currently being tracked was first and most recently seen.
    /// </summary>
    [ViewVariables]
    public Dictionary<EntityUid, (TimeSpan FirstSeen, TimeSpan LastSeen)> Sightings = new();
}
