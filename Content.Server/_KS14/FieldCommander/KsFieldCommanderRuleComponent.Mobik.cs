namespace Content.Server._KS14.FieldCommander;

/// <summary>
///     The ending: once the defenders have beaten the last wave, the commander who failed to take their stronghold is
///         relieved of command, mobilised, and sent in himself as one conscript, a "mobik". Killing him wins the round
///         for the defenders, or leads into the vote on endless mode. Off unless <see cref="Mobik"/> is set.
/// </summary>
public sealed partial class KsFieldCommanderRuleComponent
{
    /// <summary>
    ///     Who is sent in, and how. Null leaves the ending as it was: the round ends, or the vote opens, as soon as the
    ///         uplink is taken out.
    /// </summary>
    [DataField]
    public KsFieldCommanderMobik? Mobik;

    /// <summary>
    ///     How far through the ending the round is. Read-only in VV: each step is the rule system's to take.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public KsFieldCommanderMobikStage MobikStage;

    /// <summary>
    ///     When his pod is due, once he is announced. On the commander's clock, so a pause holds it.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan? MobikArrivalTime;

    /// <summary>
    ///     The mobik in the field: kept up to date when he is taken off the field for being stuck and sent again.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? MobikUid;

    /// <summary>
    ///     Whether the defenders killed him, for the round end summary.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public bool MobikKilled;

    /// <summary>
    ///     The next of <see cref="KsFieldCommanderMobik.Aftermath"/> to take.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public int AftermathStepIndex;

    /// <summary>
    ///     When it is due. On the commander's clock, so a pause holds it.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan? NextAftermathTime;

    /// <summary>
    ///     The lights the aftermath turned off, turned back on if the rule ends while the round goes on.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public List<EntityUid> AftermathLightsOff = [];
}

/// <summary>
///     The failed commander, sent in at the end. See <see cref="KsFieldCommanderRuleComponent.Mobik"/>.
/// </summary>
[DataDefinition]
public sealed partial class KsFieldCommanderMobik
{
    /// <summary>
    ///     What is sent: a unit like any of the commander's, with its kill points and stand-off, but never bought from
    ///         a wave's budget. Always comes by pod, whatever its <see cref="KsFieldCommanderAsset.Delivery"/>.
    /// </summary>
    [DataField(required: true)]
    public KsFieldCommanderAsset Unit = new();

    /// <summary>
    ///     The command announcement that he has been relieved and mobilised, sent as he is due.
    /// </summary>
    [DataField]
    public LocId? Announcement = "ks-fieldcommander-mobik-announcement";

    /// <summary>
    ///     Who the announcement is signed by.
    /// </summary>
    [DataField]
    public LocId Sender = "ks-fieldcommander-mobik-sender";

    /// <summary>
    ///     The defenders' intelligence on him, a few seconds after the announcement.
    /// </summary>
    [DataField]
    public LocId? Intel = "ks-fieldcommander-intel-mobik";

    /// <summary>
    ///     The defenders' intelligence once he is dead.
    /// </summary>
    [DataField]
    public LocId? KilledIntel = "ks-fieldcommander-intel-mobik-killed";

    /// <summary>
    ///     From the announcement to his pod.
    /// </summary>
    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(20);

    /// <summary>
    ///     Points every living defender gets when he dies, on top of the kill's own.
    /// </summary>
    [DataField]
    public int BonusPoints = 300;

    /// <summary>
    ///     Steps taken in order once he is dead, each after its own delay, before the round may end. Units a step sends
    ///         have to be dealt with too. Empty, and the round ends at his death.
    /// </summary>
    [DataField]
    public List<KsFieldCommanderAftermathStep> Aftermath = [];
}

/// <summary>
///     One step of <see cref="KsFieldCommanderMobik.Aftermath"/>: any of turning the lights out, an announcement, and
///         groups of units sent in.
/// </summary>
[DataDefinition]
public sealed partial class KsFieldCommanderAftermathStep
{
    /// <summary>
    ///     After the step before it, or after his death for the first.
    /// </summary>
    [DataField]
    public TimeSpan Delay;

    /// <summary>
    ///     Turns off every powered light on the defenders' map that is on. Emergency lights and lights carried or worn
    ///         are not powered lights, and stay on.
    /// </summary>
    [DataField]
    public bool LightsOut;

    [DataField]
    public KsFieldCommanderAftermathAnnouncement? Announcement;

    /// <summary>
    ///     Each group arrives together on foot, out of the defenders' sight, with nothing said; the groups at bearings
    ///         spread evenly around the defenders. They are the commander's units, ordered at the defenders.
    /// </summary>
    [DataField]
    public List<KsFieldCommanderAftermathGroup> Groups = [];
}

/// <summary>
///     An announcement on the defenders' intelligence channel, written into the prototype rather than a locale file.
/// </summary>
[DataDefinition]
public sealed partial class KsFieldCommanderAftermathAnnouncement
{
    /// <summary>
    ///     The text by culture name (<c>en-US</c>): the server's culture's, or else the first. Formatted on the server,
    ///         as every one of the commander's announcements is.
    /// </summary>
    [DataField(required: true)]
    public Dictionary<string, string> Text = new();

    /// <summary>
    ///     Who it is signed by; the rule's <see cref="KsFieldCommanderRuleComponent.IntelSender"/> if unset.
    /// </summary>
    [DataField]
    public LocId? Sender;

    /// <summary>
    ///     Whether the intelligence chirp plays under it.
    /// </summary>
    [DataField]
    public bool Sound = true;
}

[DataDefinition]
public sealed partial class KsFieldCommanderAftermathGroup
{
    /// <summary>
    ///     What the group is made of: each unit as many times as its <see cref="KsFieldCommanderAsset.Count"/>.
    /// </summary>
    [DataField(required: true)]
    public List<KsFieldCommanderAsset> Units = [];
}

public enum KsFieldCommanderMobikStage : byte
{
    /// <summary>
    ///     The round has not reached its end.
    /// </summary>
    None,

    /// <summary>
    ///     The uplink is down and the round is held open: no more waves, but the commander's units still in the field,
    ///         and any wave an admin queued, have to be dealt with first.
    /// </summary>
    AwaitingClear,

    /// <summary>
    ///     Announced; his pod is due at <see cref="KsFieldCommanderRuleComponent.MobikArrivalTime"/>.
    /// </summary>
    Announced,

    /// <summary>
    ///     In the field.
    /// </summary>
    Arrived,

    /// <summary>
    ///     Killed and paid for; on the commander's next tick the aftermath begins, or the round ends, or the vote opens.
    /// </summary>
    Killed,

    /// <summary>
    ///     Taking the aftermath's steps.
    /// </summary>
    Aftermath,

    /// <summary>
    ///     Every step taken; waiting for whatever they sent to be dealt with.
    /// </summary>
    AftermathClear,

    /// <summary>
    ///     Over: never again this round, endless mode included.
    /// </summary>
    Done,
}
