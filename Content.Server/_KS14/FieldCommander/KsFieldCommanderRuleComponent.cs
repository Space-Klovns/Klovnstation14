using Content.Shared._KS14.Scenario.Components;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Audio;
using Robust.Shared.Map;
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
    ///     Nearest distance from the defenders' objectives that units on foot appear at.
    /// </summary>
    [DataField]
    public float MinSpawnDistance = 28f;

    /// <summary>
    ///     Furthest distance from the defenders' objectives that anything is sent in at.
    /// </summary>
    [DataField]
    public float MaxSpawnDistance = 60f;

    /// <summary>
    ///     How close to a living defender units on foot may appear.
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

    [DataField]
    public int PlacementAttempts = 40;

    /// <summary>
    ///     The radius around each defender spawn point that nothing is sent into, struck or ordered towards, so that
    ///         players always have somewhere to come back to. Units can still walk in on their own.
    /// </summary>
    [DataField]
    public float SafeZoneRadius = 8f;

    /// <summary>
    ///     Extra room kept between a strike's impact and the safe zone, for its blast.
    /// </summary>
    [DataField]
    public float StrikeSafeMargin = 6f;

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
    ///     What an airstrike drops.
    /// </summary>
    [DataField]
    public EntProtoId StrikePrototype = "SupplyPodMissileSyndicateExplosive";

    /// <summary>
    ///     How often the commander points its units at the defenders again. A unit that loses track of its target
    ///         settles down where it is.
    /// </summary>
    [DataField]
    public TimeSpan OrderInterval = TimeSpan.FromSeconds(20);

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
    ///     Played for routine transmissions: waves, strikes.
    /// </summary>
    [DataField]
    public SoundSpecifier RadioSound = new SoundPathSpecifier("/Audio/_KS14/Announcements/confirmdropoff.ogg");

    /// <summary>
    ///     Played for the moments that matter: phases, objectives, the uplink.
    /// </summary>
    [DataField]
    public SoundSpecifier MajorSound = new SoundPathSpecifier("/Audio/_KS14/Announcements/fieldcommander.ogg");

    /// <summary>
    ///     One of these is sent with each wave.
    /// </summary>
    [DataField]
    public List<LocId> WaveAnnouncements = [];

    /// <summary>
    ///     One of these is sent with each airstrike.
    /// </summary>
    [DataField]
    public List<LocId> StrikeAnnouncements = [];

    /// <summary>
    ///     Sent when a defender objective falls. Takes <c>remaining</c>.
    /// </summary>
    [DataField]
    public LocId? ObjectiveLostAnnouncement = "ks-fieldcommander-objective-lost";

    /// <summary>
    ///     Sent when the uplink lands. Takes <c>direction</c>, <c>distance</c> and <c>minutes</c>.
    /// </summary>
    [DataField]
    public LocId? UplinkAnnouncement = "ks-fieldcommander-uplink-deployed";

    /// <summary>
    ///     How long before the uplink's bombardment the defenders are warned.
    /// </summary>
    [DataField]
    public TimeSpan UplinkWarningBefore = TimeSpan.FromMinutes(2);

    [DataField]
    public LocId? UplinkWarningAnnouncement = "ks-fieldcommander-uplink-warning";

    #endregion

    #region State

    [ViewVariables]
    public int PhaseIndex = -1;

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
    ///     Whether the defender objectives have had their own timers taken off. The commander decides how a round
    ///         ends, so an objective must not win it by simply lasting.
    /// </summary>
    [ViewVariables]
    public bool ObjectivesPrepared;

    /// <summary>
    ///     Centres of the zones nothing is sent into, found from the defenders' spawn points.
    /// </summary>
    [ViewVariables]
    public List<KsFieldCommanderSafeZone> SafeZones = [];

    [ViewVariables]
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

public readonly record struct KsFieldCommanderSafeZone(MapCoordinates Center, float Radius);

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
    ///     Chance for each wave to come with an airstrike.
    /// </summary>
    [DataField]
    public float StrikeChance;

    /// <summary>
    ///     Sent when the phase begins, with <see cref="KsFieldCommanderRuleComponent.MajorSound"/>.
    /// </summary>
    [DataField]
    public LocId? Announcement;

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
    ///     Sent once with each wave this is part of.
    /// </summary>
    [DataField]
    public LocId? Announcement;
}
