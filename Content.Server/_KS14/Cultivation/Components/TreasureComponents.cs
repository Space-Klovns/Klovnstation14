using Content.Server._KS14.Cultivation.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._KS14.Cultivation.Components;

/// <summary>
///     A heavenly treasure in bloom: a spirit herb every cultivator on the station can feel. The first to dig it up
///         claims it; left alone, it fades.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(HeavenlyTreasureSystem))]
public sealed partial class SpiritHerbPlantComponent : Component
{
    [DataField]
    public EntProtoId Herb = "KsSpiritHerb";

    [DataField]
    public TimeSpan PickTime = TimeSpan.FromSeconds(4);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextPetals;
}

/// <summary>
///     A picked spirit herb, steeped in heavenly qi. Eaten, it fills a cultivator's foundation, tempers a body
///         cultivator's flesh, and heals anyone.
/// </summary>
[RegisterComponent, Access(typeof(HeavenlyTreasureSystem))]
public sealed partial class SpiritHerbComponent : Component
{
    [DataField]
    public float Heal = 50f;

    [DataField]
    public int InjuryHeal = 3;

    [DataField]
    public float Insight = 40f;

    /// <summary>
    ///     At least this share of the insight the eater's realm needs.
    /// </summary>
    [DataField]
    public float InsightShare = 0.1f;

    [DataField]
    public float Tempering = 25f;
}

/// <summary>
///     An ancestral ring with an ancient master asleep in its stone. A cultivator can wake them, and while a player
///         lives in it, its bearer learns faster from their scolding.
/// </summary>
[RegisterComponent, Access(typeof(AncestralRingSystem))]
public sealed partial class AncestralRingComponent : Component
{
    /// <summary>
    ///     A ghost has been asked to wake in it.
    /// </summary>
    [DataField]
    public bool Awakened;

    /// <summary>
    ///     How much faster the bearer learns while the master is awake and watching.
    /// </summary>
    [DataField]
    public float InsightMultiplier = 1.25f;

    [DataField]
    public List<EntProtoId> ElderActions = new() { "ActionKsRingElderSpeak", "ActionKsRingElderLendQi" };
}
