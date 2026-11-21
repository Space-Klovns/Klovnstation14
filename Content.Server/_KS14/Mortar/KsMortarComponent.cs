using System.Numerics;
using Content.Shared._KS14.FarSound;
using Content.Shared.NPC.Prototypes;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Mortar;

/// <summary>
///     A mortar: set up where it stands, never moved, and fired either by the crewman who set it up
///         (<see cref="KsMortarCrewComponent"/>) or, once it has none, by anyone through its window. Every shell is a supply pod
///         (<c>SupplyPodComponent</c>) spawned where it is to land, the way the cruise missiles are: it falls for its fall
///         duration with its landing zone marked on the ground, and goes off as it lands. It has <see cref="Rounds"/> shells
///         and no more.
/// </summary>
/// <remarks>
///     The crewman fires every <see cref="Interval"/>, while it stands by the mortar, at the hostile between
///         <see cref="MinRange"/> and <see cref="MaxRange"/> most worth hitting: one more for each other hostile within
///         <see cref="ClusterRadius"/>, and a priority target (a turret, <c>KsNpcTargetPriority</c>) above the rest. Its crewman
///         dead or gone, the mortar is abandoned: it joins <see cref="AbandonedFactions"/>, which its old side is hostile to,
///         becomes a priority target, and, with <see cref="DissolveWhenAbandoned"/>, dissolves after <see cref="IdleTime"/>
///         without anyone using it. One put down with no crewman at all (by an admin, or on a map) is anyone's from the start.
/// </remarks>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(KsMortarSystem))]
public sealed partial class KsMortarComponent : Component
{
    /// <summary>
    ///     What is spawned where the shell is to land: a supply pod that goes off on landing.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId Shell;

    /// <summary>
    ///     Shells left. Every shot spends one, whoever fires it.
    /// </summary>
    [DataField]
    public int Rounds = 20;

    [DataField]
    public float MinRange = 30f;

    [DataField]
    public float MaxRange = 50f;

    /// <summary>
    ///     How often the crewman fires.
    /// </summary>
    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(12);

    /// <summary>
    ///     How soon after a shot anyone may fire it again through its window.
    /// </summary>
    [DataField]
    public TimeSpan ManualCooldown = TimeSpan.FromSeconds(6);

    [DataField]
    public float ClusterRadius = 3f;

    /// <summary>
    ///     What a target's <c>KsNpcTargetPriority</c> is worth, in hostiles of cluster: a turret at 0.8 with this at 5 counts
    ///         as four more.
    /// </summary>
    [DataField]
    public float PriorityWeight = 5f;

    /// <summary>
    ///     How far from where it was aimed, at most, a shell lands, in tiles: the crewman's and anyone else's alike.
    /// </summary>
    [DataField]
    public float Spread = 1.5f;

    /// <summary>
    ///     How near the mortar its crewman has to be to fire it, and how slowly it has to be moving.
    /// </summary>
    [DataField]
    public float CrewRange = 2f;

    [DataField]
    public float MaxCrewSpeed = 0.5f;

    /// <summary>
    ///     The thump at the mortar on every launch.
    /// </summary>
    [DataField]
    public SoundSpecifier? FireSound;

    /// <summary>
    ///     The launch as heard from far off, by everyone in its range.
    /// </summary>
    [DataField]
    public FarSoundData? FarFireSound;

    /// <summary>
    ///     Played where the shell is to land as it is fired: the shell on its way.
    /// </summary>
    [DataField]
    public SoundSpecifier? TravelSound;

    /// <summary>
    ///     How long after it is fired the shell lands, its landing zone marked all the while.
    /// </summary>
    [DataField]
    public TimeSpan FlightTime = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     The least and most of its flight the shell is seen falling for: as long as the incoming clip picked for it, kept
    ///         between the two, and never longer than <see cref="FlightTime"/>. Before that it is nowhere to be seen, its
    ///         landing zone marked all the same.
    /// </summary>
    [DataField]
    public TimeSpan MinDescentTime = TimeSpan.FromSeconds(1);

    /// <inheritdoc cref="MinDescentTime"/>
    [DataField]
    public TimeSpan MaxDescentTime = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     Played where the shell is to land, starting as long before it lands as the clip picked lasts, so that it ends as it
    ///         lands. A collection: one is picked for each shell.
    /// </summary>
    [DataField]
    public SoundSpecifier? IncomingSound;

