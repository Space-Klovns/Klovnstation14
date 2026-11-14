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
    ///     How much of the dark the NPC sees through, from 0 to 1, going by <see cref="NpcGetDarkVisionEvent"/>: 1 is as
    ///         if everything were fully lit. See <see cref="NpcPerceptionSystem.RefreshDarkVision"/>.
    /// </summary>
    [ViewVariables]
    public float DarkVision;

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

    /// <summary>
    ///     Whether it sees through smoke, which stops everyone else's eyes as a wall does. Only for what sight goes by:
    ///         where it would stand to be hidden from a hostile still treats smoke as cover.
    /// </summary>
    [DataField]
    public bool SeesThroughSmoke;

    #region Darkness

    /// <summary>
    ///     Within this many tiles a hostile is seen however dark it is.
    /// </summary>
    [DataField]
    public float ProximityRange = 2.5f;

    /// <summary>
    ///     How dark a hostile has to be, as the light the NPC sees it in from 0 to 1, for its shots at it to go astray: it can
    ///         see where it is, not well enough to aim. None for an NPC that sees in the dark (<see cref="DarkVision"/>), or
    ///         at a hostile within <see cref="ProximityRange"/>. See <c>NPCCombatSystem.Klovn.DarkAim.cs</c>.
    /// </summary>
    [DataField]
    public float DarkAimLightLevel = 0.10f;

    /// <summary>
    ///     How far off the aim its shots may go at most, in pitch dark; less as the light rises to
    ///         <see cref="DarkAimLightLevel"/>. In degrees in YAML.
    /// </summary>
    [DataField]
    public Angle DarkAimSpread = Angle.FromDegrees(12);

    /// <summary>
    ///     How long its aim keeps the same error before it wanders to another: a burst goes the same way.
    /// </summary>
    [DataField]
    public TimeSpan DarkAimHold = TimeSpan.FromSeconds(0.6);

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
    ///         has got to. A squad's hunt keeps asking through its watch (<c>NpcSquadTacticsSystem.UpdatePrediction</c>),
    ///         so the guess moves on up to this long after the loss.
    /// </summary>
    [DataField]
    public TimeSpan DeadReckoningTime = TimeSpan.FromSeconds(5);

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

    #region Squad talk

    /// <summary>
    ///     Whether it tells its squad what it sees: squadmates that cannot see a hostile it can learn where it is, and
    ///         its main target becomes the squad's threat. Off, nothing it sees reaches its squad - it fights alone
    ///         until they see the hostile for themselves.
    /// </summary>
    [DataField]
    public bool CallsOutContacts = true;

    /// <summary>
    ///     Whether it takes in what squadmates call out. Off, it knows only what it sees itself: it carries on as it
    ///         was while a squadmate fights round the corner, and hunts a hostile its squad killed until it sees the
    ///         body.
    /// </summary>
    [DataField]
    public bool HearsCallouts = true;

    /// <summary>
    ///     Whether a death it confirms is passed on to its squad, so they stop counting the hostile as one. Off, each
    ///         squadmate finds out for itself, by seeing the body, and hunts it until then.
    /// </summary>
    [DataField]
    public bool SharesKills = true;

    /// <summary>
    ///     Whether a hostile that damages the NPC becomes known to it wherever it is: out of vision range, out of
    ///         sight, or in the dark. Then it turns and fires back, and cannot be sniped from where it is unseen.
    ///         Off, it only fights what it can see for itself.
    /// </summary>
    [DataField]
    public bool RevealAttackers;

    /// <summary>
    ///     How long after its last hit an attacker stays known wherever it is. After that it is remembered like
    ///         any other hostile gone out of sight, for <see cref="MemoryTime"/>.
    /// </summary>
    [DataField]
    public TimeSpan RevealDuration = TimeSpan.FromSeconds(3);

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
