using Content.Shared.Mobs;
using Content.Shared._KS14.FieldCommander;
using Content.Shared._KS14.Scenario.Components;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.FieldCommander;

/// <summary>
///     An AI Syndicate field commander that besieges a scenario's defenders: a timeline of phases, each sending
///         waves of units bought from a threat budget that grows with the phase and with the number of defenders
///         alive, and ending in an uplink the defenders must take out before it calls down a bombardment.
/// </summary>
[RegisterComponent, Access(typeof(KsFieldCommanderRuleSystem))]
public sealed partial class KsFieldCommanderRuleComponent : Component
{
    #region Sides

    /// <summary>
    ///     The commander's side. Every unit it sends joins it.
    /// </summary>
    [DataField]
    public ProtoId<ScenarioFactionPrototype> Faction = "Syndicate";

    /// <summary>
    ///     The side being besieged: its living members are counted to scale waves, and hunted.
    /// </summary>
    [DataField]
    public ProtoId<ScenarioFactionPrototype> DefenderFaction = "Nanotrasen";

    #endregion

    #region Timeline

    /// <summary>
    ///     In order of <see cref="KsFieldCommanderPhase.Start"/>.
    /// </summary>
    [DataField(required: true)]
    public List<KsFieldCommanderPhase> Phases = [];

    /// <summary>
    ///     Everything the commander can send. A wave buys from the entries whose
    ///         <see cref="KsFieldCommanderAsset.MinPhase"/> has been reached.
    /// </summary>
    [DataField]
    public List<KsFieldCommanderAsset> Assets = [];

    #endregion

    #region Scaling

    /// <summary>
    ///     How many of the commander's units may be alive at once, before counting defenders.
    /// </summary>
    [DataField]
    public int MaxAlive = 6;

    /// <summary>
    ///     How many more units may be alive at once for each living defender.
    /// </summary>
    [DataField]
    public float MaxAlivePerDefender = 1.5f;

    /// <summary>
    ///     Never more units alive at once than this, whatever the head count, so that waves stay inside what the NPC
    ///         update budget can run.
    /// </summary>
    [DataField]
    public int HardMaxAlive = 36;

    [DataField]
    public float MinAdaptiveMultiplier = 0.75f;

    [DataField]
    public float MaxAdaptiveMultiplier = 1.3f;

    /// <summary>
    ///     Multiplied into the adaptive multiplier when the defenders lost nobody since the last wave.
    /// </summary>
    [DataField]
    public float AdaptiveStepUp = 1.1f;

    /// <summary>
    ///     Multiplied into the adaptive multiplier when the defenders lost at least
    ///         <see cref="HeavyLossFraction"/> of their number since the last wave.
    /// </summary>
    [DataField]
    public float AdaptiveStepDown = 0.85f;

    [DataField]
    public float HeavyLossFraction = 0.25f;

    #endregion

    #region Placement

    /// <summary>
    ///     Nearest distance from the defenders' objectives that units on foot appear at, and that the uplink lands at.
    /// </summary>
    [DataField]
    public float MinSpawnDistance = 28f;

    /// <summary>
    ///     Furthest distance from the defenders' objectives that anything is sent in at, the uplink included.
    /// </summary>
    [DataField]
    public float MaxSpawnDistance = 60f;

    /// <summary>
    ///     The uplink lands at least this share of <see cref="MaxSpawnDistance"/> out, so that reaching it is an
    ///         expedition.
    /// </summary>
    [DataField]
    public float UplinkDistanceFraction = 0.6f;

    /// <summary>
    ///     How far from the uplink its guards are placed.
    /// </summary>
    [DataField]
    public float UplinkGuardRadius = 4f;

    /// <summary>
    ///     How close to a living defender units on foot, and the uplink, may appear.
    /// </summary>
    [DataField]
    public float MinDefenderDistance = 16f;

    /// <summary>
    ///     How close to a living defender a pod may land.
    /// </summary>
    [DataField]
    public float MinPodDefenderDistance = 10f;

    /// <summary>
    ///     How close to a defender objective a pod may land.
    /// </summary>
    [DataField]
    public float MinPodObjectiveDistance = 15f;

    /// <summary>
    ///     How far a defender is assumed to see when deciding whether they would watch units appear.
    /// </summary>
    [DataField]
    public float DefenderSightRange = 30f;

    /// <summary>
    ///     How many random spots are tried when looking for somewhere to put something.
    /// </summary>
    [DataField]
    public int PlacementAttempts = 40;