    /// <summary>
    ///     A shell going in, when someone fires it through its window.
    /// </summary>
    [DataField]
    public SoundSpecifier? LoadSound;

    /// <summary>
    ///     Who it belongs to once abandoned: who its old side shoots at.
    /// </summary>
    [DataField]
    public HashSet<ProtoId<NpcFactionPrototype>> AbandonedFactions = new();

    /// <summary>
    ///     Its <c>KsNpcTargetPriority</c> once abandoned.
    /// </summary>
    [DataField]
    public float AbandonedPriority = 0.9f;

    /// <summary>
    ///     Whether it dissolves once nobody is using it (<see cref="IdleTime"/>, <see cref="EmptyTime"/>), as one left behind
    ///         by an NPC crew should. Off, it stays until destroyed, as one put down by hand should.
    /// </summary>
    [DataField]
    public bool DissolveWhenAbandoned;

    /// <summary>
    ///     How long an abandoned mortar lasts with nobody using it, with <see cref="DissolveWhenAbandoned"/>. Firing it, or
    ///         opening its window, starts this again.
    /// </summary>
    [DataField]
    public TimeSpan IdleTime = TimeSpan.FromMinutes(3);

    /// <summary>
    ///     How long an abandoned mortar with no rounds left lasts.
    /// </summary>
    [DataField]
    public TimeSpan EmptyTime = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     The crewman that set it up, while it has one alive.
    /// </summary>
    [ViewVariables]
    public EntityUid? CrewUid;

    [DataField, AutoPausedField]
    public TimeSpan NextFire;

    /// <summary>
    ///     How many of its last impacts it keeps for its window to show (<see cref="Impacts"/>); zero keeps none.
    /// </summary>
    [DataField]
    public int MaxImpacts = 5;

    /// <summary>
    ///     Where its last <see cref="MaxImpacts"/> shells landed, relative to it, on the map's axes, oldest first: the fall
    ///         of shot its window plots.
    /// </summary>
    [ViewVariables]
    public List<Vector2> Impacts = new();

    /// <summary>
    ///     When an abandoned mortar dissolves, unless someone uses it first. Null while it is crewed, and for one that never
    ///         dissolves.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan? IdleEndsAt;
}

/// <summary>
///     A mortar crewman: it sets <see cref="Mortar"/> up where it has come to a stop with a hostile in the mortar's range,
///         once, and fires it from there for as long as it stands by it (<see cref="KsMortarComponent"/>). Without one it is
///         whatever else it is.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(KsMortarSystem))]
public sealed partial class KsMortarCrewComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Mortar;

    /// <summary>
    ///     How long it has to have stood still before it sets up.
    /// </summary>
    [DataField]
    public TimeSpan SetUpTime = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     When it last came to a stop, while it has not set up; null while it moves.
    /// </summary>
    [ViewVariables, AutoPausedField]
    public TimeSpan? StillSince;

    /// <summary>
    ///     When it next looks for something to set up against, having found nothing: the search covers the mortar's whole
    ///         range, too much to run every tick.
    /// </summary>
    [ViewVariables, AutoPausedField]
    public TimeSpan NextTargetSearch;

    /// <summary>
    ///     Whether it has set its mortar up. It only has the one.
    /// </summary>
    [ViewVariables]
    public bool Deployed;

    [ViewVariables]
    public EntityUid? MortarUid;
}

/// <summary>
///     Raised on a crewman when it sets its mortar up.
/// </summary>
[ByRefEvent]
public readonly record struct KsMortarDeployedEvent(EntityUid MortarUid);

/// <summary>
///     Raised on a crewman whose mortar is gone: it is no crew any more.
/// </summary>
[ByRefEvent]
public readonly record struct KsMortarLostEvent;

/// <summary>
///     A mortar shell on its way, until its incoming sound has played.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(KsMortarSystem))]
public sealed partial class KsMortarShellComponent : Component
{
    [ViewVariables]
    public ResolvedSoundSpecifier? IncomingSound;

    [ViewVariables, AutoPausedField]
    public TimeSpan IncomingAt;
}

/// <summary>
///     A mortar shell, for as long as it is on its way: which mortar fired it, for that mortar to record where it lands.
/// </summary>
[RegisterComponent, Access(typeof(KsMortarSystem))]
public sealed partial class KsMortarFallOfShotComponent : Component
{
    [ViewVariables]
    public EntityUid MortarUid;
}
