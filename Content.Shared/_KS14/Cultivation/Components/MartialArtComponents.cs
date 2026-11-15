using Content.Shared._KS14.Cultivation.Prototypes;
using Content.Shared._KS14.Cultivation.Systems;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._KS14.Cultivation.Components;

/// <summary>
///     Someone who has learned martial arts from manuals.
/// </summary>
[RegisterComponent]
[Access(typeof(SharedMartialArtsSystem))]
public sealed partial class MartialArtistComponent : Component
{
    [DataField]
    public List<ProtoId<MartialArtPrototype>> Arts = new();

    /// <summary>
    ///     How many body arts anyone can hold.
    /// </summary>
    [DataField]
    public int MaxBodyArts = 2;

    [DataField]
    public Dictionary<EntProtoId, EntityUid> Actions = new();
}

/// <summary>
///     A manual teaching one martial art.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class MartialArtManualComponent : Component
{
    [DataField(required: true)]
    public ProtoId<MartialArtPrototype> Art;
}

/// <summary>
///     Practises a dark art. Others can feel it.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DarkArtsComponent : Component;

/// <summary>
///     Nine Yang Manual: yang qi floods the meridians. Qi recovers faster, it takes a harder blow to cause an
///         internal injury, and injuries heal on their own over time.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentPause]
public sealed partial class NineYangComponent : Component
{
    [DataField]
    public float RegenMultiplier = 1.5f;

    [DataField]
    public float HeavyBlowBonus = 10f;

    [DataField]
    public TimeSpan HealInterval = TimeSpan.FromSeconds(60);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextHeal;
}

/// <summary>
///     Muscle-Tendon Changing Classic: the sinews are remade. Stamina damage is reduced, and for a cultivator it
///         takes a harder blow to cause an internal injury.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class MuscleTendonComponent : Component
{
    [DataField]
    public float StaminaDamageMultiplier = 0.7f;

    [DataField]
    public float HeavyBlowBonus = 5f;
}

/// <summary>
///     Golden Bell Shield: the skin turns away physical blows for a while.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(CultivationStanceSystem))]
public sealed partial class GoldenBellComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan EndTime;

    [DataField, AutoNetworkedField]
    public float DamageMultiplier = 0.6f;
}

/// <summary>
///     Graceful Waves Steps: light, quick footwork.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(CultivationStanceSystem))]
public sealed partial class GracefulStepsComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan EndTime;

    [DataField, AutoNetworkedField]
    public float SpeedMultiplier = 1.35f;
}

/// <summary>
///     Soft Hedgehog Armor: barbs under the silk wound anyone who strikes the wearer up close.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SoftHedgehogArmorComponent : Component
{
    [DataField]
    public float ThornDamage = 6f;

    [DataField]
    public float Range = 1.5f;
}

/// <summary>
///     One piece of the Great Sage's regalia. Wearing every piece at once wakes the set.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class GreatSageRegaliaComponent : Component;

/// <summary>
///     Wearing the whole of the Great Sage's regalia: blows land softer, and a cultivator's qi flows faster.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class GreatSageComponent : Component
{
    [DataField]
    public float DamageMultiplier = 0.85f;

    [DataField]
    public float QiRegenMultiplier = 1.25f;
}