    /// <summary>
    ///     The radius of the zones that nothing is sent into, struck or ordered towards, so that players always have
    ///         somewhere to come back to, and where nobody may build. Units can still walk in on their own. Used around
    ///         each defender spawn point, and for any <see cref="KsFieldCommanderSafeZoneComponent"/> that sets none.
    /// </summary>
    [DataField]
    public float SafeZoneRadius = 8f;

    #endregion

    #region Fog

    /// <summary>
    ///     Radius of the play area around the defenders' base. Beyond it lies a fog that hurts defenders, and that the
    ///         commander's units are sent in from. Zero for no fog.
    /// </summary>
    [DataField]
    public float FogRange;

    /// <summary>
    ///     How far beyond the fog's edge units on foot appear, so that nobody can camp the edge for them.
    /// </summary>
    [DataField]
    public float FogSpawnMargin = 8f;

    /// <summary>
    ///     How far past the uplink the fog lifts when it lands, so that the defenders can fight around it.
    /// </summary>
    [DataField]
    public float FogUplinkMargin = 12f;

    #endregion

    #region Delivery

    /// <summary>
    ///     The pod that carries units in.
    /// </summary>
    [DataField]
    public EntProtoId PodPrototype = "SupplyPodCargoBaseSyndicate";

    [DataField]
    public int MaxPerPod = 4;

    /// <summary>
    ///     The most units that set off together from one point on foot.
    /// </summary>
    [DataField]
    public int MaxPerGroup = 4;

    /// <summary>
    ///     How often the commander points its units at the defenders again. A unit that loses track of its target
    ///         settles down where it is.
    /// </summary>
    [DataField]
    public TimeSpan OrderInterval = TimeSpan.FromSeconds(20);

    #endregion

    #region Strikes

    /// <summary>
    ///     The missile pod a strike drops on the defenders' works.
    /// </summary>
    [DataField]
    public EntProtoId StrikePrototype = "SupplyPodMissileSyndicateExplosive";

    /// <summary>
    ///     Extra room kept between a strike's impact and the safe zones and defender objectives, for its blast.
    /// </summary>
    [DataField]
    public float StrikeSafeMargin = 6f;

    #endregion

    #region Uplink

    /// <summary>
    ///     The objective the commander lands in its final phase. Destroying or capturing it wins the defenders the
    ///         round. If it lasts until its own <see cref="Robust.Shared.Spawners.TimedDespawnComponent"/> runs out,
    ///         the commander wins.
    /// </summary>
    [DataField]
    public EntProtoId? UplinkPrototype = "KsFieldCommandUplink";

    #endregion

    #region Announcements

    [DataField]
    public LocId Sender = "ks-comms-console-announcement-title-field-commander";

    [DataField]
    public Color AnnouncementColor = Color.Red;

    /// <summary>
    ///     The commander's march, played under its transmissions for the moments that matter: phases, objectives, the
    ///         uplink. Its routine taunts go out silently.
    /// </summary>
    [DataField]
    public SoundSpecifier MajorSound = new SoundPathSpecifier("/Audio/_KS14/Announcements/fieldcommander.ogg");

    // The commander only ever taunts: everything the defenders can act on comes from their own side's intelligence,
    //      a few seconds later, as a reply.

    /// <summary>
    ///     The commander taunts with one of these with each wave.
    /// </summary>
    [DataField]
    public List<LocId> WaveAnnouncements = [];

    /// <summary>
    ///     Intelligence on each wave. Takes <c>count</c> and <c>direction</c>.
    /// </summary>
    [DataField]
    public LocId? WaveIntel = "ks-fieldcommander-intel-wave";

    /// <summary>
    ///     The commander taunts with one of these with each strike.
    /// </summary>
    [DataField]
    public List<LocId> StrikeAnnouncements = [];

    [DataField]
    public LocId? StrikeIntel = "ks-fieldcommander-intel-strike";

    /// <summary>
    ///     The commander's gloat when a defender objective falls.
    /// </summary>
    [DataField]
    public LocId? ObjectiveLostAnnouncement = "ks-fieldcommander-objective-lost";

    /// <summary>
    ///     Takes <c>remaining</c>.
    /// </summary>
    [DataField]
    public LocId? ObjectiveLostIntel = "ks-fieldcommander-intel-objective-lost";

