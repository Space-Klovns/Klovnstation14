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
///     Nine Swords of Dugu: waiting for the opening in the next blow.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentPause]
public sealed partial class DuguCounterComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan EndTime;

    [DataField]
    public float CounterDamage = 15f;

    [DataField]
    public TimeSpan Knockdown = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     Only blows from this close can be read and broken.
    /// </summary>
    [DataField]
    public float Range = 2f;
}

/// <summary>
///     Great Shift of Heaven and Earth: incoming projectiles are turned back on whoever loosed them.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentPause]
public sealed partial class GreatShiftComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan EndTime;

    [DataField]
    public int Charges = 3;
}

/// <summary>
///     Struck by the Eighteen Dragon-Subduing Palms: each further palm from the same hand hits harder.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class DragonPalmComboComponent : Component
{
    [DataField]
    public EntityUid? Striker;

    [DataField]
    public int Stacks;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan Expires;
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
