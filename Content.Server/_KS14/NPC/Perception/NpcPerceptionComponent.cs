namespace Content.Server._KS14.NPC.Perception;

/// <summary>
///     Gives an NPC a memory of the hostiles around it - which it can see, which it has lost and where, which it saw
///         hide - and a reaction time before it engages one it has only just spotted. Kept up to date by
///         <see cref="NpcPerceptionSystem"/>; HTN reads it through <c>PerceivedContactsQuery</c> and friends.
/// </summary>
[RegisterComponent]
[Access(typeof(NpcPerceptionSystem))]
public sealed partial class NpcPerceptionComponent : Component
{
    /// <summary>
    ///     How often what the NPC can see is re-checked.
    /// </summary>
    [DataField]
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.2);

    /// <summary>
    ///     How long a hostile out of sight is remembered.
    /// </summary>
    [DataField]
    public TimeSpan MemoryTime = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     How long a hostile believed to be in a container is remembered.
    /// </summary>
    [DataField]
    public TimeSpan ConcealedMemoryTime = TimeSpan.FromSeconds(60);

    #region Reaction

    /// <summary>
    ///     How long a calm NPC takes to react to a hostile after noticing it. The hostile does not have to stay in
    ///         sight meanwhile: a glimpse is noticed, and reacted to this long after.
    /// </summary>
    [DataField]
    public TimeSpan ReactionTime = TimeSpan.FromSeconds(0.6);

    /// <summary>
    ///     A hostile out of sight for longer than this needs a fresh reaction when it reappears.
    /// </summary>
    [DataField]
    public TimeSpan ReactionForgetTime = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     How much longer a dimly lit hostile takes to react to, with light detection on: the reaction time is
    ///         scaled by <c>1 + DarknessReactionScale * (1 - light level)</c>. 0 ignores light.
    /// </summary>
    [DataField]
    public float DarknessReactionScale = 1f;

    /// <summary>
    ///     A virtual marker meaning the NPC is already in combat, and so reacts at once. Null to ignore markers.
    /// </summary>
    [DataField]
    public string? AlertMarker = "OpInCombat";

    /// <summary>
    ///     How recently the NPC's squad must have had contact for it to react at once.
    /// </summary>
    [DataField]
    public TimeSpan SquadAlertWindow = TimeSpan.FromSeconds(20);

    #endregion

    #region Darkness

    /// <summary>
    ///     Within this many tiles a hostile is seen however dark it is.
    /// </summary>
    [DataField]
    public float ProximityRange = 2.5f;

    /// <summary>
    ///     The least light, from 0 to 1, a hostile needs to be spotted from further than <see cref="ProximityRange"/>.
    /// </summary>
    [DataField]
    public float MinimumLightLevel = 0.03f;

    /// <summary>
    ///     A hostile moving at least this fast, in tiles per second, is spotted however dark it is. Between walking
    ///         and sprinting, so running through the dark gives you away and walking does not.
    /// </summary>
    [DataField]
    public float RevealSpeed = 4f;

    /// <summary>
    ///     How long the NPC keeps track of a hostile it is watching that goes still in the dark.
    /// </summary>
    [DataField]
    public TimeSpan DarkTrackTime = TimeSpan.FromSeconds(2.5);

    /// <summary>
    ///     A watched hostile moving at least this fast in the dark stays tracked: the NPC follows the movement.
    /// </summary>
    [DataField]
    public float TrackSpeed = 0.5f;

    #endregion

    #region Inference

    /// <summary>
    ///     How far ahead of where a lost hostile was last seen, in seconds of its last velocity, the NPC guesses it
    ///         has got to.
    /// </summary>
    [DataField]
    public TimeSpan DeadReckoningTime = TimeSpan.FromSeconds(3);

    /// <summary>
    ///     A hostile that vanishes in plain sight within this many tiles of a closed locker is suspected to have
    ///         hidden in it.
    /// </summary>
    [DataField]
    public float SuspicionRange = 1.25f;

    /// <summary>
    ///     How often a hostile in sight is called out to the squad.
    /// </summary>
    [DataField]
    public TimeSpan CalloutInterval = TimeSpan.FromSeconds(1);

    #endregion

    [ViewVariables]
    public TimeSpan NextUpdate;

    [ViewVariables]
    public TimeSpan NextCallout;

    /// <summary>
    ///     Everything the NPC currently knows of, by hostile.
    /// </summary>
    [ViewVariables]
    public Dictionary<EntityUid, NpcContact> Contacts = new();

    /// <summary>
    ///     Hostiles the NPC knows to be dead - it saw the body, saw it die, killed it, or a squadmate told it - and
    ///         when it stops remembering that (after <see cref="MemoryTime"/>). A hostile that died where nobody saw
    ///         is not in here: as far as anyone knows, it is still out there. See <c>NpcPerceptionSystem.ConfirmDead</c>.
    /// </summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> KnownDead = new();

    /// <summary>
    ///     When this NPC confirmed a death its squad cared about, if it has not called it out yet. See
    ///         <c>TargetDownPrecondition</c>.
    /// </summary>
    [ViewVariables]
    public TimeSpan? ConfirmedKillAt;
}