    /// <summary>
    ///     The commander's taunt when the uplink lands.
    /// </summary>
    [DataField]
    public LocId? UplinkAnnouncement = "ks-fieldcommander-uplink-deployed";

    /// <summary>
    ///     Takes <c>direction</c>, <c>distance</c> and <c>minutes</c>.
    /// </summary>
    [DataField]
    public LocId? UplinkIntel = "ks-fieldcommander-intel-uplink";

    /// <summary>
    ///     How long before the uplink's bombardment the defenders are warned.
    /// </summary>
    [DataField]
    public TimeSpan UplinkWarningBefore = TimeSpan.FromMinutes(2);

    /// <summary>
    ///     Takes <c>minutes</c>.
    /// </summary>
    [DataField]
    public LocId? UplinkWarningIntel = "ks-fieldcommander-intel-uplink-warning";

    /// <summary>
    ///     Sent when a phase drops the defenders a reinforcement pod.
    /// </summary>
    [DataField]
    public LocId? ReinforcementIntel = "ks-fieldcommander-intel-reinforcements";

    #endregion

    #region Intel

    // The defenders' own side on the air: everything they can act on comes from here, a few seconds after whatever
    //      the commander said about it.

    /// <summary>
    ///     How long after the rule starts the defenders' own command first comes on the air.
    /// </summary>
    [DataField]
    public TimeSpan BriefingDelay = TimeSpan.FromSeconds(6);

    /// <summary>
    ///     The defenders' briefing, sent once after <see cref="BriefingDelay"/>.
    /// </summary>
    [DataField]
    public LocId? BriefingAnnouncement = "ks-fieldcommander-briefing";

    /// <summary>
    ///     How long after the commander speaks the defenders' intelligence replies.
    /// </summary>
    [DataField]
    public TimeSpan IntelDelay = TimeSpan.FromSeconds(3);

    /// <summary>
    ///     Who every intelligence message, the briefing included, is signed by.
    /// </summary>
    [DataField]
    public LocId IntelSender = "ks-fieldcommander-intel-sender";

    /// <summary>
    ///     Readable over snow and ice, where the defenders spend the round.
    /// </summary>
    [DataField]
    public Color IntelColor = Color.FromHex("#5B8CFF");

    /// <summary>
    ///     The radio chirp under intelligence, unless it follows one of the commander's major transmissions.
    /// </summary>
    [DataField]
    public SoundSpecifier IntelSound = new SoundPathSpecifier("/Audio/_KS14/Announcements/confirmdropoff.ogg");

    #endregion

    #region Points

    /// <summary>
    ///     The key the defenders' points are kept under on their <see cref="Content.Shared._RMC14.Vendors.CMVendorUserComponent"/>,
    ///         and so the <c>pointsType</c> of a vendor that sells for them.
    /// </summary>
    [DataField]
    public string PointsType = Content.Shared._KS14.FieldCommander.KsFieldCommanderPoints.PointsType;

    /// <summary>
    ///     Points a defender starts the round with.
    /// </summary>
    [DataField]
    public int StartingPoints;

    /// <summary>
    ///     Points per unit of <see cref="KsFieldCommanderAsset.Cost"/> for killing a unit, unless the asset sets its own
    ///         <see cref="KsFieldCommanderAsset.KillPoints"/>.
    /// </summary>
    [DataField]
    public float PointsPerCost = 25f;

    /// <summary>
    ///     Points for killing a fellow defender. Negative.
    /// </summary>
    [DataField]
    public int TeamkillPoints = -150;

    /// <summary>
    ///     The least a teamkill costs the defenders behind it, between them, however small their share of what killed
    ///         the victim: <see cref="TeamkillPoints"/> is scaled by that share, down to this. Below zero, like it.
    /// </summary>
    [DataField]
    public int TeamkillMinimumPoints = -15;

    /// <summary>
    ///     The flash of static over a defender as they go into each state that has one, which every defender is given:
    ///         black as they die.
    /// </summary>
    [DataField]
    public Dictionary<MobState, KsFieldCommanderDownFlashSettings> DownFlashes = new()
    {
        [MobState.Critical] = new(),
        [MobState.Dead] = new()
        {
            Color = Color.FromHex("#000000D0"),
            Duration = TimeSpan.FromSeconds(0.5),
        },
    };

    /// <summary>
    ///     Points every living defender gets when the uplink is taken out.
    /// </summary>
    [DataField]
    public int UplinkPoints = 200;

