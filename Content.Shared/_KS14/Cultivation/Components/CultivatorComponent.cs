using Content.Shared.Alert;
using Content.Shared._KS14.Cultivation.Prototypes;
using Content.Shared._KS14.Cultivation.Systems;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._KS14.Cultivation.Components;

/// <summary>
///     Someone walking the path of cultivation: they gather qi, sit in meditation to gain insight, and break through
///         from realm to realm. The first law they learn is their Root Law and shapes one of its techniques; from
///         Foundation Establishment they may hold a second, Complementary Law.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(SharedCultivationSystem))]
public sealed partial class CultivatorComponent : Component
{
    /// <summary>
    ///     The alert that shows how much qi is held.
    /// </summary>
    [DataField]
    public ProtoId<AlertPrototype> QiAlert = "KsQi";

    /// <summary>
    ///     The alert that shows internal injuries, one severity step per stack.
    /// </summary>
    [DataField]
    public ProtoId<AlertPrototype> InjuryAlert = "KsInternalInjury";

    /// <summary>
    ///     Qi currently held in the dantian. Techniques spend it.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Qi = 50f;

    [DataField, AutoNetworkedField]
    public CultivationRealm Realm = CultivationRealm.QiCondensation;

    /// <summary>
    ///     Progress towards the next breakthrough.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Insight;

    /// <summary>
    ///     Laws known, the Root Law first.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<ProtoId<CultivationLawPrototype>> Laws = new();

    /// <summary>
    ///     Stacks of internal injury from heavy blows. Each one slows qi recovery and risks a backlash when spending qi.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int InternalInjury;

    [DataField]
    public int MaxInternalInjury = 3;

    /// <summary>
    ///     A single blow at or above this much damage bruises the meridians.
    /// </summary>
    [DataField]
    public float HeavyBlowThreshold = 15f;

    /// <summary>
    ///     Qi capacity at each realm, indexed by <see cref="CultivationRealm"/>.
    /// </summary>
    [DataField]
    public List<float> MaxQiByRealm = new() { 100f, 200f, 350f, 500f };

    /// <summary>
    ///     Insight needed to break out of each realm, indexed by <see cref="CultivationRealm"/>. Past the peak of
    ///         Nascent Soul, the last entry is what Ascension needs.
    /// </summary>
    [DataField]
    public List<float> InsightByRealm = new() { 375f, 875f, 1625f, 2750f };

    [DataField]
    public float QiRegenPerSecond = 1f;

    /// <summary>
    ///     Fraction of qi recovery lost per stack of internal injury.
    /// </summary>
    [DataField]
    public float InjuryRegenPenalty = 0.25f;

    /// <summary>
    ///     Chance per stack of internal injury that spending qi twists the meridians.
    /// </summary>
    [DataField]
    public float BacklashChancePerInjury = 0.1f;

    [DataField]
    public TimeSpan MeditateDuration = TimeSpan.FromSeconds(8);

    [DataField]
    public float MeditateInsight = 20f;

    [DataField]
    public float MeditateQi = 25f;

    /// <summary>
    ///     Every this many completed meditations, one stack of internal injury heals.
    /// </summary>
    [DataField]
    public int MeditationsPerInjuryHeal = 2;

    [DataField]
    public int MeditationsSinceHeal;

    /// <summary>
    ///     Each breakthrough is endured in cycles of this long while heaven's lightning falls.
    /// </summary>
    [DataField]
    public TimeSpan BreakthroughCycle = TimeSpan.FromSeconds(3);

    /// <summary>
    ///     Readiness before anything else is counted. 70 or more always succeeds.
    /// </summary>
    [DataField]
    public int BaseReadiness = 40;

    [DataField]
    public int ReadinessPerInjury = 20;

    /// <summary>
    ///     Cycles left in a running breakthrough, and its readiness.
    /// </summary>
    [DataField]
    public int BreakthroughCyclesLeft;

    [DataField]
    public int BreakthroughReadiness;

    /// <summary>
    ///     Beat your own heart demon and the next breakthrough is steadier.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool DaoHeartTempered;

    /// <summary>
    ///     Forced Circulation: meditation gathers twice the insight, at the risk of injury every cycle.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool ForcedCirculation;

    [DataField]
    public float ForcedCirculationRisk = 0.25f;

    [DataField]
    public TimeSpan ReadManualDuration = TimeSpan.FromSeconds(5);

    [DataField]
    public EntProtoId MeditateAction = "ActionCultivationMeditate";

    [DataField]
    public EntityUid? MeditateActionEntity;

    /// <summary>
    ///     Whether the Meditate button is showing that a breakthrough is ready.
    /// </summary>
    [DataField]
    public bool ReadyGlow;

    /// <summary>
    ///     When they last used a technique: qi stays unsettled for a minute after.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan LastTechniqueAt;

    /// <summary>
    ///     When they last broke through: the new foundation takes a few minutes to steady.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan LastBreakthroughAt;

    /// <summary>
    ///     Technique actions granted by laws, keyed by the action prototype so each is granted once.
    /// </summary>
    [DataField]
    public Dictionary<EntProtoId, EntityUid> TechniqueActions = new();

    /// <summary>
    ///     A manual of one of these is handed over when someone first becomes a cultivator. Empty for none.
    /// </summary>
    [DataField]
    public List<EntProtoId> StartingManuals = new();

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextRegen;
}