    [DataField]
    public SoundSpecifier PointsGainedSound = new SoundPathSpecifier("/Audio/Effects/kaching.ogg", AudioParams.Default.WithVolume(-2f));

    [DataField]
    public SoundSpecifier PointsLostSound = new SoundPathSpecifier("/Audio/Machines/buzz-two.ogg", AudioParams.Default.WithVolume(-1f));

    #endregion

    #region State

    [ViewVariables]
    public int PhaseIndex = -1;

    /// <summary>
    ///     Added to the time since the rule started when the timeline is walked, so that a phase set by hand stays set.
    /// </summary>
    [ViewVariables]
    public TimeSpan TimeOffset;

    [ViewVariables]
    public bool BriefingSent;

    /// <summary>
    ///     The map the fog is on, once it is up.
    /// </summary>
    [ViewVariables]
    public EntityUid? FogMapUid;

    /// <summary>
    ///     The map carrying the wave status the defenders' HUD reads.
    /// </summary>
    [ViewVariables]
    public EntityUid? WaveStatusMapUid;

    [ViewVariables]
    public MapCoordinates FogOrigin;

    /// <summary>
    ///     The fog's radius now, which grows when the uplink lands.
    /// </summary>
    [ViewVariables]
    public float FogCurrentRange;

    /// <summary>
    ///     Intelligence waiting to go out, in the order it was queued.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public List<KsFieldCommanderPendingIntel> PendingIntel = [];

    /// <summary>
    ///     Every defender's points, by player rather than by body, so that they last through a death. Read-only in VV,
    ///         as <see cref="KsFieldCommanderRuleSystem.SetPoints"/> keeps each body's copy in step with it; admins use
    ///         the <c>ks_fcpoints</c> command.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public Dictionary<NetUserId, int> Points = new();

    /// <summary>
    ///     Every player who has been a defender this round, for telling whose side a player with no body is on.
    /// </summary>
    [ViewVariables]
    public HashSet<NetUserId> Defenders = new();

    [ViewVariables]
    public TimeSpan NextWaveTime = TimeSpan.MaxValue;

    [ViewVariables]
    public TimeSpan NextOrderTime;

    [ViewVariables]
    public float CarriedBudget;

    [ViewVariables]
    public float AdaptiveMultiplier = 1f;

    /// <summary>
    ///     Defenders that were dead when the last wave was sent, to tell how many died since.
    /// </summary>
    [ViewVariables]
    public int DeadDefendersAtLastWave;

    [ViewVariables]
    public int WavesSent;

    [ViewVariables]
    public int UnitsSent;

    [ViewVariables]
    public int UnitsKilled;

    [ViewVariables]
    public int ObjectivesLost;

    /// <summary>
    ///     Whether the battlefield is set up, which waits for the map with the defenders' objectives:
    ///         <list type="bullet">
    ///             <item>their objectives' own timers are off (the commander decides how a round ends, so an objective
    ///                 must not win it by simply lasting);</item>
    ///             <item>the commander's side counts as alive, and as holding an objective still to come;</item>
    ///             <item>the safe zones are found, and closed to building;</item>
    ///             <item>the defenders are readied, and the fog is up.</item>
    ///         </list>
    /// </summary>
    [ViewVariables]
    public bool Prepared;

    /// <summary>
    ///     The zones nothing is sent into: the map's <see cref="KsFieldCommanderSafeZoneComponent"/> markers if it has
    ///         any, or else the defenders' spawn points.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public List<KsFieldCommanderSafeZone> SafeZones = [];

    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? UplinkUid;

    [ViewVariables]
    public TimeSpan? UplinkDeployedTime;

    [ViewVariables]
    public TimeSpan? UplinkBombardmentTime;

    [ViewVariables]
    public bool UplinkWarned;

    /// <summary>
    ///     When the uplink was taken out, if it was.
    /// </summary>
    [ViewVariables]
    public TimeSpan? UplinkDestroyedTime;

    #endregion
}

/// <summary>
///     A circle nothing is sent into, struck or ordered towards. See <see cref="KsFieldCommanderRuleComponent.SafeZones"/>.
/// </summary>
public readonly record struct KsFieldCommanderSafeZone(MapCoordinates Center, float Radius);

/// <summary>
///     An intelligence message, already formatted, to send at <paramref name="At"/>.
/// </summary>
public readonly record struct KsFieldCommanderPendingIntel(TimeSpan At, string Message, bool Sound);

/// <summary>
///     One stage of the commander's timeline.
/// </summary>
[DataDefinition]
public sealed partial class KsFieldCommanderPhase
{
    /// <summary>
    ///     Name shown in the round end summary.
    /// </summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>
    ///     When this phase begins, counted from the start of the rule.
    /// </summary>
    [DataField]
    public TimeSpan Start;

    /// <summary>
    ///     Time between waves. Zero sends none.
    /// </summary>
    [DataField]
    public TimeSpan WaveInterval;

    /// <summary>
    ///     Delay before this phase's first wave.
    /// </summary>
    [DataField]
    public TimeSpan FirstWaveDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Threat budget of one wave against a single defender.
    /// </summary>
    [DataField]
    public float Budget;

    /// <summary>
    ///     How much of <see cref="Budget"/> each defender past the first adds.
    /// </summary>
    [DataField]
    public float BudgetPerExtraDefender = 0.35f;

    /// <summary>
    ///     Chance for each wave to come with a strike.
    /// </summary>
    [DataField]
    public float StrikeChance;

    /// <summary>
    ///     The commander's taunt when the phase begins, with <see cref="KsFieldCommanderRuleComponent.MajorSound"/>.
    /// </summary>
    [DataField]
    public LocId? Announcement;

    /// <summary>
    ///     The defenders' intelligence on what this phase brings.
    /// </summary>
    [DataField]
    public LocId? Intel;

    /// <summary>
    ///     A pod dropped to the defenders when the phase begins, as their reward for lasting this long.
    /// </summary>
    [DataField]
    public EntProtoId? ReinforcementPod;

    /// <summary>
    ///     Whether the uplink lands when this phase begins.
    /// </summary>
    [DataField]
    public bool DeployUplink;

    /// <summary>
    ///     Budget spent on units guarding the uplink when it lands.
    /// </summary>
    [DataField]
    public float UplinkGuardBudget;
}

public enum KsFieldCommanderDelivery : byte
{
    /// <summary>
    ///     Appears out of the defenders' sight and walks in, or comes by pod when there is nowhere out of sight.
    /// </summary>
    Foot,

    /// <summary>
    ///     Dropped in by supply pod.
    /// </summary>
    Pod,
}

/// <summary>
///     Something the commander can buy for a wave.
/// </summary>
[DataDefinition]
public sealed partial class KsFieldCommanderAsset
{
    /// <summary>
    ///     A random humanoid to spawn. Takes precedence over <see cref="Entity"/>.
    /// </summary>
    [DataField]
    public ProtoId<RandomHumanoidSettingsPrototype>? Humanoid;

    /// <summary>
    ///     Name a <see cref="Humanoid"/> gets, when its settings don't randomise one.
    /// </summary>
    [DataField]
    public LocId HumanoidName = "ks-fieldcommander-unit-name";

    /// <summary>
    ///     An entity to spawn, when there is no <see cref="Humanoid"/>.
    /// </summary>
    [DataField]
    public EntProtoId? Entity;

    /// <summary>
    ///     How many come for one purchase.
    /// </summary>
    [DataField]
    public int Count = 1;

    [DataField]
    public float Cost = 1f;

    /// <summary>
    ///     Points a defender gets for killing one of these. Unset is <see cref="Cost"/> times
    ///         <see cref="KsFieldCommanderRuleComponent.PointsPerCost"/>.
    /// </summary>
    [DataField]
    public int? KillPoints;

    [DataField]
    public float Weight = 1f;

    /// <summary>
    ///     Index of the first phase this can be bought in.
    /// </summary>
    [DataField]
    public int MinPhase;

    /// <summary>
    ///     How many of this may be alive at once. Zero for no limit.
    /// </summary>
    [DataField]
    public int MaxAlive;

    [DataField]
    public KsFieldCommanderDelivery Delivery = KsFieldCommanderDelivery.Foot;

    /// <summary>
    ///     Whether the commander keeps pointing this unit at the defenders. Units with a mind of their own, such as
    ///         a borg that already heads for the nearest objective, don't need it.
    /// </summary>
    [DataField]
    public bool GiveOrders = true;

    /// <summary>
    ///     The commander's taunt, sent once with each wave this is part of.
    /// </summary>
    [DataField]
    public LocId? Announcement;

    /// <summary>
    ///     The defenders' intelligence on it, sent once with each wave this is part of.
    /// </summary>
    [DataField]
    public LocId? Intel;
}
